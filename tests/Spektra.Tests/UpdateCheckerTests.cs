using Spektra.Core;
using System.Net;
using System.Text.Json;
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
    public void FormatVersion_ClampsMissingBuild()
    {
        // A two-part Version reports Build == -1; it must render as ".0",
        // not "1.2.-1".
        Assert.Equal("1.2.0", UpdateChecker.FormatVersion(new Version(1, 2)));
        Assert.Equal("0.11.0", UpdateChecker.FormatVersion(new Version(0, 11, 0)));
        Assert.Equal("1.2.3", UpdateChecker.FormatVersion(new Version(1, 2, 3, 4)));
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
    public void Evaluate_MissingTagOrWrongShape_ReturnsNull()
    {
        Assert.Null(UpdateChecker.Evaluate("""{ "name": "no tag here" }""", new Version(0, 6, 0)));
        Assert.Null(UpdateChecker.Evaluate("[]", new Version(0, 6, 0)));
    }

    [Fact]
    public void Evaluate_MalformedJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => UpdateChecker.Evaluate("not json", new Version(0, 6, 0)));
    }

    [Fact]
    public async Task CheckAsync_MalformedPayload_IsCheckFailed_NotUpToDate()
    {
        using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        }));
        var result = await UpdateChecker.CheckAsync(http, new Version(0, 6, 0));
        Assert.Equal(UpdateOutcome.CheckFailed, result.Outcome);
    }

    [Fact]
    public async Task CheckAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var http = new HttpClient(new StubHandler(() => throw new InvalidOperationException("unreached")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UpdateChecker.CheckAsync(http, new Version(0, 6, 0), cts.Token));
    }

    [Fact]
    public async Task CheckAsync_Timeout_IsCheckFailed()
    {
        // HttpClient's own timeout surfaces as TaskCanceledException with an
        // untripped caller token; that must stay a soft failure.
        using var http = new HttpClient(new StubHandler(
            () => throw new TaskCanceledException("timed out")));
        var result = await UpdateChecker.CheckAsync(http, new Version(0, 6, 0));
        Assert.Equal(UpdateOutcome.CheckFailed, result.Outcome);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond());
        }
    }
}
