using Spektra.Core;

namespace Spektra.Tests;

public sealed class KeyStrokeTests
{
    [Test]
    [Arguments("Ctrl+O", "O", KeyMods.Ctrl)]
    [Arguments("Ctrl+Shift+S", "S", KeyMods.Ctrl | KeyMods.Shift)]
    [Arguments("F5", "F5", KeyMods.None)]
    [Arguments("Shift+F5", "F5", KeyMods.Shift)]
    [Arguments("Ctrl+Left", "Left", KeyMods.Ctrl)]
    [Arguments("Alt+Enter", "Enter", KeyMods.Alt)]
    public async Task Parses_a_gesture_into_a_key_and_its_modifiers(
        string text, string key, KeyMods mods)
    {
        await Assert.That(KeyStroke.TryParse(text, out var stroke)).IsTrue();
        await Assert.That(stroke.Key).IsEqualTo(key);
        await Assert.That(stroke.Mods).IsEqualTo(mods);
    }

    [Test]
    [Arguments("ctrl+shift+s")]
    [Arguments("CTRL+SHIFT+S")]
    [Arguments("  Ctrl + Shift + S  ")]
    [Arguments("Control+Shift+S")]
    public async Task Modifier_names_are_forgiving(string text)
    {
        // Someone hand-editing JSON should not have to guess our casing or
        // spacing, and "Control" is what Windows itself calls the key.
        await Assert.That(KeyStroke.TryParse(text, out var stroke)).IsTrue();
        await Assert.That(stroke).IsEqualTo(new KeyStroke("S", KeyMods.Ctrl | KeyMods.Shift));
    }

    [Test]
    [Arguments("Ctrl+0", "D0")]
    [Arguments("Ctrl+9", "D9")]
    [Arguments("Esc", "Escape")]
    [Arguments("Escape", "Escape")]
    public async Task Human_key_names_normalize_to_the_framework_name(
        string text, string canonical)
    {
        // The shell matches against Avalonia's Key enum name, where the "0"
        // key is called D0. Nobody hand-writing a binding would ever guess
        // that, so both spellings parse to the one canonical name.
        await Assert.That(KeyStroke.TryParse(text, out var stroke)).IsTrue();
        await Assert.That(stroke.Key).IsEqualTo(canonical);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Ctrl+")]
    [Arguments("Ctrl")]
    [Arguments("Ctrl+Shift")]
    [Arguments("+S")]
    [Arguments("Ctrl+O+P")]
    public async Task Junk_does_not_parse(string text)
    {
        await Assert.That(KeyStroke.TryParse(text, out _)).IsFalse();
    }

    [Test]
    [Arguments("Ctrl+Shift+S", "Ctrl+Shift+S")]
    [Arguments("shift+ctrl+s", "Ctrl+Shift+S")]
    [Arguments("Ctrl+0", "Ctrl+0")]
    [Arguments("Escape", "Esc")]
    [Arguments("F5", "F5")]
    public async Task Renders_back_to_the_spelling_the_documentation_uses(
        string text, string rendered)
    {
        // Modifiers always render Ctrl, Shift, Alt in that order regardless of
        // how they were written, so the menus and the F1 window cannot show
        // two spellings of one gesture.
        KeyStroke.TryParse(text, out var stroke);
        await Assert.That(stroke.ToString()).IsEqualTo(rendered);
    }

    [Test]
    public async Task A_rendered_gesture_parses_back_to_itself()
    {
        foreach (var text in new[] { "Ctrl+Shift+S", "Ctrl+0", "Esc", "F5", "Ctrl+Left" })
        {
            KeyStroke.TryParse(text, out var first);
            KeyStroke.TryParse(first.ToString(), out var second);
            await Assert.That(second).IsEqualTo(first).Because($"{text} must round-trip");
        }
    }
}
