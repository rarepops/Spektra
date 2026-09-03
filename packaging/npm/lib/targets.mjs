// The one table of what gets published, shared by the assembler, the
// publisher and the post-publish smoke test.
//
// Adding a platform means: a runtime identifier in release.yml's CLI publish
// loop, an entry here, an entry in PACKAGES in bin/spektra-cli.js (the
// runtime side, which is CommonJS and cannot import this), and a matrix entry
// in .github/workflows/npm-smoke.yml. A test asserts this table and the
// shim's map still agree.

export const DISPATCHER = 'spektra-cli';

export const TARGETS = [
    {
        rid: 'win-x64',
        pkg: '@rarepops/spektra-cli-win32-x64',
        dir: 'spektra-cli-win32-x64',
        os: 'win32',
        // Windows 11 on ARM runs x64 user-mode binaries under emulation, so
        // the x64 build is offered there rather than nothing at all.
        cpu: ['x64', 'arm64'],
        exe: 'spektra-cli.exe',
        label: 'Windows x64',
    },
    {
        rid: 'linux-x64',
        pkg: '@rarepops/spektra-cli-linux-x64',
        dir: 'spektra-cli-linux-x64',
        os: 'linux',
        cpu: ['x64'],
        exe: 'spektra-cli',
        label: 'Linux x64 (glibc)',
    },
    {
        rid: 'osx-x64',
        pkg: '@rarepops/spektra-cli-darwin-x64',
        dir: 'spektra-cli-darwin-x64',
        os: 'darwin',
        cpu: ['x64'],
        exe: 'spektra-cli',
        label: 'macOS x64 (Intel)',
    },
    {
        rid: 'osx-arm64',
        pkg: '@rarepops/spektra-cli-darwin-arm64',
        dir: 'spektra-cli-darwin-arm64',
        os: 'darwin',
        cpu: ['arm64'],
        exe: 'spektra-cli',
        label: 'macOS arm64 (Apple silicon)',
    },
];

export const PLATFORM_PACKAGES = TARGETS.map((t) => t.pkg);

/// The package a machine of this shape needs, or undefined. Mirrors select()
/// in bin/spektra-cli.js, which is what actually runs on a user's machine.
export function targetFor(platform, arch) {
    return TARGETS.find((t) => t.os === platform && t.cpu.includes(arch));
}
