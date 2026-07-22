using Spektra.Core;

namespace Spektra.Tests;

// Both filter boxes (Duplicate Detective results, Folder Manifest kinds) split
// their input into words on the same separators. This pins that shared
// contract so the two parsers cannot drift on what counts as a word boundary.
public sealed class FilterTokenizerTests
{
    [Test]
    public async Task BothParsers_SplitOnSpaceCommaSemicolonAndTab()
    {
        const string mixed = "flac,mp3;wav\taac ogg";
        await Assert.That(DuplicateScan.ParseFilterTokens(mixed)
            .SequenceEqual(["flac", "mp3", "wav", "aac", "ogg"])).IsTrue();
        await Assert.That(FolderManifest.ParseKinds(mixed)
            .SequenceEqual(["flac", "mp3", "wav", "aac", "ogg"])).IsTrue();
    }
}
