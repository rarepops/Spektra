#!/usr/bin/env node

// Publishes the packages build.mjs assembled.
//
//     node packaging/npm/publish.mjs [--dry-run] [--out DIR]
//
// Every package is packed into a tarball first, and it is that exact file
// that gets published, so the bytes this script hashed are the bytes the
// registry stores. A version already on the registry is skipped only when its
// recorded integrity matches the tarball in hand: a re-run therefore finishes
// a half-finished release, while a second, different build of a version that
// is already public stops the release instead of quietly leaving the registry
// and the release page describing different code.
//
// Platform packages are published first and the dispatcher last. The reverse
// order leaves a window in which `npm install spektra-cli` resolves the
// dispatcher, finds no binary package for that version, and installs a tool
// that cannot run.
//
// Authentication: none of npm's credentials are handled here. In the release
// workflow npm authenticates through GitHub's OIDC token (trusted publishing)
// and attaches provenance by itself, which is why no --provenance flag is
// passed and no .npmrc is written.
//
// The one-off bootstrap publish from a machine is different: an account with
// 2FA has to send a one-time password with every publish, and `npm login
// --auth-type=web` does not change that on npm 10. Pass `--otp <code>`, which
// goes to each npm publish untouched. A code lasts about 30 seconds and each
// platform tarball is ~32 MB, so one code may not cover all five; that is what
// the integrity comparison is for, and a re-run with a fresh code continues.

import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { DISPATCHER, PLATFORM_PACKAGES } from './lib/targets.mjs';
import {
    MIN_NPM_VERSION,
    integrityOf,
    isStrictVersion,
    packFilenameOf,
    publishVerdict,
    readBackVerdict,
    sha1Of,
    supportsTrustedPublishing,
} from './lib/registry.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const REGISTRY = 'https://registry.npmjs.org';
const INTEGRITY_FILE = 'INTEGRITY.txt';

function fail(message) {
    console.error(`publish.mjs: ${message}`);
    process.exit(1);
}

const opts = { dryRun: false };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--dry-run') opts.dryRun = true;
    else if (argv[i] === '--out') opts.out = argv[++i];
    else if (argv[i] === '--otp') opts.otp = argv[++i];
    else if (argv[i].startsWith('--otp=')) opts.otp = argv[i].slice('--otp='.length);
    else fail(`unknown argument ${argv[i]}`);
}

const out = path.resolve(repo, opts.out ?? path.join('dist', 'npm'));
if (!existsSync(out)) fail(`${path.relative(repo, out)} does not exist. Run build.mjs first.`);

// --- what was built ---

const packages = readdirSync(out, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => {
        const manifest = path.join(out, e.name, 'package.json');
        if (!existsSync(manifest)) fail(`${e.name} has no package.json`);
        return { dir: path.join(out, e.name), manifest: JSON.parse(readFileSync(manifest, 'utf8')) };
    });

const dispatcher = packages.find((p) => p.manifest.name === DISPATCHER);
if (!dispatcher) fail(`no ${DISPATCHER} package in ${path.relative(repo, out)}`);
const version = dispatcher.manifest.version;

// A published version is forever, so the shape of the version is checked
// before anything is packed. Manual dry-run builds are stamped 0.0.0-dev and
// only ever dry-run.
if (!opts.dryRun && !isStrictVersion(version))
    fail(`"${version}" is not a release version. Only a bare X.Y.Z is published; this looks like a `
        + 'manual build, which is dry-run only.');

// A --local build points the dispatcher at sibling directories so it can be
// installed and run without a registry. Publishing that would ship a package
// whose dependencies cannot resolve on anyone else's machine.
for (const [name, spec] of Object.entries(dispatcher.manifest.optionalDependencies ?? {})) {
    if (spec !== version)
        fail(`${DISPATCHER} depends on ${name}@${spec}, not the exact version ${version}. `
            + 'This looks like a --local build; rebuild without --local.');
}

const platforms = packages.filter((p) => p.manifest.name !== DISPATCHER);
const pinned = Object.keys(dispatcher.manifest.optionalDependencies ?? {}).sort();
const present = platforms.map((p) => p.manifest.name).sort();
const expected = [...PLATFORM_PACKAGES].sort();
if (pinned.join() !== expected.join())
    fail(`${DISPATCHER} pins [${pinned.join(', ')}], expected [${expected.join(', ')}]`);
if (present.join() !== expected.join())
    fail(`the build holds [${present.join(', ')}], expected [${expected.join(', ')}]`);
for (const p of platforms) {
    if (p.manifest.version !== version)
        fail(`${p.manifest.name} is ${p.manifest.version} but ${DISPATCHER} is ${version}`);
}

// --- npm itself ---

function npm(args, options = {}) {
    // shell:true to find npm.cmd on Windows as well as npm on the runners.
    return spawnSync('npm', args, { shell: true, encoding: 'utf8', ...options });
}

const npmVersion = (npm(['--version']).stdout ?? '').trim();
if (!npmVersion) fail('could not run npm');

// GitHub sets these two when a job has id-token: write, and they are what npm
// exchanges for a registry token under trusted publishing. If they are here,
// this is a trusted-publishing run and npm has to be new enough to use them,
// or it would fall through to looking for a token that deliberately is not
// there and fail with a confusing 401 (or worse, publish unattested).
const oidcAvailable = Boolean(process.env.ACTIONS_ID_TOKEN_REQUEST_URL);
if (oidcAvailable && !opts.dryRun && !supportsTrustedPublishing(npmVersion))
    fail(`npm ${npmVersion} cannot publish through OIDC; ${MIN_NPM_VERSION} or newer is needed. `
        + 'Raise the node-version in the workflow.');

// --- pack everything, then publish ---

/// Packs one package and returns the tarball with its integrity. `npm pack`
/// writes the file; --json tells us the name it chose rather than guessing at
/// npm's naming rules.
function pack(pkg) {
    const result = npm(['pack', '--json', '--pack-destination', out], { cwd: pkg.dir });
    if (result.status !== 0) {
        process.stderr.write(result.stderr ?? '');
        fail(`npm pack failed for ${pkg.manifest.name}`);
    }
    let filename;
    try {
        filename = packFilenameOf(result.stdout);
    } catch (e) {
        fail(`could not read npm pack output for ${pkg.manifest.name}: ${e.message}\n${result.stdout}`);
    }
    const tarball = path.join(out, filename);
    if (!existsSync(tarball)) fail(`npm pack reported ${filename} but it is not in ${path.relative(repo, out)}`);
    const bytes = readFileSync(tarball);
    return {
        ...pkg,
        filename,
        tarball,
        size: bytes.length,
        integrity: integrityOf(bytes),
        sha1: sha1Of(bytes),
    };
}

// Order matters from here on: platform packages, then the dispatcher.
const ordered = [...PLATFORM_PACKAGES.map((name) => platforms.find((p) => p.manifest.name === name)), dispatcher];

console.log(`Packing ${DISPATCHER} ${version} from ${path.relative(repo, out)}`);
const packed = ordered.map(pack);
const size = (bytes) => bytes < 1048576 ? `${Math.round(bytes / 1024)} kB` : `${(bytes / 1048576).toFixed(1)} MB`;
for (const p of packed) console.log(`  ${p.filename.padEnd(40)} ${size(p.size).padStart(8)}  ${p.integrity}`);

// The same integrity strings the registry will report, written next to the
// tarballs so a manual build can be checked and a publish can be audited.
writeFileSync(path.join(out, INTEGRITY_FILE),
    `${packed.map((p) => `${p.integrity}  ${p.filename}`).join('\n')}\n`);
console.log(`  -> ${INTEGRITY_FILE}`);

/// The registry's record of this exact version, or null when it has none.
async function registryDist(name, wanted) {
    const response = await fetch(`${REGISTRY}/${encodeURIComponent(name)}`);
    if (response.status === 404) return null;
    if (!response.ok) fail(`registry answered ${response.status} for ${name}`);
    const body = await response.json();
    return body.versions?.[wanted]?.dist ?? null;
}

function publish(pkg) {
    const args = ['publish', pkg.filename, '--access', 'public'];
    if (opts.otp) args.push('--otp', opts.otp);
    if (opts.dryRun) args.push('--dry-run');
    // The tarball is named relative to its own directory, so no path with a
    // space has to survive the shell that shell:true introduces.
    const result = npm(args, { cwd: out, stdio: 'inherit', encoding: undefined });
    if (result.status !== 0)
        fail(`npm publish failed for ${pkg.manifest.name}.`
            + (oidcAvailable
                ? ' If the registry answered 403 or 404, check that this package names this repository'
                + ' and release.yml as its trusted publisher on npmjs.com; see packaging/npm/PUBLISHING.md.'
                : ' If the registry answered EOTP, a one-time password is required or the one given has'
                + ' expired: run this again with a fresh --otp <code>. Anything already published with'
                + ' these exact bytes is skipped, so a re-run picks up where this stopped.'));
}

// Reading a publish back. npm serves a package a little after it accepts it,
// so `absent` has to be waited on rather than failed on; two minutes has been
// seen for real. The wait is bounded because the other thing npm does is
// accept a publish it will never serve, and a release that hangs forever is no
// better than one that lies.
const READ_BACK_ATTEMPTS = 12;
const READ_BACK_WAIT_MS = 15_000;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/// Refuses to move on until the registry actually serves what was just
/// published. Without this the publisher believes `npm publish`, which exits 0
/// and prints `+ name@version` for a package the registry then 404s, and the
/// dispatcher goes out naming a platform package nobody can install. That has
/// happened twice to the same name, so this is not defensive programming.
async function confirmPublished(pkg) {
    const name = pkg.manifest.name;
    for (let attempt = 1; attempt <= READ_BACK_ATTEMPTS; attempt++) {
        const verdict = readBackVerdict({
            local: { integrity: pkg.integrity, sha1: pkg.sha1 },
            dist: await registryDist(name, version),
        });
        if (verdict === 'present') {
            console.log(`  readable on the registry${attempt > 1 ? ` after ${attempt} checks` : ''}`);
            return;
        }
        if (verdict === 'different')
            fail(`${name}@${version} is on the registry with contents other than the tarball just published.`);
        if (attempt < READ_BACK_ATTEMPTS) {
            if (attempt === 1) console.log('  waiting for the registry to serve it');
            await sleep(READ_BACK_WAIT_MS);
        }
    }
    const waited = Math.round((READ_BACK_ATTEMPTS - 1) * READ_BACK_WAIT_MS / 1000);
    fail(`npm accepted ${name}@${version} and is still not serving it ${waited} seconds later.`
        + ' This is not slow propagation: npm can take a publish, report success, and never serve the'
        + ' package, which leaves the version unpublishable (403, versions are immutable) and'
        + ' uninstallable (404) at the same time. Nothing here can repair it. Publish the platform'
        + ' binary under a new package name and open a support ticket for this one; see'
        + ' packaging/npm/PUBLISHING.md.');
}

console.log(`\nPublishing to ${REGISTRY}${opts.dryRun ? ' (dry run)' : ''}`);
if (!opts.dryRun)
    console.log(oidcAvailable
        ? `  npm ${npmVersion}, authenticating through OIDC; provenance is attached by npm`
        : `  npm ${npmVersion}, using the credentials npm is already configured with`);

let published = 0;
let skipped = 0;
for (const pkg of packed) {
    const name = pkg.manifest.name;
    const verdict = publishVerdict({
        name,
        version,
        local: { integrity: pkg.integrity, sha1: pkg.sha1 },
        dist: await registryDist(name, version),
    });

    if (verdict.action === 'fail') fail(verdict.reason);
    if (verdict.action === 'skip') {
        console.log(`\n${name}@${version} is already published with these exact bytes, skipping.`);
        skipped++;
        continue;
    }

    console.log(`\n${name}@${version}`);
    publish(pkg);
    published++;
    // Every package is read back before the next one goes out, so the
    // dispatcher can never be published naming a platform package the registry
    // is not serving. A dry run publishes nothing, so there is nothing to read.
    if (!opts.dryRun) await confirmPublished(pkg);
}

console.log(`\n${published} published, ${skipped} already there.`);
if (!opts.dryRun) console.log(`https://www.npmjs.com/package/${DISPATCHER}`);
