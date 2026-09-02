using Spektra.Core;

namespace Spektra.Tests;

public sealed class KeyMapStoreTests
{
    private static string WriteFile(string contents)
    {
        var dir = Directory.CreateTempSubdirectory("spektra-keys").FullName;
        var path = Path.Combine(dir, "keybindings.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Test]
    public async Task An_absent_file_is_the_defaults_and_says_nothing()
    {
        // Every existing user is in this state. It is not a problem to report.
        var dir = Directory.CreateTempSubdirectory("spektra-keys-none").FullName;

        var map = KeyMapStore.Load(Path.Combine(dir, "keybindings.json"), out var problems);

        await Assert.That(problems).IsEmpty();
        await Assert.That(map.Label(KeyCommand.SaveImage)).IsEqualTo("Ctrl+S");
    }

    [Test]
    public async Task A_flat_object_of_command_to_gesture_applies()
    {
        var path = WriteFile("""
            {
              "next-file": "Alt+Right",
              "previous-file": "Alt+Left"
            }
            """);

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems).IsEmpty();
        await Assert.That(map.Label(KeyCommand.NextFile)).IsEqualTo("Alt+Right");
        await Assert.That(map.Label(KeyCommand.PreviousFile)).IsEqualTo("Alt+Left");
    }

    [Test]
    public async Task Comments_and_trailing_commas_are_allowed()
    {
        // This file is meant to be hand-edited, and a JSON parser that rejects
        // a comment makes documenting your own bindings impossible.
        var path = WriteFile("""
            {
              // browser-style navigation
              "next-file": "Alt+Right",
            }
            """);

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems).IsEmpty();
        await Assert.That(map.Label(KeyCommand.NextFile)).IsEqualTo("Alt+Right");
    }

    [Test]
    public async Task Malformed_json_is_reported_and_every_default_survives()
    {
        var path = WriteFile("{ this is not json");

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems).IsNotEmpty();
        foreach (var command in Enum.GetValues<KeyCommand>())
            await Assert.That(map.For(command)).IsEqualTo(KeyMap.Defaults.For(command));
    }

    [Test]
    public async Task A_value_that_is_not_a_string_is_reported_and_skipped()
    {
        // JsonElement throws InvalidOperationException on a wrong-typed read,
        // which is outside the usual catch list; reading the kind first is
        // what keeps a number here from taking down the whole file.
        var path = WriteFile("""
            {
              "next-file": 5,
              "previous-file": "Alt+Left"
            }
            """);

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems.Any(p => p.Contains("next-file"))).IsTrue();
        await Assert.That(map.Label(KeyCommand.NextFile)).IsEqualTo("Ctrl+Right");
        await Assert.That(map.Label(KeyCommand.PreviousFile)).IsEqualTo("Alt+Left");
    }

    [Test]
    public async Task A_json_document_that_is_not_an_object_is_reported()
    {
        var path = WriteFile("""["next-file", "Alt+Right"]""");

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems).IsNotEmpty();
        await Assert.That(map.Label(KeyCommand.NextFile)).IsEqualTo("Ctrl+Right");
    }

    [Test]
    public async Task Problems_from_the_merge_are_reported_too()
    {
        // The loader passes the file's own complaints and the map's merge
        // complaints back through one list, since a user cannot tell (or care)
        // which layer objected.
        var path = WriteFile("""{ "next-file": "Ctrl+S" }""");

        var map = KeyMapStore.Load(path, out var problems);

        await Assert.That(problems.Any(p => p.Contains("save-image"))).IsTrue();
        await Assert.That(map.For(KeyCommand.SaveImage)).IsNull();
    }
}
