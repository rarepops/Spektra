using Spektra.Core;
using System.Net;
using System.Text.Json;

namespace Spektra.Tests;

public class UpdateCheckerTests
{
    [Test]
    [Arguments("v0.6.0", 0, 6, 0)]
    [Arguments("0.6.0", 0, 6, 0)]
    [Arguments("V1.2.3", 1, 2, 3)]
    [Arguments("v1.2.3-rc1", 1, 2, 3)]
    [Arguments("v2.0.0+build7", 2, 0, 0)]
    public async Task TryParseVersion_HandlesTagForms(string tag, int maj, int min, int build)
    {
        await Assert.That(UpdateChecker.TryParseVersion(tag, out var v)).IsTrue();
        await Assert.That(v).IsEqualTo(new Version(maj, min, build));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("nightly")]
    public async Task TryParseVersion_RejectsNonVersions(string? tag)
    {
        await Assert.That(UpdateChecker.TryParseVersion(tag, out _)).IsFalse();
    }

    [Test]
    public async Task FormatVersion_ClampsMissingBuild()
    {
        // A two-part Version reports Build == -1; it must render as ".0",
        // not "1.2.-1".
        await Assert.That(UpdateChecker.FormatVersion(new Version(1, 2))).IsEqualTo("1.2.0");
        await Assert.That(UpdateChecker.FormatVersion(new Version(0, 11, 0))).IsEqualTo("0.11.0");
        await Assert.That(UpdateChecker.FormatVersion(new Version(1, 2, 3, 4))).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task IsNewer_ComparesOnMajorMinorBuild_IgnoringRevision()
    {
        await Assert.That(UpdateChecker.IsNewer(new Version(0, 7, 0), new Version(0, 6, 0, 0))).IsTrue();
        await Assert.That(UpdateChecker.IsNewer(new Version(0, 6, 0), new Version(0, 6, 0, 0))).IsFalse();
        await Assert.That(UpdateChecker.IsNewer(new Version(0, 5, 9), new Version(0, 6, 0, 0))).IsFalse();
    }

    [Test]
    public async Task Evaluate_NewerTag_ReturnsInfo()
    {
        var json = """
            { "tag_name": "v0.7.0", "html_url": "https://example.com/r/0.7.0", "body": "notes here" }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0, 0));
        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Latest).IsEqualTo(new Version(0, 7, 0));
        await Assert.That(info.Url).IsEqualTo("https://example.com/r/0.7.0");
        await Assert.That(info.Notes).IsEqualTo("notes here");
    }

    [Test]
    public async Task Evaluate_SameOrOlderTag_ReturnsNull()
    {
        var same = UpdateChecker.Evaluate("""{ "tag_name": "v0.6.0" }""", new Version(0, 6, 0, 0));
        var older = UpdateChecker.Evaluate("""{ "tag_name": "v0.5.0" }""", new Version(0, 6, 0, 0));
        await Assert.That(same).IsNull();
        await Assert.That(older).IsNull();
    }

    [Test]
    public async Task Evaluate_MissingTagOrWrongShape_ReturnsNull()
    {
        await Assert.That(UpdateChecker.Evaluate("""{ "name": "no tag here" }""", new Version(0, 6, 0))).IsNull();
        await Assert.That(UpdateChecker.Evaluate("[]", new Version(0, 6, 0))).IsNull();
    }

    [Test]
    public async Task Evaluate_MalformedJson_Throws()
    {
        await Assert.That(() => UpdateChecker.Evaluate("not json", new Version(0, 6, 0)))
            .Throws<JsonException>();
    }

    [Test]
    public async Task CheckAsync_MalformedPayload_IsCheckFailed_NotUpToDate()
    {
        using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        }));
        var result = await UpdateChecker.CheckAsync(http, new Version(0, 6, 0));
        await Assert.That(result.Outcome).IsEqualTo(UpdateOutcome.CheckFailed);
    }

    [Test]
    public async Task CheckAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var http = new HttpClient(new StubHandler(() => throw new InvalidOperationException("unreached")));
        await Assert.That(async () => await UpdateChecker.CheckAsync(http, new Version(0, 6, 0), cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CheckAsync_Timeout_IsCheckFailed()
    {
        // HttpClient's own timeout surfaces as TaskCanceledException with an
        // untripped caller token; that must stay a soft failure.
        using var http = new HttpClient(new StubHandler(
            () => throw new TaskCanceledException("timed out")));
        var result = await UpdateChecker.CheckAsync(http, new Version(0, 6, 0));
        await Assert.That(result.Outcome).IsEqualTo(UpdateOutcome.CheckFailed);
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
