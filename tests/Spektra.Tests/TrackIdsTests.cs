using Spektra.Core;

namespace Spektra.Tests;

/// What TrackIds.FromTags makes of a file's tags. Every ID here is invented: a Spotify track ID is
/// any 22 base62 characters, an ISRC is 2 letters, 3 letters or digits and 7 digits.
public sealed class TrackIdsTests
{
    private const string Id = "0aBcDeFgHiJkLmNoPqRsTu";
    private const string OtherId = "1zYxWvUtSrQpOnMlKjIhGf";
    private const string TrackUrl = "https://open.spotify.com/track/";

    private static TrackIds Read(params (string Key, string Value)[] tags) =>
        TrackIds.FromTags(tags.Select(t => new KeyValuePair<string, string>(t.Key, t.Value)));

    [Test]
    [Arguments("isrc", "USRC17607839", "USRC17607839")]
    [Arguments("ISRC", "USRC17607839", "USRC17607839")]
    [Arguments("isrc", "us-rc1-76-07839", "USRC17607839")]
    [Arguments("Isrc", " US RC1 76 07839 ", "USRC17607839")]
    [Arguments("isrc", "US1A27600001", "US1A27600001")]
    [Arguments("TSRC", "USRC17607839", "USRC17607839")]
    [Arguments("tsrc", "gb-xyz-24-00001", "GBXYZ2400001")]
    public async Task FromTags_ReadsAnIsrc(string key, string value, string expected)
    {
        await Assert.That(Read((key, value)).Isrc).IsEqualTo(expected);
    }

    [Test]
    public async Task FromTags_TheFirstIsrcWins()
    {
        await Assert.That(Read(("tsrc", "GBXYZ2400001"), ("isrc", "USRC17607839")).Isrc).IsEqualTo("GBXYZ2400001");
    }

    // A value that is no ISRC gives none, and a valid one in a later tag is then taken.
    [Test]
    [Arguments("USRC1760783", null, null)]                      // 11 characters
    [Arguments("USRC176078399", null, null)]                    // 13 characters
    [Arguments("USRC17A07839", null, null)]                     // a letter among the last seven
    [Arguments("USRC1760783X", null, null)]                     // a letter as the very last
    [Arguments("12RC17607839", null, null)]                     // digits where the country letters go
    [Arguments("---", null, null)]
    [Arguments("", null, null)]
    [Arguments("USRC1760783", "USRC17607839", "USRC17607839")]
    [Arguments("USRC17A07839", "gb-xyz-24-00001", "GBXYZ2400001")]
    public async Task FromTags_SkipsAnIsrcThatIsNotOne(string value, string? later, string? expected)
    {
        var ids = later is null ? Read(("isrc", value)) : Read(("isrc", value), ("tsrc", later));

        await Assert.That(ids.Isrc).IsEqualTo(expected);
    }

    // The four keys in any case, and what a tag made for the ID may hold: it is trimmed, and it is
    // exactly 22 characters of 0-9, A-Z and a-z.
    [Test]
    [Arguments("spotid", Id, Id)]
    [Arguments("SPOTID", Id, Id)]
    [Arguments("spotify_id", Id, Id)]
    [Arguments("Spotify_ID", Id, Id)]
    [Arguments("spotifyid", Id, Id)]
    [Arguments("SpotifyId", Id, Id)]
    [Arguments("spotify_track_id", Id, Id)]
    [Arguments("SPOTIFY_TRACK_ID", Id, Id)]
    [Arguments("spotid", "  " + Id + " ", Id)]
    [Arguments("spotid", "0aBcDeFgHiJkLmNoPqRsT", null)]        // 21 characters
    [Arguments("spotid", Id + "v", null)]                       // 23 characters
    [Arguments("spotid", "0aBcDeFgHiJkLmNoPqRs-u", null)]
    [Arguments("spotid", "0aBcDeFgHiJkLmNoPqRs_u", null)]
    [Arguments("spotid", "0aBcDeFgHiJkLmNoPqRsTé", null)]
    [Arguments("spotid", "", null)]
    public async Task FromTags_ReadsASpotifyIdFromItsOwnTag(string key, string value, string? expected)
    {
        await Assert.That(Read((key, value)).SpotifyId).IsEqualTo(expected);
    }

    // The first one wins, and one that holds no ID is passed over.
    [Test]
    [Arguments(OtherId, Id, OtherId)]
    [Arguments("not an id", OtherId, OtherId)]
    public async Task FromTags_TheFirstOwnTagWins(string first, string second, string expected)
    {
        await Assert.That(Read(("spotify_id", first), ("spotid", second)).SpotifyId).IsEqualTo(expected);
    }

    [Test]
    [Arguments("comment", TrackUrl + Id)]
    [Arguments("comment", TrackUrl + Id + "?si=abc123")]
    [Arguments("comment", "https://open.spotify.com/intl-de/track/" + Id)]
    [Arguments("comment", "https://open.spotify.com/intl-pt-br/track/" + Id + "?si=abc123")]
    [Arguments("comment", "spotify:track:" + Id)]
    [Arguments("comment", "Ripped from " + TrackUrl + Id + " at 320 kbps")]
    [Arguments("comment", "(" + TrackUrl + Id + ")")]
    [Arguments("purl", TrackUrl + Id)]
    [Arguments("WOAF", "spotify:track:" + Id)]
    public async Task FromTags_ReadsASpotifyIdFromALink(string key, string value)
    {
        await Assert.That(Read((key, value)).SpotifyId).IsEqualTo(Id);
    }

    [Test]
    [Arguments("https://open.spotify.com/album/" + Id)]
    [Arguments("https://open.spotify.com/playlist/" + Id)]
    [Arguments("https://open.spotify.com/artist/" + Id)]
    [Arguments("https://open.spotify.com/intl-de/album/" + Id)]
    [Arguments("spotify:album:" + Id)]
    [Arguments("spotify:artist:" + Id)]
    [Arguments(TrackUrl + Id + "v")]                // 23 base62 characters after track/
    [Arguments("spotify:track:" + Id + "7")]
    [Arguments(TrackUrl + "0aBcDeFgHiJkLmNoPqRsT")] // 21
    [Arguments("https://example.com/track/" + Id)]
    [Arguments("track/" + Id)]
    public async Task FromTags_ALinkToSomethingElseIsNoTrackId(string value)
    {
        await Assert.That(Read(("comment", value)).SpotifyId).IsNull();
    }

    [Test]
    public async Task FromTags_TheIdsOwnTagBeatsALink()
    {
        // The link comes first in the list, yet the tag made for the ID is the better witness.
        await Assert.That(Read(("comment", TrackUrl + Id), ("spotid", OtherId)).SpotifyId).IsEqualTo(OtherId);
    }

    // The first link wins, and a link to an album on the way to it is passed over.
    [Test]
    [Arguments(TrackUrl + Id, TrackUrl + OtherId, Id)]
    [Arguments("https://open.spotify.com/album/" + Id, "spotify:track:" + OtherId, OtherId)]
    public async Task FromTags_TheFirstLinkWins(string first, string second, string expected)
    {
        await Assert.That(Read(("comment", first), ("purl", second)).SpotifyId).IsEqualTo(expected);
    }

    [Test]
    public async Task FromTags_ReadsBothIdsFromOneSetOfTags()
    {
        var ids = Read(("comment", TrackUrl + Id), ("title", "Song Title"), ("spotid", OtherId), ("TSRC", "us-rc1-76-07839"));

        await Assert.That(ids).IsEqualTo(new TrackIds(OtherId, "USRC17607839"));
    }

    // The name must be the whole key: a key that only contains it is some other tag.
    [Test]
    [Arguments("isrc_note", "USRC17607839")]
    [Arguments("my_tsrc", "USRC17607839")]
    [Arguments("my_spotid", Id)]
    [Arguments("spotify_id2", Id)]
    public async Task FromTags_AKeyThatOnlyContainsAName_IsNotThatTag(string key, string value)
    {
        await Assert.That(Read((key, value))).IsEqualTo(TrackIds.None);
    }

    [Test]
    public async Task FromTags_NothingFound_IsNone()
    {
        var ids = Read(("title", "Song Title"), ("artist", "Artist Name"), ("comment", "no link here"));

        await Assert.That(ids).IsEqualTo(TrackIds.None);
        await Assert.That(ids.IsEmpty).IsTrue();
    }

    [Test]
    public async Task FromTags_NoTagsAtAll_IsNone()
    {
        await Assert.That(Read()).IsEqualTo(TrackIds.None);
    }

    // One ID alone is not empty.
    [Test]
    [Arguments(null, null, true)]
    [Arguments(Id, null, false)]
    [Arguments(null, "USRC17607839", false)]
    [Arguments(Id, "USRC17607839", false)]
    public async Task IsEmpty_IsTrueOnlyWhenNeitherIdIsThere(string? spotifyId, string? isrc, bool expected)
    {
        await Assert.That(new TrackIds(spotifyId, isrc).IsEmpty).IsEqualTo(expected);
    }
}
