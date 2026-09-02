using Spektra.Core;
using System.Net;
using System.Security.Cryptography;

namespace Spektra.Tests;

public class ReleaseDownloaderTests
{
    private const string Url = "https://github.com/rarepops/Spektra/releases/download/v0.24.0/Spektra-0.24.0-Setup.msi";

    // Awkward size on purpose: not a multiple of any buffer the loop might use.
    private static readonly byte[] Payload = Enumerable.Range(0, 200_003).Select(i => (byte)(i * 7)).ToArray();

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static ReleaseAsset Asset(long size) => new("Spektra-0.24.0-Setup.msi", Url, size);

    private static HttpClient Serving(HttpContent content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(() => new HttpResponseMessage(status) { Content = content }));

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spektra-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public async Task Download_WritesTheBytes_AndReportsVerified()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var http = Serving(new ByteArrayContent(Payload));
            var outcome = await ReleaseDownloader.DownloadVerifiedAsync(
                http, Asset(Payload.Length), dest, Hex(Payload), progress: null, CancellationToken.None);
            await Assert.That(outcome).IsEqualTo(DownloadOutcome.Verified);
            await Assert.That(Hex(File.ReadAllBytes(dest))).IsEqualTo(Hex(Payload));
            await Assert.That(Directory.GetFiles(dir).Length).IsEqualTo(1); // no .partial left behind
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_ReportsProgressThatEndsAtTheTotal()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var http = Serving(new ByteArrayContent(Payload));
            var seen = new Recorder();
            await ReleaseDownloader.DownloadVerifiedAsync(
                http, Asset(Payload.Length), dest, Hex(Payload), seen, CancellationToken.None);
            await Assert.That(seen.Items.Count).IsGreaterThan(0);
            await Assert.That(seen.Items[^1].Done).IsEqualTo((long)Payload.Length);
            await Assert.That(seen.Items[^1].Total).IsEqualTo((long)Payload.Length);
            for (var i = 1; i < seen.Items.Count; i++)
                await Assert.That(seen.Items[i].Done).IsGreaterThanOrEqualTo(seen.Items[i - 1].Done);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_ChecksumMismatch_DeletesTheFile()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var http = Serving(new ByteArrayContent(Payload));
            var outcome = await ReleaseDownloader.DownloadVerifiedAsync(
                http, Asset(Payload.Length), dest, Hex([1, 2, 3]), progress: null, CancellationToken.None);
            await Assert.That(outcome).IsEqualTo(DownloadOutcome.ChecksumMismatch);
            await Assert.That(Directory.GetFiles(dir).Length).IsEqualTo(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_ServerError_Throws_AndLeavesNothing()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var http = Serving(new StringContent("gone"), HttpStatusCode.NotFound);
            await Assert.That(async () => await ReleaseDownloader.DownloadVerifiedAsync(
                    http, Asset(Payload.Length), dest, Hex(Payload), progress: null, CancellationToken.None))
                .Throws<HttpRequestException>();
            await Assert.That(Directory.GetFiles(dir).Length).IsEqualTo(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_BodyFailsMidway_Throws_AndLeavesNoPartial()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var http = Serving(new StreamContent(new FailingStream(Payload, failAfter: 50_000)));
            await Assert.That(async () => await ReleaseDownloader.DownloadVerifiedAsync(
                    http, Asset(Payload.Length), dest, Hex(Payload), progress: null, CancellationToken.None))
                .Throws<IOException>();
            await Assert.That(Directory.GetFiles(dir).Length).IsEqualTo(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_Cancelled_Throws_AndLeavesNothing()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            using var cts = new CancellationTokenSource();
            using var http = Serving(new StreamContent(new CancellingStream(Payload, cts, cancelAfter: 50_000)));
            await Assert.That(async () => await ReleaseDownloader.DownloadVerifiedAsync(
                    http, Asset(Payload.Length), dest, Hex(Payload), progress: null, cts.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(Directory.GetFiles(dir).Length).IsEqualTo(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Download_ReplacesAStaleFileOfTheSameName()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            File.WriteAllBytes(dest, [9, 9, 9]);
            using var http = Serving(new ByteArrayContent(Payload));
            var outcome = await ReleaseDownloader.DownloadVerifiedAsync(
                http, Asset(Payload.Length), dest, Hex(Payload), progress: null, CancellationToken.None);
            await Assert.That(outcome).IsEqualTo(DownloadOutcome.Verified);
            await Assert.That(Hex(File.ReadAllBytes(dest))).IsEqualTo(Hex(Payload));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task IsVerified_TrueOnlyForAFileWithThatHash()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "Spektra-0.24.0-Setup.msi");
            File.WriteAllBytes(path, Payload);
            await Assert.That(await ReleaseDownloader.IsVerifiedAsync(path, Hex(Payload), CancellationToken.None)).IsTrue();
            await Assert.That(await ReleaseDownloader.IsVerifiedAsync(path, Hex([1, 2, 3]), CancellationToken.None)).IsFalse();
            await Assert.That(await ReleaseDownloader.IsVerifiedAsync(Path.Combine(dir, "missing"), Hex(Payload), CancellationToken.None)).IsFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task FetchText_ReturnsTheBody_AndThrowsOnErrors()
    {
        using var ok = Serving(new StringContent("abc  x.zip\n"));
        await Assert.That(await ReleaseDownloader.FetchTextAsync(ok, Url, CancellationToken.None)).IsEqualTo("abc  x.zip\n");
        using var bad = Serving(new StringContent("nope"), HttpStatusCode.Forbidden);
        await Assert.That(async () => await ReleaseDownloader.FetchTextAsync(bad, Url, CancellationToken.None))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task DownloadsFolder_IsAnAbsolutePath()
    {
        await Assert.That(Path.IsPathRooted(ReleaseDownloader.DownloadsFolder())).IsTrue();
    }

    private sealed class Recorder : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Items { get; } = [];
        public void Report(DownloadProgress value) => Items.Add(value);
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

    /// Serves `data` and then breaks the connection with an IOException once
    /// `failAfter` bytes have been read.
    private class FailingStream(byte[] data, int failAfter) : Stream
    {
        private int _pos;

        protected virtual void OnBoundary() => throw new IOException("connection reset");

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= failAfter) OnBoundary();
            var n = Math.Min(count, Math.Min(failAfter - _pos, data.Length - _pos));
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// Like FailingStream, but trips the caller's token at the boundary, the
    /// way a user pressing Cancel mid-download does.
    private sealed class CancellingStream(byte[] data, CancellationTokenSource cts, int cancelAfter)
        : FailingStream(data, cancelAfter)
    {
        protected override void OnBoundary()
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }
    }
}
