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
}
