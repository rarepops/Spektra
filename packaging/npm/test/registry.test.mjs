// The publisher's decisions, tested away from the network: how a tarball's
// integrity string is formed, what counts as a releasable version, whether the
// npm in use can publish through OIDC, and whether a version already on the
// registry may be skipped or must stop the release. Run with:
//
//     node --test packaging/npm/test/registry.test.mjs

import test from 'node:test';
import assert from 'node:assert/strict';

import {
    integrityOf,
    isStrictVersion,
    packFilenameOf,
    publishVerdict,
    readBackVerdict,
    sha1Of,
    supportsTrustedPublishing,
} from '../lib/registry.mjs';

// NIST vectors, so the hash and the base64 encoding are both pinned to
// something published rather than to another call of the same library.
const EMPTY_SHA512 = 'cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce'
    + '47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e';
const ABC_SHA512 = 'ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a'
    + '2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f';

const b64 = (hex) => Buffer.from(hex, 'hex').toString('base64');

test('integrity is the registry form: sha512- and base64, not hex', () => {
    assert.equal(integrityOf(Buffer.alloc(0)), `sha512-${b64(EMPTY_SHA512)}`);
    assert.equal(integrityOf(Buffer.from('abc')), `sha512-${b64(ABC_SHA512)}`);
});

test('sha1 is lowercase hex, the shape a registry shasum takes', () => {
    // SHA-1("abc"), the published vector.
    assert.equal(sha1Of(Buffer.from('abc')), 'a9993e364706816aba3e25717850c26c9cd0d89d');
});

test('only a bare X.Y.Z is releasable', () => {
    for (const good of ['0.24.1', '1.0.0', '10.20.30']) assert.equal(isStrictVersion(good), true, good);
    for (const bad of ['0.0.0-dev', '1.2', '1.2.3.4', 'v1.2.3', '1.2.3-rc.1', '', '1.2.3 ', 'x.y.z'])
        assert.equal(isStrictVersion(bad), false, bad);
});

test('a leading zero is not a version component', () => {
    // Guards against 01.2.3 sliding through a lazy \d+ and publishing under a
    // name npm would normalise to something else.
    assert.equal(isStrictVersion('01.2.3'), false);
    assert.equal(isStrictVersion('1.02.3'), false);
});

test('trusted publishing needs npm 11.5.1 or newer', () => {
    assert.equal(supportsTrustedPublishing('11.5.1'), true);
    assert.equal(supportsTrustedPublishing('11.6.0'), true);
    assert.equal(supportsTrustedPublishing('12.0.0'), true);
    assert.equal(supportsTrustedPublishing('11.5.0'), false);
    assert.equal(supportsTrustedPublishing('10.9.2'), false);
});

test('version comparison is numeric, not lexical', () => {
    // '11.10.0' sorts before '11.5.1' as text, which is the classic way this
    // check passes for old npm and fails for new.
    assert.equal(supportsTrustedPublishing('11.10.0'), true);
    assert.equal(supportsTrustedPublishing('11.4.99'), false);
});

test('a prerelease npm build is judged on its release numbers', () => {
    assert.equal(supportsTrustedPublishing('11.6.0-pre.0'), true);
});

test('an unpublished version is published', () => {
    const verdict = publishVerdict({ name: 'spektra-cli', version: '0.24.1', local: { integrity: 'sha512-A' }, dist: null });
    assert.equal(verdict.action, 'publish');
});

test('a published version with the same integrity is skipped', () => {
    const verdict = publishVerdict({
        name: 'spektra-cli', version: '0.24.1',
        local: { integrity: 'sha512-A', sha1: 'aa' },
        dist: { integrity: 'sha512-A' },
    });
    assert.equal(verdict.action, 'skip');
});

test('a published version with different contents stops the release', () => {
    const verdict = publishVerdict({
        name: 'spektra-cli', version: '0.24.1',
        local: { integrity: 'sha512-LOCAL', sha1: 'aa' },
        dist: { integrity: 'sha512-REGISTRY' },
    });
    assert.equal(verdict.action, 'fail');
    assert.match(verdict.reason, /sha512-REGISTRY/);
    assert.match(verdict.reason, /sha512-LOCAL/);
    assert.match(verdict.reason, /0\.24\.1/);
});

test('a registry entry with no integrity falls back to the shasum', () => {
    const same = publishVerdict({
        name: 'x', version: '1.0.0',
        local: { integrity: 'sha512-A', sha1: 'abc123' },
        dist: { shasum: 'abc123' },
    });
    assert.equal(same.action, 'skip');

    const different = publishVerdict({
        name: 'x', version: '1.0.0',
        local: { integrity: 'sha512-A', sha1: 'abc123' },
        dist: { shasum: 'def456' },
    });
    assert.equal(different.action, 'fail');
});

test('a registry entry with nothing to compare is never assumed identical', () => {
    const verdict = publishVerdict({
        name: 'x', version: '1.0.0',
        local: { integrity: 'sha512-A', sha1: 'abc123' },
        dist: {},
    });
    assert.equal(verdict.action, 'fail');
    assert.match(verdict.reason, /cannot be compared/i);
});

// `npm pack --json` changed shape between majors: npm 11 and earlier answer
// with an array of entries, npm 12 with an object keyed by package name. The
// publisher packs one directory at a time, so either shape holds exactly one
// entry, and reading the wrong one aborts a release after the GitHub release
// has already been created.
const PACK_ENTRY = {
    id: '@rarepops/spektra-cli-win32-x64@0.24.2',
    name: '@rarepops/spektra-cli-win32-x64',
    version: '0.24.2',
    size: 34117174,
    filename: 'rarepops-spektra-cli-win32-x64-0.24.2.tgz',
};

test('an npm 11 pack result names the tarball', () => {
    assert.equal(packFilenameOf(JSON.stringify([PACK_ENTRY])), PACK_ENTRY.filename);
});

test('an npm 12 pack result names the same tarball', () => {
    const keyed = { [PACK_ENTRY.name]: PACK_ENTRY };
    assert.equal(packFilenameOf(JSON.stringify(keyed)), PACK_ENTRY.filename);
});

test('a pack result holding no entry is refused in either shape', () => {
    assert.throws(() => packFilenameOf('[]'), /no packed file/);
    assert.throws(() => packFilenameOf('{}'), /no packed file/);
    assert.throws(() => packFilenameOf('null'), /no packed file/);
});

test('a pack result naming more than one tarball is refused', () => {
    const two = [PACK_ENTRY, { ...PACK_ENTRY, filename: 'other-0.24.2.tgz' }];
    assert.throws(() => packFilenameOf(JSON.stringify(two)), /one package at a time/);
});

test('a pack entry carrying no file name is refused', () => {
    const nameless = { ...PACK_ENTRY, filename: undefined };
    assert.throws(() => packFilenameOf(JSON.stringify([nameless])), /no file name/);
});

test('output that is not JSON is refused', () => {
    assert.throws(() => packFilenameOf('npm warn config production\n'), /not JSON/);
});

// After publishing, the registry is asked for what was just sent. npm answers
// a publish with success before the package is readable, and has answered with
// success and then never served the package at all, which is how 0.24.1 and
// 0.24.3 shipped a dispatcher naming a Linux package nobody could install.
test('a read-back that finds our own bytes is present', () => {
    const verdict = readBackVerdict({
        local: { integrity: 'sha512-A', sha1: 'abc123' },
        dist: { integrity: 'sha512-A' },
    });
    assert.equal(verdict, 'present');
});

test('a read-back that finds nothing is absent, not a failure', () => {
    // Propagation is measured in minutes, so this is the caller's cue to wait
    // rather than to give up.
    assert.equal(readBackVerdict({ local: { integrity: 'sha512-A' }, dist: null }), 'absent');
});

test('a read-back that finds other bytes is different', () => {
    const verdict = readBackVerdict({
        local: { integrity: 'sha512-A', sha1: 'abc123' },
        dist: { integrity: 'sha512-B' },
    });
    assert.equal(verdict, 'different');
});

test('a read-back falls back to the shasum when there is no integrity', () => {
    const local = { integrity: 'sha512-A', sha1: 'abc123' };
    assert.equal(readBackVerdict({ local, dist: { shasum: 'abc123' } }), 'present');
    assert.equal(readBackVerdict({ local, dist: { shasum: 'def456' } }), 'different');
});

test('a read-back with nothing to compare is never assumed present', () => {
    const verdict = readBackVerdict({ local: { integrity: 'sha512-A' }, dist: {} });
    assert.equal(verdict, 'different');
});
