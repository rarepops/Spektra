#!/usr/bin/env node

// Assembles the npm packages for spektra-cli out of a finished CLI publish.
//
//     dotnet publish src/Spektra.Cli -c Release -r <rid> ... -o dist/cli-<rid>
//     node packaging/npm/build.mjs --version 0.24.1
//
// Output is one directory per package under dist/npm, ready for `npm publish`
// (see publish.mjs): the dispatcher `spektra-cli`, plus one package per
// platform holding that platform's binary and nothing else. The dispatcher
// lists them as optional dependencies pinned to the exact same version, and
// npm installs the single one whose os/cpu match the machine.
//
// Flags:
//   --version X.Y.Z  version to stamp (default: <Version> in Directory.Build.props)
//   --dist DIR       where the cli-<rid> publish directories are (default: dist)
//   --out DIR        where to write the packages (default: <dist>/npm)
//   --local          build only the platforms present and point the optional
//                    dependencies at sibling directories, so the result can be
//                    installed and run without a registry. NEVER publishable;
//                    publish.mjs refuses a tree built this way.

import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { TARGETS } from './lib/targets.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');

function fail(message) {
    console.error(`build.mjs: ${message}`);
    process.exit(1);
}

function args() {
    const out = { local: false };
    const argv = process.argv.slice(2);
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === '--local') out.local = true;
        else if (a === '--version' || a === '--dist' || a === '--out') out[a.slice(2)] = argv[++i];
        else fail(`unknown argument ${a}`);
    }
    return out;
}

/// The single version the whole repo builds under, so a local run needs no
/// arguments and cannot disagree with the assemblies.
function repoVersion() {
    const props = readFileSync(path.join(repo, 'Directory.Build.props'), 'utf8');
    const m = props.match(/<Version>([^<]+)<\/Version>/);
    if (!m) fail('no <Version> in Directory.Build.props');
    return m[1].trim();
}

/// The most recent edit to anything the CLI is built from. A publish
/// directory older than this holds a binary from before that edit.
///
/// Not paranoia: dist/ is gitignored and survives everything, so a machine
/// that once published four runtime identifiers keeps three stale binaries
/// forever. Stamping one of those with today's version publishes weeks-old
/// code under a version number that never contained it, and the --version
/// check above cannot see it, because a Linux binary cannot be run here.
function newestInputMs() {
    let newest = 0;
    for (const file of ['Directory.Build.props', 'global.json']) {
        const p = path.join(repo, file);
        if (existsSync(p)) newest = Math.max(newest, statSync(p).mtimeMs);
    }
    const walk = (dir) => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.name === 'bin' || entry.name === 'obj') continue;
            const p = path.join(dir, entry.name);
            if (entry.isDirectory()) walk(p);
            else newest = Math.max(newest, statSync(p).mtimeMs);
        }
    };
    walk(path.join(repo, 'src'));
    return newest;
}

const shared = {
    homepage: 'https://github.com/rarepops/Spektra#readme',
    bugs: { url: 'https://github.com/rarepops/Spektra/issues' },
    repository: { type: 'git', url: 'git+https://github.com/rarepops/Spektra.git' },
    license: 'SEE LICENSE IN LICENSE.md',
    author: 'Rares (rarepops)',
    engines: { node: '>=18' },
};

function platformManifest(target, version) {
    return {
        name: target.pkg,
        version,
        description: `${target.label} binary for spektra-cli. Installed automatically by the spektra-cli package.`,
        ...shared,
        os: [target.os],
        cpu: target.cpu,
        // A bin entry under the package's unscoped basename, which cannot collide with
        // the dispatcher's `spektra-cli`. It exists for its side effect: npm
        // sets mode 0o755 on a bin target when it links it, and these
        // tarballs are packed on Windows, which has no executable bit to
        // preserve. bin/spektra-cli.js chmods as a fallback.
        bin: { [target.dir]: target.exe },
        files: [target.exe],
        // Yarn Berry keeps this package unpacked rather than zipped, so the
        // binary is a real file on disk that can be executed.
        preferUnplugged: true,
    };
}

function write(file, text) {
    mkdirSync(path.dirname(file), { recursive: true });
    writeFileSync(file, text.endsWith('\n') ? text : `${text}\n`);
}

function writeJson(file, value) {
    write(file, JSON.stringify(value, null, 2));
}

/// Proves the binary was published under the version being stamped. Only the
/// host's own platform can be run, which on the release runner is win-x64.
function checkStampedVersion(exe, version, target) {
    if (target.os !== process.platform || !target.cpu.includes(process.arch)) return;
    const result = spawnSync(exe, ['--version'], { encoding: 'utf8' });
    if (result.error) fail(`could not run ${exe}: ${result.error.message}`);
    const printed = `${result.stdout}`.trim();
    if (!printed.includes(version))
        fail(`${target.rid} binary reports "${printed}" but the packages are being stamped ${version}. `
            + 'Re-publish the CLI with -p:Version.');
    console.log(`  ${target.rid}: ${printed}`);
}

const opts = args();
const version = opts.version ?? repoVersion();
const dist = path.resolve(repo, opts.dist ?? 'dist');
const out = path.resolve(repo, opts.out ?? path.join(dist, 'npm'));

// Loose on purpose: a manual workflow_dispatch build stamps something like
// 0.0.0-dev to exercise the packaging without a tag. publish.mjs is the gate
// that refuses anything but a bare X.Y.Z for a real publish.
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version)) fail(`"${version}" is not a version`);

rmSync(out, { recursive: true, force: true });
console.log(`Assembling spektra-cli ${version} npm packages in ${path.relative(repo, out)}`);

const newestInput = newestInputMs();
const built = [];
const missing = [];
const stale = [];
for (const target of TARGETS) {
    const exe = path.join(dist, `cli-${target.rid}`, target.exe);
    if (!existsSync(exe)) {
        missing.push(target);
        continue;
    }
    if (statSync(exe).mtimeMs < newestInput) {
        stale.push(target);
        continue;
    }
    checkStampedVersion(exe, version, target);
    const dir = path.join(out, target.dir);
    mkdirSync(dir, { recursive: true });
    copyFileSync(exe, path.join(dir, target.exe));
    copyFileSync(path.join(repo, 'LICENSE.md'), path.join(dir, 'LICENSE.md'));
    writeJson(path.join(dir, 'package.json'), platformManifest(target, version));
    write(path.join(dir, 'README.md'), [
        `# ${target.pkg}`,
        '',
        `The ${target.label} build of Spektra's command-line tool.`,
        '',
        'This package carries a binary and nothing else. Install',
        '[spektra-cli](https://www.npmjs.com/package/spektra-cli) instead: it',
        'depends on this one and npm picks the right build for your machine.',
    ].join('\n'));
    built.push(target);
}

const rids = (list) => list.map((t) => t.rid).join(', ');
if (stale.length)
    console.log(`  stale, ignored: ${rids(stale)} (published before the newest change under src/)`);
if (missing.length)
    console.log(`  not published: ${rids(missing)}`);
if ((missing.length || stale.length) && !opts.local)
    fail(`incomplete: ${rids([...missing, ...stale])} ${missing.length + stale.length === 1 ? 'has' : 'have'} no current publish `
        + `under ${path.relative(repo, dist)}. Publish every runtime identifier from this source, or pass --local `
        + 'to build only what is there (which cannot be published).');
if (built.length === 0) fail(`no current CLI publish under ${path.relative(repo, dist)}`);

// The dispatcher. Its optional dependencies are pinned to this exact version:
// a range would let npm pair a new dispatcher with an old binary.
const manifest = JSON.parse(readFileSync(path.join(here, 'spektra-cli.package.json'), 'utf8'));
manifest.version = version;
// --local uses an absolute file: spec with forward slashes on purpose, and
// both halves of that matter. npm resolves a relative spec against the
// project being installed into rather than against the package that declares
// it, so `file:../<pkg>` is looked for beside the consumer; and a spec
// carrying Windows backslashes is not read as a path at all. Either way the
// lookup fails and, the dependency being optional, npm skips it in silence
// and the tool installs without a binary.
const localSpec = (target) => `file:${path.join(out, target.dir).replaceAll(path.sep, '/')}`;
manifest.optionalDependencies = Object.fromEntries(built.map((t) =>
    [t.pkg, opts.local ? localSpec(t) : version]));

const main = path.join(out, 'spektra-cli');
mkdirSync(path.join(main, 'bin'), { recursive: true });
copyFileSync(path.join(here, 'bin', 'spektra-cli.js'), path.join(main, 'bin', 'spektra-cli.js'));
copyFileSync(path.join(repo, 'LICENSE.md'), path.join(main, 'LICENSE.md'));
copyFileSync(path.join(here, 'README.md'), path.join(main, 'README.md'));
writeJson(path.join(main, 'package.json'), manifest);

console.log(`\n${built.length + 1} packages:`);
for (const t of built) console.log(`  ${t.pkg.padEnd(30)} ${t.os}/${t.cpu.join(',')}`);
console.log(`  ${'spektra-cli'.padEnd(30)} dispatcher -> ${opts.local ? 'sibling directories' : version}`);
if (opts.local) console.log('\n--local build: for installing and running here, not for publishing.');
