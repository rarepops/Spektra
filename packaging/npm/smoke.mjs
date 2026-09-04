#!/usr/bin/env node

// Post-publish smoke test, run once per platform on a clean runner. Installs
// the published version the way a user would and proves three things that only
// a real install can show: the dispatcher resolves, the ONE platform package
// for this machine came with it and no other did, and the binary runs and
// reports the version that was released.
//
//     node packaging/npm/smoke.mjs --version 0.24.1 --expect @rarepops/spektra-cli-win32-x64
//
// Flags:
//   --version X.Y.Z  the version to install and the version the binary must report
//   --expect NAME    the only platform package that may be installed
//   --spec SPEC      install this instead of spektra-cli@<version> (a local
//                    tarball, for proving this script without a registry)
//   --keep           leave the temporary install behind for inspection

import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';

import { DISPATCHER, PLATFORM_PACKAGES, targetFor } from './lib/targets.mjs';

const opts = {};
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--keep') opts.keep = true;
    else if (['--version', '--expect', '--spec'].includes(argv[i])) opts[argv[i].slice(2)] = argv[++i];
    else die(`unknown argument ${argv[i]}`);
}

function die(message) {
    console.error(`\nsmoke: ${message}`);
    process.exit(1);
}

if (!opts.version) die('--version is required');

// Default to what this machine should get, so a matrix entry that names the
// wrong package fails here rather than passing by accident.
const expected = opts.expect ?? targetFor(process.platform, process.arch)?.pkg;
if (!expected) die(`no platform package exists for ${process.platform}-${process.arch}`);

const spec = opts.spec ?? `${DISPATCHER}@${opts.version}`;
const dir = mkdtempSync(path.join(os.tmpdir(), 'spektra-npm-smoke-'));

// A package.json of its own, so npm treats this directory as the project
// rather than walking up and finding whatever it finds above the temp dir.
writeFileSync(path.join(dir, 'package.json'),
    `${JSON.stringify({ name: 'spektra-cli-smoke', version: '0.0.0', private: true }, null, 2)}\n`);

console.log(`smoke: ${process.platform}-${process.arch}, expecting ${expected}`);
console.log(`  install ${spec}`);
console.log(`  in      ${dir}`);

function npm(args, options = {}) {
    return spawnSync('npm', args, { cwd: dir, shell: true, encoding: 'utf8', ...options });
}

const modules = path.join(dir, 'node_modules');
const ATTEMPTS = 6;
// Synchronous wait, so the retry needs no async restructuring.
const wait = () => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 15000);

// A version published seconds ago is not always visible to the next install,
// and the wait can run to minutes. Two symptoms, and only one of them is an
// error: the dispatcher itself 404s, or the dispatcher installs cleanly and
// its platform dependency is quietly skipped, because a missing OPTIONAL
// dependency is not a failure to npm and `npm install` still exits 0. Retrying
// only the first is how a released macOS package that was merely two minutes
// late got reported as a broken release. So the whole assertion is retried,
// not the install.
//
// --prefer-online because a retry that reads npm's cached 404 learns nothing.
let installed = null;
let platformPackages = [];
for (let attempt = 1; attempt <= ATTEMPTS; attempt++) {
    const last = attempt === ATTEMPTS;
    installed = npm(['install', spec, '--no-audit', '--no-fund', '--prefer-online', '--loglevel', 'error']);
    if (installed.status !== 0) {
        const output = `${installed.stdout ?? ''}${installed.stderr ?? ''}`;
        if (last || !/E404|404 Not Found/.test(output)) {
            process.stderr.write(output);
            die(`npm install ${spec} failed`);
        }
        console.log(`  not on the registry yet (attempt ${attempt}), waiting 15s`);
        wait();
        continue;
    }
    platformPackages = [...new Set(platformPackagesIn(modules))].sort();
    if (platformPackages.length === 1 && platformPackages[0] === expected) break;
    if (last) break;
    console.log(`  ${expected} did not come with it (attempt ${attempt}), waiting 15s`);
    wait();
}

// --- the dispatcher ---

const dispatcherManifest = path.join(modules, DISPATCHER, 'package.json');
if (!existsSync(dispatcherManifest)) die(`${DISPATCHER} is not in node_modules`);
const dispatcherVersion = JSON.parse(readFileSync(dispatcherManifest, 'utf8')).version;
if (dispatcherVersion !== opts.version)
    die(`installed ${DISPATCHER} is ${dispatcherVersion}, expected ${opts.version}`);
console.log(`  ok      ${DISPATCHER}@${dispatcherVersion} installed`);

// --- exactly one platform package ---

/// Every installed package whose name marks it as a platform binary, wherever
/// npm put it: hoisted to the top level, or nested under the dispatcher.
///
/// A symlink counts as a package. A registry install extracts real
/// directories, but npm links a `file:` dependency instead, and isDirectory()
/// is false for a link: reading only real directories would report "no
/// platform package installed" for a local build while passing in CI.
function platformPackagesIn(root, depth = 0) {
    if (depth > 3 || !existsSync(root)) return [];
    const found = [];
    const platformPackages = new Set(PLATFORM_PACKAGES);
    const collect = (name, packageRoot) => {
        if (platformPackages.has(name)) found.push(name);
        found.push(...platformPackagesIn(path.join(packageRoot, 'node_modules'), depth + 1));
    };
    for (const entry of readdirSync(root, { withFileTypes: true })) {
        if (entry.name.startsWith('.')) continue;
        if (!entry.isDirectory() && !entry.isSymbolicLink()) continue;
        const packageRoot = path.join(root, entry.name);
        if (entry.name.startsWith('@')) {
            for (const scoped of readdirSync(packageRoot, { withFileTypes: true })) {
                if (!scoped.isDirectory() && !scoped.isSymbolicLink()) continue;
                collect(`${entry.name}/${scoped.name}`, path.join(packageRoot, scoped.name));
            }
        } else {
            collect(entry.name, packageRoot);
        }
    }
    return found;
}

if (platformPackages.length !== 1 || platformPackages[0] !== expected)
    die(`installed platform packages are [${platformPackages.join(', ')}], expected exactly [${expected}], `
        + `after ${ATTEMPTS} attempts. npm should install one binary per machine and skip the rest through `
        + 'os/cpu. An empty list here is npm declining to serve that package rather than a slow one: the '
        + 'dependency is optional, so npm skips it in silence and the install still succeeds.');
console.log(`  ok      only ${expected} came with it`);

// --- the binary runs ---

// Through node_modules/.bin, so the bin link npm created is exercised too and
// not just the file inside the package. On Windows that entry is a .cmd, which
// the shell resolves.
const bin = path.join(modules, '.bin', DISPATCHER);
const ran = spawnSync(bin, ['--version'], { cwd: dir, shell: true, encoding: 'utf8' });
if (ran.error) die(`could not run ${bin}: ${ran.error.message}`);
const printed = `${ran.stdout ?? ''}`.trim();
if (ran.status !== 0) {
    process.stderr.write(`${ran.stderr ?? ''}`);
    die(`${DISPATCHER} --version exited ${ran.status}`);
}
if (!printed.includes(opts.version))
    die(`${DISPATCHER} --version printed "${printed}", which does not name ${opts.version}`);
console.log(`  ok      ${printed}`);

if (!opts.keep) rmSync(dir, { recursive: true, force: true });
console.log(`smoke: ${process.platform}-${process.arch} passed`);
