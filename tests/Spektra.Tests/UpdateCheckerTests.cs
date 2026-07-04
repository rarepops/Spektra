using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.6.0", 0, 6, 0)]
    [InlineData("0.6.0", 0, 6, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3-rc1", 1, 2, 3)]
    [InlineData("v2.0.0+build7", 2, 0, 0)]
    public void TryParseVersion_HandlesTagForms(string tag, int maj, int min, int build)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(maj, min, build), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    public void TryParseVersion_RejectsNonVersions(string? tag)
    {
        Assert.False(UpdateChecker.TryParseVersion(tag, out _));
    }

    [Fact]
    public void IsNewer_ComparesOnMajorMinorBuild_IgnoringRevision()
    {
        Assert.True(UpdateChecker.IsNewer(new Version(0, 7, 0), new Version(0, 6, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 6, 0), new Version(0, 6, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 5, 9), new Version(0, 6, 0, 0)));
    }

    [Fact]
    public void Evaluate_NewerTag_ReturnsInfo()
    {
        var json = """
            { "tag_name": "v0.7.0", "html_url": "https://example.com/r/0.7.0", "body": "notes here" }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0, 0));
        Assert.NotNull(info);
        Assert.Equal(new Version(0, 7, 0), info!.Latest);
        Assert.Equal("https://example.com/r/0.7.0", info.Url);
        Assert.Equal("notes here", info.Notes);
    }

    [Fact]
    public void Evaluate_SameOrOlderTag_ReturnsNull()
    {
        var same = UpdateChecker.Evaluate("""{ "tag_name": "v0.6.0" }""", new Version(0, 6, 0, 0));
        var older = UpdateChecker.Evaluate("""{ "tag_name": "v0.5.0" }""", new Version(0, 6, 0, 0));
        Assert.Null(same);
        Assert.Null(older);
    }

    [Fact]
    public void Evaluate_MissingOrJunk_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Evaluate("""{ "name": "no tag here" }""", new Version(0, 6, 0)));
        Assert.Null(UpdateChecker.Evaluate("not json", new Version(0, 6, 0)));
        Assert.Null(UpdateChecker.Evaluate("[]", new Version(0, 6, 0)));
    }
}
