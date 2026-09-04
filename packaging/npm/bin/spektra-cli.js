#!/usr/bin/env node
'use strict';

// Entry point of the `spektra-cli` npm package.
//
// The tool itself is a self-contained native binary, one per platform, each
// published as its own npm package and listed as an optional dependency of
// this one. npm installs only the package whose `os`/`cpu` match the machine,
// so an install downloads one binary and ignores the rest, with no
// postinstall script and nothing fetched from the network at install time.
// This file finds the binary npm chose and hands it the whole command line,
// so `npx spektra-cli audit Music` behaves exactly like the binary from the
// releases page: same stdio, same exit code.

const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const ISSUES = 'https://github.com/rarepops/Spektra/issues';

// `${process.platform}-${process.arch}` -> the package carrying that binary.
// Windows on ARM is deliberately pointed at the x64 build: Windows 11 runs
// x64 user-mode binaries under emulation, which is slower than a native
// build but far better than "no build for your machine". The win32 package
// therefore allows both CPUs; every other entry is an exact match.
const PACKAGES = {
    'win32-x64': '@rarepops/spektra-cli-win32-x64',
    'win32-arm64': '@rarepops/spektra-cli-win32-x64',
    'linux-x64': '@rarepops/spektra-cli-linux-x64-glibc',
    'darwin-x64': '@rarepops/spektra-cli-darwin-x64',
    'darwin-arm64': '@rarepops/spektra-cli-darwin-arm64',
};

/// Which binary package this machine needs: `{ package }`, or `{ error }`
/// with a message to print. `libc` matters on Linux only.
function select(platform, arch, libc) {
    const pkg = PACKAGES[`${platform}-${arch}`];
    if (!pkg) return { error: unsupportedMessage(platform, arch) };
    // The Linux build is dynamically linked against glibc. npm has no idea,
    // it only checks os/cpu, so the package installs on Alpine and then dies
    // with a bare "not found" from the loader. Say why instead.
    if (platform === 'linux' && libc === 'musl') return { error: muslMessage() };
    return { package: pkg };
}

function binaryName(platform) {
    return platform === 'win32' ? 'spektra-cli.exe' : 'spektra-cli';
}

/// Where the binary lives, or null when npm skipped the optional dependency.
/// Resolving `<pkg>/package.json` rather than a module entry point keeps the
/// platform packages free of JavaScript; they must not gain an `exports`
/// field, which would stop this subpath resolving.
function binaryPath(pkg, platform, resolve = require.resolve) {
    try {
        const manifest = resolve(`${pkg}/package.json`);
        return path.join(path.dirname(manifest), binaryName(platform));
    } catch (e) {
        if (e && e.code === 'MODULE_NOT_FOUND') return null;
        throw e;
    }
}

/// Node reports the glibc it is linked against; on musl (Alpine) that field
/// is absent. Same signal `detect-libc` reads, without the dependency.
function libcOf(report) {
    return report && report.header && report.header.glibcVersionRuntime ? 'glibc' : 'musl';
}

function currentLibc() {
    if (process.platform !== 'linux') return null;
    try {
        return libcOf(process.report.getReport());
    } catch {
        // No report available: assume the common case rather than refuse.
        return 'glibc';
    }
}

function unsupportedMessage(platform, arch) {
    return [
        `spektra-cli: there is no prebuilt binary for ${platform}-${arch}.`,
        'Prebuilt: Windows x64 (and ARM64 under emulation), Linux x64 (glibc), macOS x64 and arm64.',
        `Build it from source with .NET, or ask for your platform at ${ISSUES}`,
    ].join('\n');
}

function muslMessage() {
    return [
        'spektra-cli: the Linux binary needs glibc, so it will not run on musl.',
        'Alpine and other musl systems are not covered by a published build. Use a',
        'glibc image such as node:22-slim or debian, or build from source with .NET.',
        `Want a musl build? Say so at ${ISSUES}`,
    ].join('\n');
}

function missingPackageMessage(pkg) {
    return [
        `spektra-cli: the binary package ${pkg} is not installed.`,
        'It is an optional dependency, so this is what an install run with',
        '--no-optional or --omit=optional leaves behind, and what a part-way',
        'failed install looks like. Either reinstall spektra-cli, or add the',
        `binary on its own:  npm install ${pkg}`,
    ].join('\n');
}

/// npm sets mode 0o755 on a package's declared bin when it links it, which is
/// why each platform package declares one: the tarballs are packed on
/// Windows, where there is no executable bit to preserve. If that has not
/// happened (a tarball unpacked by hand, a package manager that skips bin
/// links), fix the mode here rather than fail with a bare EACCES.
function ensureExecutable(bin) {
    if (process.platform === 'win32') return;
    try {
        fs.accessSync(bin, fs.constants.X_OK);
    } catch {
        try {
            fs.chmodSync(bin, 0o755);
        } catch {
            // Reported by the spawn below, with the path in the message.
        }
    }
}

function main(argv) {
    const chosen = select(process.platform, process.arch, currentLibc());
    if (chosen.error) {
        console.error(chosen.error);
        return 1;
    }

    const bin = binaryPath(chosen.package, process.platform);
    if (bin === null || !fs.existsSync(bin)) {
        console.error(missingPackageMessage(chosen.package));
        return 1;
    }

    ensureExecutable(bin);
    const result = spawnSync(bin, argv, { stdio: 'inherit' });
    if (result.error) {
        console.error(`spektra-cli: could not run ${bin}: ${result.error.message}`);
        return 1;
    }
    // A signalled child has no exit status. Node cannot re-raise the signal
    // portably, so report a plain failure.
    if (result.signal) {
        console.error(`spektra-cli: stopped by ${result.signal}.`);
        return 1;
    }
    return result.status === null ? 1 : result.status;
}

if (require.main === module) process.exit(main(process.argv.slice(2)));

module.exports = {
    PACKAGES,
    binaryName,
    binaryPath,
    libcOf,
    main,
    missingPackageMessage,
    select,
};
