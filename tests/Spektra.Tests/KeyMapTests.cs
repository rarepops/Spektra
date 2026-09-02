using Spektra.Core;

namespace Spektra.Tests;

public sealed class KeyMapTests
{
    private static KeyMap From(params (string Command, string Gesture)[] overrides) =>
        KeyMap.From(overrides.ToDictionary(o => o.Command, o => o.Gesture), out _);

    private static KeyMap From(
        out IReadOnlyList<string> problems, params (string Command, string Gesture)[] overrides) =>
        KeyMap.From(overrides.ToDictionary(o => o.Command, o => o.Gesture), out problems);

    [Test]
    public async Task Every_command_ships_with_a_key()
    {
        // A command with no default would be unreachable out of the box and
        // invisible in the menus, which is a shipping bug, not a preference.
        foreach (var command in Enum.GetValues<KeyCommand>())
            await Assert.That(KeyMap.Defaults.For(command))
                .IsNotNull().Because($"{command} has no default gesture");
    }

    [Test]
    public async Task Every_command_has_an_id_that_round_trips()
    {
        foreach (var command in Enum.GetValues<KeyCommand>())
        {
            var id = KeyMap.IdOf(command);
            await Assert.That(KeyMap.TryParseCommand(id, out var back)).IsTrue();
            await Assert.That(back).IsEqualTo(command);
        }
    }

    [Test]
    public async Task The_shipped_defaults_do_not_collide()
    {
        // The regression guard for the whole table: two commands on one
        // gesture means one of them silently never fires.
        var seen = new Dictionary<KeyStroke, KeyCommand>();
        foreach (var command in Enum.GetValues<KeyCommand>())
        {
            var stroke = KeyMap.Defaults.For(command)!.Value;
            await Assert.That(seen.ContainsKey(stroke))
                .IsFalse().Because($"{command} collides with {(seen.TryGetValue(stroke, out var other) ? other : default)} on {stroke}");
            seen[stroke] = command;
        }
    }

    [Test]
    public async Task No_overrides_leaves_every_default_in_place()
    {
        // The state every existing user is in: the file is absent, and nothing
        // about the keyboard may change.
        var map = KeyMap.From(null, out var problems);

        await Assert.That(problems).IsEmpty();
        foreach (var command in Enum.GetValues<KeyCommand>())
            await Assert.That(map.For(command)).IsEqualTo(KeyMap.Defaults.For(command));
    }

    [Test]
    public async Task An_override_replaces_one_binding_and_leaves_the_rest()
    {
        var map = From(("next-file", "Alt+Right"));

        await Assert.That(map.For(KeyCommand.NextFile).ToString()).IsEqualTo("Alt+Right");
        await Assert.That(map.Resolve(Stroke("Alt+Right"))).IsEqualTo(KeyCommand.NextFile);
        await Assert.That(map.For(KeyCommand.PreviousFile))
            .IsEqualTo(KeyMap.Defaults.For(KeyCommand.PreviousFile));
    }

    [Test]
    public async Task Taking_a_key_from_its_default_owner_leaves_that_owner_unbound()
    {
        // Remapping onto an occupied key is the normal case, not an error: it
        // is what "I want Ctrl+S to do this instead" means. The old owner has
        // to lose the key, or two commands fire on one press.
        var map = From(out var problems, ("next-file", "Ctrl+S"));

        await Assert.That(map.Resolve(Stroke("Ctrl+S"))).IsEqualTo(KeyCommand.NextFile);
        await Assert.That(map.For(KeyCommand.SaveImage)).IsNull();
        await Assert.That(problems.Any(p => p.Contains("save-image"))).IsTrue();
    }

    [Test]
    public async Task An_empty_gesture_unbinds_a_command()
    {
        var map = From(("save-image", ""));

        await Assert.That(map.For(KeyCommand.SaveImage)).IsNull();
        await Assert.That(map.Label(KeyCommand.SaveImage)).IsEqualTo("");
        await Assert.That(map.Resolve(Stroke("Ctrl+S"))).IsNull();
    }

    [Test]
    public async Task An_unknown_command_is_reported_and_changes_nothing()
    {
        var map = From(out var problems, ("teleport", "Ctrl+J"));

        await Assert.That(problems.Any(p => p.Contains("teleport"))).IsTrue();
        await Assert.That(map.Resolve(Stroke("Ctrl+J"))).IsNull();
    }

    [Test]
    public async Task A_malformed_gesture_is_reported_and_keeps_the_default()
    {
        // A broken entry must never cost the user the key it was trying to
        // change; one typo taking a command off the keyboard is the failure
        // mode this whole file has to avoid.
        var map = From(out var problems, ("save-image", "Ctrl+Shift+"));

        await Assert.That(problems.Any(p => p.Contains("save-image"))).IsTrue();
        await Assert.That(map.For(KeyCommand.SaveImage))
            .IsEqualTo(KeyMap.Defaults.For(KeyCommand.SaveImage));
    }

    [Test]
    public async Task Two_overrides_on_one_gesture_keep_the_first_and_report_the_second()
    {
        var map = From(out var problems,
            ("next-file", "Ctrl+J"), ("previous-file", "Ctrl+J"));

        await Assert.That(map.Resolve(Stroke("Ctrl+J"))).IsEqualTo(KeyCommand.NextFile);
        await Assert.That(map.For(KeyCommand.PreviousFile))
            .IsEqualTo(KeyMap.Defaults.For(KeyCommand.PreviousFile));
        await Assert.That(problems.Any(p => p.Contains("previous-file"))).IsTrue();
    }

    [Test]
    public async Task Swapping_two_commands_keys_works()
    {
        // Both sides move at once, so neither is "taking" an occupied key by
        // the time the map settles. A rule that rejected either would make the
        // most obvious customization impossible.
        var map = From(out var problems,
            ("next-file", "Ctrl+Down"), ("next-channel", "Ctrl+Right"));

        await Assert.That(problems).IsEmpty();
        await Assert.That(map.Resolve(Stroke("Ctrl+Down"))).IsEqualTo(KeyCommand.NextFile);
        await Assert.That(map.Resolve(Stroke("Ctrl+Right"))).IsEqualTo(KeyCommand.NextChannel);
    }

    [Test]
    public async Task Resolve_answers_nothing_for_an_unbound_gesture()
    {
        await Assert.That(KeyMap.Defaults.Resolve(Stroke("Ctrl+Alt+Q"))).IsNull();
    }

    [Test]
    public async Task Label_is_what_the_menus_and_the_shortcut_sheet_show()
    {
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.ExportReport)).IsEqualTo("Ctrl+Shift+S");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.ResetView)).IsEqualTo("Ctrl+0");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.CompareBoth)).IsEqualTo("Esc");
    }

    [Test]
    public async Task The_defaults_are_exactly_what_shipped_before_the_map()
    {
        // Spot-check of the bindings a user would notice immediately. The
        // whole point of the first version is that nothing moves.
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.OpenFiles)).IsEqualTo("Ctrl+O");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.OpenFolder)).IsEqualTo("Ctrl+Shift+O");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.CloseTab)).IsEqualTo("Ctrl+W");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.NextTab)).IsEqualTo("Ctrl+Tab");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.PreviousTab)).IsEqualTo("Ctrl+Shift+Tab");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.CheckIntegrity)).IsEqualTo("Ctrl+I");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.MeasureLoudness)).IsEqualTo("Ctrl+L");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.Reload)).IsEqualTo("F5");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.ReloadFresh)).IsEqualTo("Shift+F5");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.RefreshFolder)).IsEqualTo("Ctrl+F5");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.NextFile)).IsEqualTo("Ctrl+Right");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.PreviousChannel)).IsEqualTo("Ctrl+Up");
        await Assert.That(KeyMap.Defaults.Label(KeyCommand.CompareFlip)).IsEqualTo("T");
    }

    private static KeyStroke Stroke(string text)
    {
        KeyStroke.TryParse(text, out var stroke);
        return stroke;
    }
}
