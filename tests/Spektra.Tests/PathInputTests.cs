using Spektra.Core;

namespace Spektra.Tests;

public sealed class PathInputTests
{
    [Test]
    public async Task Normalize_NullOrBlank_IsEmpty()
    {
        await Assert.That(PathInput.Normalize(null)).IsEqualTo("");
        await Assert.That(PathInput.Normalize("   ")).IsEqualTo("");
    }

    [Test]
    public async Task Normalize_TrimsSurroundingWhitespace()
    {
        await Assert.That(PathInput.Normalize("  C:\\Music  ")).IsEqualTo(@"C:\Music");
    }

    [Test]
    public async Task Normalize_StripsCopyAsPathQuotes_KeepingInnerSpaces()
    {
        // Windows "Copy as path" wraps the path in double quotes; a folder name
        // can legitimately contain spaces, so only the wrapping is removed.
        await Assert.That(PathInput.Normalize("\"C:\\My Music\"")).IsEqualTo(@"C:\My Music");
    }

    [Test]
    public async Task Normalize_StripsPaddingOutsideAndInsideQuotes()
    {
        // The two trims bracket the quote strip, so a quoted path with padding
        // on both sides of the quotes still comes back clean.
        await Assert.That(PathInput.Normalize("  \"  C:\\x  \"  ")).IsEqualTo(@"C:\x");
    }
}
