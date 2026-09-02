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
            { "tag_name": "v0.7.0", "html_url": "https://github.com/rarepops/Spektra/releases/tag/v0.7.0", "body": "notes here" }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0, 0));
        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Latest).IsEqualTo(new Version(0, 7, 0));
        await Assert.That(info.Url).IsEqualTo("https://github.com/rarepops/Spektra/releases/tag/v0.7.0");
        await Assert.That(info.Notes).IsEqualTo("notes here");
    }

    // The payload is trusted only as far as TLS to the API, and the release
    // URL is handed to the OS launcher. Fencing it to the one host a Spektra
    // release page can live on costs nothing; an empty URL hides the button.
    [Test]
    [Arguments("https://github.com/rarepops/Spektra/releases/tag/v9.9.9", true)]
    [Arguments("https://GITHUB.COM/rarepops/Spektra", true)]
    [Arguments("http://github.com/rarepops/Spektra", false)]
    [Arguments("https://github.com.evil.example/x", false)]
    [Arguments("https://gist.github.com/x", false)]
    [Arguments("file:///C:/Windows/System32/calc.exe", false)]
    [Arguments("javascript:alert(1)", false)]
    [Arguments("not a url", false)]
    public async Task IsTrustedReleaseUrl_AcceptsOnlyHttpsGithub(string url, bool trusted)
    {
        await Assert.That(UpdateChecker.IsTrustedReleaseUrl(url)).IsEqualTo(trusted);
    }

    [Test]
    public async Task Evaluate_UntrustedUrl_IsDropped_NotesSurvive()
    {
        var json = """
            { "tag_name": "v9.9.9", "html_url": "https://example.com/r", "body": "notes" }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0));
        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Url).IsEqualTo("");
        await Assert.That(info.Notes).IsEqualTo("notes");
    }

    // Wrong-typed fields are dropped or read as no-update, never thrown on:
    // Evaluate's callers catch JsonException, and an InvalidOperationException
    // from under JsonElement would crash the update check instead of failing it.
    [Test]
    public async Task Evaluate_WrongTypedFields_NeverThrow()
    {
        await Assert.That(UpdateChecker.Evaluate("""{ "tag_name": 7 }""", new Version(0, 6, 0)))
            .IsNull();
        var info = UpdateChecker.Evaluate(
            """{ "tag_name": "v9.9.9", "html_url": 5, "body": 5 }""", new Version(0, 6, 0));
        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Url).IsEqualTo("");
        await Assert.That(info.Notes).IsNull();
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

    [Test]
    public async Task Evaluate_ReadsAssets_NameUrlSize_InPayloadOrder()
    {
        var json = """
            { "tag_name": "v0.7.0",
              "assets": [
                { "name": "Spektra-0.7.0-Setup.msi",
                  "browser_download_url": "https://github.com/rarepops/Spektra/releases/download/v0.7.0/Spektra-0.7.0-Setup.msi",
                  "size": 123 },
                { "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://github.com/rarepops/Spektra/releases/download/v0.7.0/SHA256SUMS.txt",
                  "size": 7 } ] }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0));
        await Assert.That(info!.Assets.Count).IsEqualTo(2);
        await Assert.That(info.Assets[0]).IsEqualTo(new ReleaseAsset(
            "Spektra-0.7.0-Setup.msi",
            "https://github.com/rarepops/Spektra/releases/download/v0.7.0/Spektra-0.7.0-Setup.msi",
            123));
        await Assert.That(info.Assets[1].Name).IsEqualTo("SHA256SUMS.txt");
    }

    // Asset URLs are handed to the downloader and the result may be launched
    // as an installer, so an entry is kept only when its URL is a download
    // from this repository; junk entries are dropped, never thrown on.
    [Test]
    public async Task Evaluate_KeepsOnlyAssetsWithATrustedUrl_AndSkipsJunkEntries()
    {
        var json = """
            { "tag_name": "v0.7.0",
              "assets": [
                { "name": "evil.msi", "browser_download_url": "https://evil.example/Spektra-0.7.0-Setup.msi", "size": 1 },
                { "name": "nourl.zip", "size": 1 },
                { "name": 7, "browser_download_url": "https://github.com/rarepops/Spektra/releases/download/v0.7.0/x.zip" },
                "not an object",
                { "name": "nosize.zip", "browser_download_url": "https://github.com/rarepops/Spektra/releases/download/v0.7.0/nosize.zip" } ] }
            """;
        var info = UpdateChecker.Evaluate(json, new Version(0, 6, 0));
        await Assert.That(info!.Assets.Count).IsEqualTo(1);
        await Assert.That(info.Assets[0].Name).IsEqualTo("nosize.zip");
        await Assert.That(info.Assets[0].Size).IsEqualTo(0L);
    }

    [Test]
    public async Task Evaluate_NoAssetsField_IsAnEmptyList()
    {
        var info = UpdateChecker.Evaluate("""{ "tag_name": "v0.7.0" }""", new Version(0, 6, 0));
        await Assert.That(info!.Assets.Count).IsEqualTo(0);
        var wrongType = UpdateChecker.Evaluate("""{ "tag_name": "v0.7.0", "assets": "none" }""", new Version(0, 6, 0));
        await Assert.That(wrongType!.Assets.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("https://github.com/rarepops/Spektra/releases/download/v0.7.0/Spektra-0.7.0-Setup.msi", true)]
    [Arguments("https://GITHUB.com/rarepops/spektra/releases/download/v0.7.0/x.zip", true)]
    [Arguments("http://github.com/rarepops/Spektra/releases/download/v0.7.0/x.zip", false)]
    [Arguments("https://github.com/rarepops/Spektra/releases/tag/v0.7.0", false)]
    [Arguments("https://github.com/someone/Else/releases/download/v0.7.0/x.zip", false)]
    [Arguments("https://objects.githubusercontent.com/rarepops/Spektra/releases/download/v0.7.0/x.zip", false)]
    [Arguments("https://github.com.evil.example/rarepops/Spektra/releases/download/v1/x.zip", false)]
    [Arguments("https://github.com/rarepops/Spektra/releases/download/", false)]
    [Arguments("", false)]
    public async Task IsTrustedAssetUrl_RequiresADownloadFromThisRepository(string url, bool trusted)
    {
        await Assert.That(UpdateChecker.IsTrustedAssetUrl(url)).IsEqualTo(trusted);
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
