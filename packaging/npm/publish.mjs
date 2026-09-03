#!/usr/bin/env node

// Publishes the packages build.mjs assembled, in the one order that works:
// every platform package first, the `spektra-cli` dispatcher last. The
// dispatcher pins its optional dependencies to an exact version, so
// publishing it first leaves a window in which `npm install spektra-cli`
// resolves the dispatcher, finds no binary package for that version, and
// installs a tool that cannot run.
//
//     node packaging/npm/publish.mjs [--dry-run] [--provenance] [--out DIR]
//
// A version already on the registry is skipped rather than failed, so a
// re-run after a half-finished publish finishes the job. Authentication is
// whatever npm itself is configured with (`npm login`, or NODE_AUTH_TOKEN in
// a workflow); this script never handles a token.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const DISPATCHER = 'spektra-cli';

function fail(message) {
    console.error(`publish.mjs: ${message}`);
    process.exit(1);
}

const opts = { dryRun: false, provenance: false };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--dry-run') opts.dryRun = true;
    else if (argv[i] === '--provenance') opts.provenance = true;
    else if (argv[i] === '--out') opts.out = argv[++i];
    else fail(`unknown argument ${argv[i]}`);
}

const out = path.resolve(repo, opts.out ?? path.join('dist', 'npm'));
if (!existsSync(out)) fail(`${path.relative(repo, out)} does not exist. Run build.mjs first.`);

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
if (pinned.join() !== present.join())
    fail(`${DISPATCHER} pins [${pinned.join(', ')}] but the build holds [${present.join(', ')}]`);
for (const p of platforms) {
    if (p.manifest.version !== version)
        fail(`${p.manifest.name} is ${p.manifest.version} but ${DISPATCHER} is ${version}`);
}

/// True when this exact name@version is already on the public registry. Read
/// straight from the registry rather than through `npm view`, which reports a
/// missing package and a missing version with the same exit code.
async function alreadyPublished(name, wanted) {
    const response = await fetch(`https://registry.npmjs.org/${encodeURIComponent(name)}`);
    if (response.status === 404) return false;
    if (!response.ok) fail(`registry answered ${response.status} for ${name}`);
    const body = await response.json();
    return Object.hasOwn(body.versions ?? {}, wanted);
}

function publish(pkg) {
    const args = ['publish', '--access', 'public'];
    if (opts.provenance) args.push('--provenance');
    if (opts.dryRun) args.push('--dry-run');
    // Run from the package directory rather than naming it: shell:true is
    // needed to find npm.cmd on Windows, and a path argument would then have
    // to survive the shell's own word splitting.
    const result = spawnSync('npm', args, { stdio: 'inherit', shell: true, cwd: pkg.dir });
    if (result.status !== 0) fail(`npm publish failed for ${pkg.manifest.name}`);
}

console.log(`Publishing ${DISPATCHER} ${version}${opts.dryRun ? ' (dry run)' : ''}`);
for (const pkg of [...platforms, dispatcher]) {
    const name = pkg.manifest.name;
    if (!opts.dryRun && await alreadyPublished(name, version)) {
        console.log(`\n${name}@${version} is already published, skipping.`);
        continue;
    }
    console.log(`\n${name}@${version}`);
    publish(pkg);
}
console.log(`\nDone. https://www.npmjs.com/package/${DISPATCHER}`);
