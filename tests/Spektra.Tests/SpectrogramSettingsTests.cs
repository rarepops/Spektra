using Spektra.Core;

namespace Spektra.Tests;

public sealed class SpectrogramSettingsTests
{
    [Test]
    public async Task EstimateColumns_ALongFile_IsCappedAtMaxColumns()
    {
        // 9605925 samples, 9379 windows at the default 1024 hop.
        var columns = new SpectrogramSettings().EstimateColumns(TimeSpan.FromSeconds(200.123456), 48000);
        await Assert.That(columns).IsEqualTo(4096);
    }

    [Test]
    public async Task EstimateColumns_AShortFile_CountsItsWindows()
    {
        // 148621 samples: (148621 - 2048) / 1024 + 1.
        var columns = new SpectrogramSettings().EstimateColumns(TimeSpan.FromSeconds(3.3701), 44100);
        await Assert.That(columns).IsEqualTo(144);
    }

    [Test]
    public async Task EstimateColumns_LessThanOneWindow_IsStillOneColumn()
    {
        var columns = new SpectrogramSettings().EstimateColumns(TimeSpan.FromSeconds(0.01), 44100);
        await Assert.That(columns).IsEqualTo(1);
    }

    [Test]
    public async Task EstimateColumns_FollowsAnOverriddenHop()
    {
        // (148621 - 2048) / 64 + 1 is 2291 windows, past a 937-column cap.
        var settings = new SpectrogramSettings(MaxColumns: 937, HopOverride: 64);
        await Assert.That(settings.EstimateColumns(TimeSpan.FromSeconds(3.3701), 44100)).IsEqualTo(937);
    }
}
