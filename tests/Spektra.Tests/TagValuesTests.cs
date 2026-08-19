using Spektra.Core;

namespace Spektra.Tests;

/// Tag values arrive from ffprobe as free text and every tagger writes them
/// differently. Normalizing once here is what keeps a downstream consumer from
/// re-deriving "5/12" and three date shapes over thousands of rows.
public class TagValuesTests
{
    [Test]
    public async Task SplitNumber_SeparatesValueFromTotal()
    {
        await Assert.That(TagValues.SplitNumber("5/12")).IsEqualTo((5, 12));
        await Assert.That(TagValues.SplitNumber("5")).IsEqualTo((5, (int?)null));
        // Leading zeros are how most taggers write single digits.
        await Assert.That(TagValues.SplitNumber("05/12")).IsEqualTo((5, 12));
        await Assert.That(TagValues.SplitNumber(" 3 / 9 ")).IsEqualTo((3, 9));
    }

    [Test]
    public async Task SplitNumber_UnusableInputIsNull_NotZero()
    {
        // Zero would read as "track 0", a real position; null reads as "no
        // track", which is the truth. The distinction survives into the export.
        await Assert.That(TagValues.SplitNumber(null)).IsEqualTo(((int?)null, (int?)null));
        await Assert.That(TagValues.SplitNumber("")).IsEqualTo(((int?)null, (int?)null));
        await Assert.That(TagValues.SplitNumber("   ")).IsEqualTo(((int?)null, (int?)null));
        await Assert.That(TagValues.SplitNumber("A-side")).IsEqualTo(((int?)null, (int?)null));
        // A total with no value is still a missing value, not a shifted one.
        await Assert.That(TagValues.SplitNumber("/12")).IsEqualTo(((int?)null, 12));
    }

    [Test]
    public async Task Year_ReadsEveryDateShapeTaggersWrite()
    {
        await Assert.That(TagValues.Year("2019")).IsEqualTo(2019);
        await Assert.That(TagValues.Year("2019-04-12")).IsEqualTo(2019);
        await Assert.That(TagValues.Year("2019-04-12T00:00:00Z")).IsEqualTo(2019);
        // Real files carry padded dates; the year is still good.
        await Assert.That(TagValues.Year("1997-00-00")).IsEqualTo(1997);
        await Assert.That(TagValues.Year(" 1984 ")).IsEqualTo(1984);
    }

    [Test]
    public async Task Year_RejectsWhatIsNotAYear()
    {
        await Assert.That(TagValues.Year(null)).IsNull();
        await Assert.That(TagValues.Year("")).IsNull();
        await Assert.That(TagValues.Year("unknown")).IsNull();
        // A two-digit year is ambiguous (1998 or 2098?), so it is not guessed.
        await Assert.That(TagValues.Year("98")).IsNull();
        // Taggers write an all-zero date to mean "unknown". Reporting year 0
        // would hand a consumer a release date to act on that never existed.
        await Assert.That(TagValues.Year("0000")).IsNull();
        await Assert.That(TagValues.Year("0000-00-00")).IsNull();
    }

    [Test]
    public async Task Text_TrimsAndTreatsBlankAsAbsent()
    {
        await Assert.That(TagValues.Text("  Aurora  ")).IsEqualTo("Aurora");
        await Assert.That(TagValues.Text("Aurora")).IsEqualTo("Aurora");
        // "" and "   " mean the tag is there but says nothing, which is the
        // same fact as no tag: null. Keeping "" would make an export claim a
        // file has an artist whose name is blank.
        await Assert.That(TagValues.Text("")).IsNull();
        await Assert.That(TagValues.Text("   ")).IsNull();
        await Assert.That(TagValues.Text(null)).IsNull();
    }
}
