using System.Xml.Linq;

namespace Spektra.Tests;

/// Guards the Options page that replaced WixUI's Custom Setup tree.
///
/// The dangerous failure here is silent and destructive in one direction. Feature
/// selection is calculated by CostFinalize, before any dialog is shown, so the
/// checkboxes drive features through AddLocal and Remove control events on Next.
/// That means a checkbox arriving unticked publishes Remove: if an entry point
/// into the page forgets to seed a checkbox from the feature state, a routine
/// upgrade switches off integration the user had asked for, during a wizard they
/// clicked straight through. Nothing about it looks wrong at the time.
///
/// Measured against the built MSI's ControlEvent table rather than assumed: every
/// navigation row WixUI authors sits at Ordering 1, so an override has to outrank
/// it, and a NewDialog event ends processing of its control's remaining events, so
/// it has to be ordered last.
public class InstallerOptionsPageTests
{
    private const string OptionsDialog = "SpektraOptionsDlg";

    private static readonly XNamespace Wxs = "http://wixtoolset.org/schemas/v4/wxs";

    private static readonly string WxsPath =
        Path.Combine(AppContext.BaseDirectory, "packaging", "spektra.wxs");

    [Test]
    public async Task EveryEntryIntoTheOptionsPage_SeedsEveryCheckbox()
    {
        var doc = ReadWxs();
        var entries = doc.Descendants(Wxs + "Publish")
            .Where(p => (string?)p.Attribute("Event") == "NewDialog"
                        && (string?)p.Attribute("Value") == OptionsDialog)
            .Select(p => ((string?)p.Attribute("Dialog") ?? "", Control: (string?)p.Attribute("Control") ?? ""))
            // Back is the one entry that cannot arrive out of step. Reaching the
            // page backwards means having gone forwards through it, and Next
            // synchronises the features to the checkboxes on the way, so the
            // properties already agree with what a seed would read. Repair and
            // patch runs go straight to VerifyReadyDlg and its Back never comes
            // here. Every other entry can arrive with stale properties.
            .Where(e => e.Control != "Back")
            .Distinct()
            .ToList();

        // A guard that finds nothing is not a guard.
        await Assert.That(entries.Count).IsGreaterThan(0)
            .Because(
                $"No forward Publish row navigates to {OptionsDialog}, so the page is unreachable " +
                "or was renamed. Update this test to match the new wiring.");

        foreach (var property in CheckBoxProperties(doc))
        {
            foreach (var (dialog, control) in entries)
            {
                var seeds = doc.Descendants(Wxs + "Publish")
                    .Where(p => (string?)p.Attribute("Dialog") == dialog
                                && (string?)p.Attribute("Control") == control
                                && (string?)p.Attribute("Property") == property)
                    .ToList();

                var setsOn = seeds.Any(p => (string?)p.Attribute("Value") == "1");
                var setsOff = seeds.Any(p => (string?)p.Attribute("Value") == "{}");

                await Assert.That(setsOn && setsOff).IsTrue()
                    .Because(
                        $"{dialog}/{control} navigates to {OptionsDialog} without seeding " +
                        $"{property} both ways (found on={setsOn}, off={setsOff}). Without the on " +
                        "row, an upgrade shows the box unticked and Next publishes Remove, quietly " +
                        "tearing out integration the user chose. Without the off row, a choice of " +
                        "'no' cannot survive a trip back through this page.");
            }
        }
    }

    [Test]
    public async Task EveryCheckbox_CanTurnItsFeatureBothOnAndOff()
    {
        var doc = ReadWxs();
        var next = OptionsPageControl(doc, "Next");

        var added = FeatureEvents(next, "AddLocal");
        var removed = FeatureEvents(next, "Remove");

        await Assert.That(added.Count).IsGreaterThan(0)
            .Because($"{OptionsDialog}'s Next button publishes no AddLocal, so no checkbox can " +
                     "install anything. Feature conditions cannot do this job: CostFinalize has " +
                     "already run by the time the page is shown.");

        await Assert.That(string.Join(",", removed)).IsEqualTo(string.Join(",", added))
            .Because(
                "Every feature a checkbox can switch on it must also be able to switch off. A " +
                "feature with AddLocal and no Remove is a one-way door: unticking the box on a " +
                "Change run leaves the feature installed and the wizard reports success.");

        // Each event has to be gated on a property that a checkbox on this page
        // actually writes, or it fires on a value nothing sets.
        var properties = CheckBoxProperties(doc);
        foreach (var publish in next.Elements(Wxs + "Publish"))
        {
            var @event = (string?)publish.Attribute("Event");
            if (@event is not ("AddLocal" or "Remove")) continue;

            var condition = (string?)publish.Attribute("Condition") ?? "";
            var gated = properties.Any(p => condition.Contains(p, StringComparison.Ordinal));

            await Assert.That(gated).IsTrue()
                .Because(
                    $"The {@event} row for '{(string?)publish.Attribute("Value")}' is conditioned on " +
                    $"[{condition}], which names none of the checkbox properties on this page " +
                    $"({string.Join(", ", properties)}). It would fire on something no checkbox sets.");
        }
    }

    [Test]
    public async Task TheMoveToTheNextPage_IsOrderedLast()
    {
        var doc = ReadWxs();
        var next = OptionsPageControl(doc, "Next");

        var transition = next.Elements(Wxs + "Publish")
            .Where(p => (string?)p.Attribute("Event") == "NewDialog")
            .Select(p => Order(p))
            .DefaultIfEmpty(-1)
            .Min();

        var others = next.Elements(Wxs + "Publish")
            .Where(p => (string?)p.Attribute("Event") != "NewDialog")
            .Select(p => Order(p))
            .DefaultIfEmpty(-1)
            .Max();

        await Assert.That(transition).IsGreaterThan(others)
            .Because(
                $"On {OptionsDialog}/Next the NewDialog row is ordered {transition} but another " +
                $"event is ordered {others}. A NewDialog event ends processing of the control's " +
                "remaining events, so anything ordered after it never runs: the wizard would move " +
                "on without applying the checkboxes.");
    }

    [Test]
    public async Task NavigationOverrides_OutrankWixUIsOwnRows()
    {
        var doc = ReadWxs();
        var overrides = doc.Descendants(Wxs + "Publish")
            .Where(p => (string?)p.Attribute("Event") == "NewDialog"
                        && ((string?)p.Attribute("Value"))?.StartsWith("Spektra", StringComparison.Ordinal) == true
                        && p.Attribute("Dialog") is not null)
            .ToList();

        await Assert.That(overrides.Count).IsGreaterThan(0)
            .Because("No dialog of ours is reachable from WixUI's chain any more.");

        foreach (var publish in overrides)
        {
            await Assert.That(Order(publish)).IsGreaterThanOrEqualTo(2)
                .Because(
                    $"{(string?)publish.Attribute("Dialog")}/{(string?)publish.Attribute("Control")} " +
                    $"points at {(string?)publish.Attribute("Value")} with Order " +
                    $"{Order(publish)}. WixUI's own navigation rows all sit at Ordering 1 (read out " +
                    "of the built MSI's ControlEvent table), and the override only wins by " +
                    "outranking them. At Order 1 or below the stock page is shown instead and ours " +
                    "is dead weight in the package.");
        }
    }

    [Test]
    public async Task ExplorerIntegration_StaysOffUntilAskedFor()
    {
        var doc = ReadWxs();
        var feature = doc.Descendants(Wxs + "Feature")
            .Single(f => (string?)f.Attribute("Id") == "ShellIntegration");

        var level = int.Parse((string?)feature.Attribute("Level") ?? "0");

        await Assert.That(level).IsGreaterThan(1)
            .Because(
                $"ShellIntegration has Level {level}, which is at or below the default INSTALLLEVEL, " +
                "so it installs unless the user opts out. The documented promise is the other way " +
                "round: Explorer integration is off until someone ticks the box, and a silent " +
                "install adds nothing to the shell.");
    }

    private static IReadOnlyList<string> CheckBoxProperties(XDocument doc) =>
        OptionsPage(doc).Descendants(Wxs + "Control")
            .Where(c => (string?)c.Attribute("Type") == "CheckBox")
            .Select(c => (string?)c.Attribute("Property") ?? "")
            .Where(p => p.Length > 0)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static XElement OptionsPage(XDocument doc) =>
        doc.Descendants(Wxs + "Dialog").Single(d => (string?)d.Attribute("Id") == OptionsDialog);

    private static XElement OptionsPageControl(XDocument doc, string id) =>
        OptionsPage(doc).Descendants(Wxs + "Control").Single(c => (string?)c.Attribute("Id") == id);

    private static IReadOnlyList<string> FeatureEvents(XElement control, string @event) =>
        control.Elements(Wxs + "Publish")
            .Where(p => (string?)p.Attribute("Event") == @event)
            .Select(p => (string?)p.Attribute("Value") ?? "")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    // An absent Order is Ordering 1 in the built table, which is what WixUI's own
    // rows use, so treat it as such rather than as 0.
    private static int Order(XElement publish) =>
        int.TryParse((string?)publish.Attribute("Order"), out var order) ? order : 1;

    private static XDocument ReadWxs()
    {
        if (!File.Exists(WxsPath))
            throw new FileNotFoundException(
                $"Expected the packaged installer source at '{WxsPath}' (copied from " +
                "packaging\\spektra.wxs via the test project's None include). If this test " +
                "project stopped copying it, the guard can't run.", WxsPath);

        return XDocument.Load(WxsPath);
    }
}
