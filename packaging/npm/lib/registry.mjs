// Pure decisions the publisher makes, kept away from the network and the
// filesystem so they can be tested: how a tarball's integrity string is
// formed, what counts as a releasable version, whether the npm in use can
// publish through OIDC, and whether a version already on the registry may be
// skipped or has to stop the release.
//
// Nothing here is published. build.mjs copies only bin/ into the dispatcher.

import { createHash } from 'node:crypto';

/// The registry's own integrity form for a tarball: `sha512-` and base64, the
/// same string that comes back as `dist.integrity`, so the two compare
/// directly.
export function integrityOf(bytes) {
    return `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
}

/// Lowercase hex sha1, the shape of a registry `dist.shasum`. Only used to
/// compare against an entry that carries no integrity.
export function sha1Of(bytes) {
    return createHash('sha1').update(bytes).digest('hex');
}

// A release is X.Y.Z and nothing else: no prerelease, no build metadata, no
// leading zero (npm would normalise 01.2.3 to something else and the tag
// would stop matching the manifest).
const STRICT_VERSION = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;

export function isStrictVersion(version) {
    return typeof version === 'string' && STRICT_VERSION.test(version);
}

// npm learned to authenticate through GitHub's OIDC token, and to attach
// provenance by itself, in 11.5.1. Below that a trusted-publishing job would
// fall through to looking for a token that is deliberately not there.
const MIN_NPM_FOR_OIDC = [11, 5, 1];

export function supportsTrustedPublishing(version) {
    // Compare the release numbers as numbers. As text '11.10.0' sorts before
    // '11.5.1', which is the classic way this check passes for old npm and
    // fails for new.
    const parts = String(version).trim().split('-')[0].split('.').map(Number);
    if (parts.length < 3 || parts.some((n) => !Number.isInteger(n))) return false;
    for (let i = 0; i < 3; i++) {
        if (parts[i] > MIN_NPM_FOR_OIDC[i]) return true;
        if (parts[i] < MIN_NPM_FOR_OIDC[i]) return false;
    }
    return true;
}

export const MIN_NPM_VERSION = MIN_NPM_FOR_OIDC.join('.');

/// Whether this exact tarball should be published, skipped, or treated as a
/// release-stopping conflict. `dist` is the registry's `versions[v].dist`, or
/// null when the registry has no such version.
///
/// A version already published is only ever skipped when the bytes match. The
/// point is that a re-run of a half-finished release finishes it, while a
/// second, different build of a version that is already public stops instead
/// of quietly leaving the registry and the release page describing different
/// code.
export function publishVerdict({ name, version, local, dist }) {
    if (!dist) return { action: 'publish' };

    const conflict = (kind, theirs, ours) => ({
        action: 'fail',
        reason: `${name}@${version} is already on the registry with different contents. `
            + `The registry's ${kind} is ${theirs}, this build packed ${ours}. Nothing was published. `
            + 'A published version is never overwritten: release the fix as the next version. '
            + 'Note that npm does not guarantee a byte-identical tarball across npm versions, so this '
            + 'can also mean "same files, different packer"; compare the file lists before deciding.',
    });

    if (dist.integrity)
        return dist.integrity === local.integrity
            ? { action: 'skip' }
            : conflict('integrity', dist.integrity, local.integrity);

    // Entries published before integrity existed carry only a sha1 shasum.
    if (dist.shasum)
        return dist.shasum === local.sha1
            ? { action: 'skip' }
            : conflict('shasum', dist.shasum, local.sha1);

    return {
        action: 'fail',
        reason: `${name}@${version} is already on the registry but cannot be compared: the entry `
            + 'reports neither an integrity nor a shasum, so there is no way to tell whether it holds '
            + 'these bytes. Nothing was published.',
    };
}
