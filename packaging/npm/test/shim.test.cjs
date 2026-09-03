'use strict';

// The npm dispatcher's decisions, tested without installing anything: which
// binary package a machine needs, what the binary is called there, and what a
// machine with no build is told. Run with:
//
//     node --test packaging/npm/test/shim.test.cjs

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const shim = require('../bin/spektra-cli.js');

test('each platform with a published build selects its own binary package', () => {
    assert.equal(shim.select('win32', 'x64', null).package, 'spektra-cli-win32-x64');
    assert.equal(shim.select('linux', 'x64', 'glibc').package, 'spektra-cli-linux-x64');
    assert.equal(shim.select('darwin', 'x64', null).package, 'spektra-cli-darwin-x64');
    assert.equal(shim.select('darwin', 'arm64', null).package, 'spektra-cli-darwin-arm64');
});

test('Windows on ARM gets the x64 build rather than nothing', () => {
    // Windows 11 runs x64 user-mode binaries under emulation, so the x64
    // build is slower there but it does run.
    assert.equal(shim.select('win32', 'arm64', null).package, 'spektra-cli-win32-x64');
});

test('a platform with no build is an error that names the platform', () => {
    const chosen = shim.select('linux', 'arm64', 'glibc');
    assert.equal(chosen.package, undefined);
    assert.match(chosen.error, /linux-arm64/);
});

test('the no-build error lists the platforms that do have one', () => {
    const chosen = shim.select('freebsd', 'x64', null);
    assert.match(chosen.error, /Windows x64/);
    assert.match(chosen.error, /Linux x64/);
    assert.match(chosen.error, /macOS/);
});

test('musl Linux is refused with the reason, not left to fail at exec', () => {
    // The linux-x64 package would install here (npm only checks os/cpu) and
    // then die with a bare "not found", which is what this pre-empts.
    const chosen = shim.select('linux', 'x64', 'musl');
    assert.equal(chosen.package, undefined);
    assert.match(chosen.error, /musl/);
    assert.match(chosen.error, /Alpine/);
    assert.match(chosen.error, /glibc/);
});

test('the binary is spektra-cli.exe on Windows and spektra-cli elsewhere', () => {
    assert.equal(shim.binaryName('win32'), 'spektra-cli.exe');
    assert.equal(shim.binaryName('linux'), 'spektra-cli');
    assert.equal(shim.binaryName('darwin'), 'spektra-cli');
});

test('the binary sits beside the platform package manifest', () => {
    const resolve = (request) => {
        assert.equal(request, 'spektra-cli-linux-x64/package.json');
        return path.join('/opt/node_modules/spektra-cli-linux-x64', 'package.json');
    };
    assert.equal(
        shim.binaryPath('spektra-cli-linux-x64', 'linux', resolve),
        path.join('/opt/node_modules/spektra-cli-linux-x64', 'spektra-cli'));
});

test('a skipped optional dependency resolves to null rather than throwing', () => {
    const resolve = () => {
        const e = new Error("Cannot find module 'spektra-cli-linux-x64/package.json'");
        e.code = 'MODULE_NOT_FOUND';
        throw e;
    };
    assert.equal(shim.binaryPath('spektra-cli-linux-x64', 'linux', resolve), null);
});

test('a resolver failure that is not a missing module is not swallowed', () => {
    const resolve = () => {
        throw new Error('disk went away');
    };
    assert.throws(() => shim.binaryPath('spektra-cli-linux-x64', 'linux', resolve), /disk went away/);
});

test('glibc is read from the process report and musl from its absence', () => {
    assert.equal(shim.libcOf({ header: { glibcVersionRuntime: '2.39' } }), 'glibc');
    assert.equal(shim.libcOf({ header: {} }), 'musl');
    assert.equal(shim.libcOf(undefined), 'musl');
});

test('the not-installed message names the package to install', () => {
    const message = shim.missingPackageMessage('spektra-cli-linux-x64');
    assert.match(message, /spektra-cli-linux-x64/);
    assert.match(message, /npm install/);
});
