// The build-time table and the runtime shim carry the same platform mapping
// in two places, because the shim is CommonJS and ships to users while the
// table is ESM tooling that never ships. This keeps them honest: a platform
// added or renamed on one side fails here rather than at install time on
// somebody's machine. Run with:
//
//     node --test packaging/npm/test/targets.test.mjs

import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

import { DISPATCHER, PLATFORM_PACKAGES, TARGETS, targetFor } from '../lib/targets.mjs';

const shim = createRequire(import.meta.url)('../bin/spektra-cli.js');

test('every package the shim can resolve is a package the build produces', () => {
    for (const pkg of Object.values(shim.PACKAGES))
        assert.ok(PLATFORM_PACKAGES.includes(pkg), `${pkg} is not built`);
});

test('every package the build produces is one the shim can resolve', () => {
    const resolvable = new Set(Object.values(shim.PACKAGES));
    for (const pkg of PLATFORM_PACKAGES)
        assert.ok(resolvable.has(pkg), `${pkg} is built but no machine would ever pick it`);
});

test('the two agree on which package each machine shape gets', () => {
    for (const [key, pkg] of Object.entries(shim.PACKAGES)) {
        const [platform, arch] = key.split('-');
        assert.equal(targetFor(platform, arch)?.pkg, pkg, key);
    }
});

test('the dispatcher is not one of the platform packages', () => {
    assert.equal(DISPATCHER, 'spektra-cli');
    assert.ok(!PLATFORM_PACKAGES.includes(DISPATCHER));
});

test('the reserved name is never used', () => {
    // `spektra` on npm belongs to an unrelated project.
    const all = [DISPATCHER, ...PLATFORM_PACKAGES];
    assert.ok(!all.includes('spektra'));
    assert.equal(DISPATCHER, 'spektra-cli');
    for (const name of PLATFORM_PACKAGES) assert.match(name, /^@rarepops\/spektra-cli-/);
});

test('one binary per runtime identifier, no duplicate package names or directories', () => {
    assert.equal(new Set(TARGETS.map((t) => t.rid)).size, TARGETS.length);
    assert.equal(new Set(PLATFORM_PACKAGES).size, TARGETS.length);
    assert.equal(new Set(TARGETS.map((t) => t.dir)).size, TARGETS.length);
});

test('only the Windows binary carries an exe suffix', () => {
    for (const t of TARGETS)
        assert.equal(t.exe, t.os === 'win32' ? 'spektra-cli.exe' : 'spektra-cli', t.rid);
});
