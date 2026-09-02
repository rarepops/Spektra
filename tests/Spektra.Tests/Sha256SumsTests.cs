using Spektra.Core;

namespace Spektra.Tests;

public class Sha256SumsTests
{
    private const string A = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string B = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";

    [Test]
    public async Task Parse_ReadsTheTwoSpaceFormatTheReleaseJobWrites()
    {
        var sums = Sha256Sums.Parse($"{A}  Spektra-0.23.0-Setup.msi\n{B}  spektra-cli-0.23.0-linux-x64.zip\n");
        await Assert.That(sums.Count).IsEqualTo(2);
        await Assert.That(sums["Spektra-0.23.0-Setup.msi"]).IsEqualTo(A);
        await Assert.That(sums["spektra-cli-0.23.0-linux-x64.zip"]).IsEqualTo(B);
    }

    [Test]
    public async Task Parse_ToleratesCrlf_BlankLines_AndTheBinaryMarker()
    {
        var sums = Sha256Sums.Parse($"\r\n{A} *Spektra-0.23.0-Setup.msi\r\n\r\n");
        await Assert.That(sums.Count).IsEqualTo(1);
        await Assert.That(sums["Spektra-0.23.0-Setup.msi"]).IsEqualTo(A);
    }

    [Test]
    public async Task Parse_NormalizesHexToLowercase()
    {
        var sums = Sha256Sums.Parse($"{B.ToUpperInvariant()}  x.zip");
        await Assert.That(sums["x.zip"]).IsEqualTo(B);
    }

    [Test]
    public async Task Parse_SkipsLinesThatAreNotASha256AndAName()
    {
        var sums = Sha256Sums.Parse($"deadbeef  short.zip\nnot a hash at all\n{A}  good.zip\n{A}\n{A}  \n");
        await Assert.That(sums.Count).IsEqualTo(1);
        await Assert.That(sums.ContainsKey("good.zip")).IsTrue();
    }

    [Test]
    public async Task Parse_KeepsSpacesInsideAName()
    {
        var sums = Sha256Sums.Parse($"{A}  My File.zip");
        await Assert.That(sums["My File.zip"]).IsEqualTo(A);
    }

    [Test]
    public async Task Parse_LooksNamesUpRegardlessOfCase()
    {
        var sums = Sha256Sums.Parse($"{A}  Spektra-0.23.0-Setup.msi");
        await Assert.That(sums.TryGetValue("spektra-0.23.0-setup.msi", out var hex)).IsTrue();
        await Assert.That(hex).IsEqualTo(A);
    }

    [Test]
    public async Task Parse_Empty_IsEmpty()
    {
        await Assert.That(Sha256Sums.Parse("").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Matches_ComparesHexRegardlessOfCase()
    {
        await Assert.That(Sha256Sums.Matches(A, A)).IsTrue();
        await Assert.That(Sha256Sums.Matches(B, B.ToUpperInvariant())).IsTrue();
        await Assert.That(Sha256Sums.Matches(A, B)).IsFalse();
    }
}
