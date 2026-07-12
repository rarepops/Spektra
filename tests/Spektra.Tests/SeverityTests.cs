using Spektra.Core;

namespace Spektra.Tests;

public sealed class SeverityTests
{
    private static AuditEntry Entry(bool hasProblem, string integrity, string bandwidth)
    {
        var row = new AuditRow(
            File: "x.flac", Codec: "flac", SampleRateHz: 44100, Channels: 2,
            BitrateBps: 1_000_000, DurationSeconds: 1.0, Bandwidth: bandwidth,
            CutoffHz: 22_000.0, Integrity: integrity, DecodeErrors: 0,
            Dropouts: 0, Truncated: false, Error: null);
        var target = new AuditTarget("x.flac", 1, 1);
        return new AuditEntry(target, row, hasProblem, FromCache: false);
    }

    [Test]
    public async Task Problem_when_the_entry_has_a_problem()
    {
        await Assert.That(FolderAudit.Severity(Entry(true, "Ok", "Lossless")))
            .IsEqualTo(RowSeverity.Problem);
    }

    [Test]
    public async Task Suspect_when_integrity_suspect_or_bandwidth_suspicious()
    {
        await Assert.That(FolderAudit.Severity(Entry(false, "Suspect", "Lossless")))
            .IsEqualTo(RowSeverity.Suspect);
        await Assert.That(FolderAudit.Severity(Entry(false, "Ok", "Suspicious")))
            .IsEqualTo(RowSeverity.Suspect);
    }

    [Test]
    public async Task Clean_otherwise()
    {
        await Assert.That(FolderAudit.Severity(Entry(false, "Ok", "Lossless")))
            .IsEqualTo(RowSeverity.Clean);
    }
}
