using Spektra.App;
using Spektra.Core;
using TUnit.Core.Executors;

namespace Spektra.Tests;

/// Loads a fixture through DocumentViewModel as a tab does, with no Avalonia
/// platform. None is needed: the redraws the load posts to Dispatcher.UIThread
/// queue up and never run here, which only skips repaints nobody is watching.
///
/// The executor gives each test one thread that the view model's awaits come
/// back to, as they come back to the UI thread in the app. A background job's
/// result and the rest of its continuation then land together, so once a poll
/// here sees the result, everything that job does after it has run too.
[TestExecutor<DedicatedThreadExecutor>]
public sealed class DocumentViewModelTests
{
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static readonly string Sine = Path.Combine(AppContext.BaseDirectory, "fixtures", "sine-1khz.wav");

    /// Every text the status bar showed, in order.
    private static List<string> StatusHistory(DocumentViewModel vm)
    {
        var statuses = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusText)) statuses.Add(vm.StatusText);
        };
        return statuses;
    }

    /// The overview's last word on the status bar.
    private static bool OverviewDone(List<string> statuses) =>
        statuses.Exists(s => s.StartsWith("Done in", StringComparison.Ordinal));

    /// A load of this 3 s fixture settles in well under a second; the bound
    /// only keeps a broken one from hanging the run.
    private static async Task Until(Func<bool> settled)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!settled())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the load did not settle within 30 s");
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task Load_EndsOnTheOverviewStatus_WhicheverJobFinishesLast()
    {
        var vm = new DocumentViewModel(Ff, Sine) { PrefetchChannels = false };
        var statuses = StatusHistory(vm);

        await vm.LoadOverviewAsync();
        await Until(() => vm.Integrity is not null && OverviewDone(statuses));

        // Which job finishes first changes from run to run, so the automatic
        // check keeps off the status bar at both ends; the banner carries its
        // result.
        await Assert.That(statuses).DoesNotContain("Checking integrity…");
        await Assert.That(statuses).DoesNotContain(vm.Integrity!.Summary);
        await Assert.That(vm.StatusText).StartsWith("Done in");
    }

    [Test]
    public async Task UserStartedCheck_StillAnnounces()
    {
        var vm = new DocumentViewModel(Ff, Sine) { PrefetchChannels = false, AutoIntegrityCheck = false };
        var statuses = StatusHistory(vm);
        await vm.LoadOverviewAsync();
        await Until(() => OverviewDone(statuses));
        statuses.Clear();

        await vm.RunIntegrityCheckAsync();

        var summary = vm.Integrity?.Summary;
        await Assert.That(summary).IsNotNull();
        await Assert.That(string.Join(" | ", statuses)).IsEqualTo($"Checking integrity… | {summary}");
        await Assert.That(vm.StatusText).IsEqualTo(summary);
    }

    [Test]
    public async Task UserStartedCheck_DuringDecode_StillAnnounces()
    {
        var vm = new DocumentViewModel(Ff, Sine) { PrefetchChannels = false, AutoIntegrityCheck = false };
        var statuses = StatusHistory(vm);

        // LoadOverviewAsync returns once the decode has started, and the
        // decode's continuation queues behind this test on its thread, so
        // until this test awaits again the overview is still decoding. Ctrl+I
        // comes in before that await.
        await vm.LoadOverviewAsync();
        var duringDecode = vm.StatusText;
        var check = vm.ToggleIntegrityAsync();
        await Assert.That(duringDecode).IsEqualTo("Analyzing…");

        await check;
        await Until(() => OverviewDone(statuses));

        // Ctrl+I announced itself, then gave its result, whenever the
        // overview finished around it.
        var summary = vm.Integrity?.Summary;
        await Assert.That(summary).IsNotNull();
        await Assert.That(string.Join(" | ", statuses.Where(s => s == "Checking integrity…" || s == summary)))
            .IsEqualTo($"Checking integrity… | {summary}");
    }
}
