using Spektra.Core;

namespace Spektra.Tests;

public sealed class PathScopeTests
{
    [Test]
    [Arguments(@"C:\music\a.flac", @"C:\music", true)]
    [Arguments(@"C:\music\Album\CD1\a.flac", @"C:\music", true)]
    [Arguments(@"C:\music", @"C:\music", false)]
    [Arguments(@"C:\music\Album2\a.flac", @"C:\music\Album", false)]
    [Arguments(@"C:\MUSIC\a.flac", @"C:\music", true)]
    [Arguments(@"C:\music\a.flac", @"C:\music\", true)]
    public async Task IsUnder_reports_strict_containment(
        string path, string folder, bool expected)
    {
        await Assert.That(PathScope.IsUnder(path, folder)).IsEqualTo(expected);
    }
}
