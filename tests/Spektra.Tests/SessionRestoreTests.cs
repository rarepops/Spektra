using Spektra.Core;

namespace Spektra.Tests;

public sealed class SessionRestoreTests
{
    // Every path "exists" except those named here.
    private static Func<string, bool> AllBut(params string[] missing) =>
        p => !missing.Contains(p, StringComparer.OrdinalIgnoreCase);

    [Test]
    public async Task Surviving_paths_are_kept_in_order()
    {
        var r = SessionRestore.Resolve(["a", "b", "c"], selectedIndex: 0, AllBut());

        await Assert.That(r.Paths.SequenceEqual(["a", "b", "c"])).IsTrue();
        await Assert.That(r.Selected).IsEqualTo("a");
    }

    [Test]
    public async Task Missing_paths_are_dropped()
    {
        var r = SessionRestore.Resolve(["a", "b", "c"], selectedIndex: 0, AllBut("b"));

        await Assert.That(r.Paths.SequenceEqual(["a", "c"])).IsTrue();
    }

    [Test]
    public async Task Selection_follows_its_path_when_an_earlier_tab_vanishes()
    {
        // "c" was selected at index 2; dropping "a" shifts it to index 1.
        // Resolving by position would wrongly select "b".
        var r = SessionRestore.Resolve(["a", "b", "c"], selectedIndex: 2, AllBut("a"));

        await Assert.That(r.Paths.SequenceEqual(["b", "c"])).IsTrue();
        await Assert.That(r.Selected).IsEqualTo("c");
    }

    [Test]
    public async Task A_vanished_selection_falls_back_to_the_first_survivor()
    {
        var r = SessionRestore.Resolve(["a", "b", "c"], selectedIndex: 1, AllBut("b"));

        await Assert.That(r.Selected).IsEqualTo("a");
    }

    [Test]
    public async Task An_all_missing_session_restores_nothing()
    {
        var r = SessionRestore.Resolve(["a", "b"], selectedIndex: 0, AllBut("a", "b"));

        await Assert.That(r.Paths).IsEmpty();
        await Assert.That(r.Selected).IsNull();
    }

    [Test]
    public async Task A_null_or_empty_session_restores_nothing()
    {
        var fromNull = SessionRestore.Resolve(null, selectedIndex: 0, AllBut());
        var fromEmpty = SessionRestore.Resolve([], selectedIndex: 0, AllBut());

        await Assert.That(fromNull.Paths).IsEmpty();
        await Assert.That(fromNull.Selected).IsNull();
        await Assert.That(fromEmpty.Paths).IsEmpty();
        await Assert.That(fromEmpty.Selected).IsNull();
    }

    [Test]
    public async Task An_out_of_range_index_does_not_throw()
    {
        var high = SessionRestore.Resolve(["a", "b"], selectedIndex: 9, AllBut());
        var negative = SessionRestore.Resolve(["a", "b"], selectedIndex: -1, AllBut());

        await Assert.That(high.Selected).IsEqualTo("a");
        await Assert.That(negative.Selected).IsEqualTo("a");
    }
}
