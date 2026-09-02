using Spektra.Core;

namespace Spektra.Tests;

public sealed class ScopeLabelTests
{
    [Test]
    [Arguments(@"D:\Music", null, "Music")]
    [Arguments(@"D:\Music\", null, "Music")]
    [Arguments(@"D:\Music", @"D:\Music\##GAMES##", "##GAMES##")]
    [Arguments(@"D:\Music", @"D:\Music\Albums\", "Albums")]
    [Arguments(@"D:\Music", @"D:\Music", "Music")]
    [Arguments(@"C:\", null, @"C:\")]
    [Arguments(@"D:\My_Music", null, "My__Music")]
    [Arguments(@"D:\Music", @"D:\Music\My_Mixes", "My__Mixes")]
    public async Task ForMenu_names_the_effective_scope(
        string root, string? scope, string expected)
    {
        await Assert.That(ScopeLabel.ForMenu(root, scope)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(@"D:\Music", null, "Music")]
    [Arguments(@"D:\Music", @"D:\Music\Albums\", "Albums")]
    [Arguments(@"C:\", null, @"C:\")]
    [Arguments(@"D:\My_Music", null, "My_Music")]
    public async Task For_names_the_same_scope_without_menu_escaping(
        string root, string? scope, string expected)
    {
        // The status bar is not a menu header: a doubled underscore would
        // render literally there, so the two callers need the same rule with
        // only the mnemonic escaping differing.
        await Assert.That(ScopeLabel.For(root, scope)).IsEqualTo(expected);
    }
}
