using RadioV2.Helpers;
using Xunit;

namespace RadioV2.Tests;

public class NowPlayingParserTests
{
    [Fact]
    public void Parse_ArtistAndTitle_SplitsOnHyphen()
    {
        var (artist, title) = NowPlayingParser.Parse("Pink Floyd - Time");
        Assert.Equal("Pink Floyd", artist);
        Assert.Equal("Time", title);
    }

    [Theory]
    [InlineData("Pink Floyd – Time")] // en dash
    [InlineData("Pink Floyd — Time")] // em dash
    public void Parse_AcceptsDashVariants(string raw)
    {
        var (artist, title) = NowPlayingParser.Parse(raw);
        Assert.Equal("Pink Floyd", artist);
        Assert.Equal("Time", title);
    }

    [Fact]
    public void Parse_BareTitle_ReturnsTitleOnly()
    {
        var (artist, title) = NowPlayingParser.Parse("Morning Show");
        Assert.Null(artist);
        Assert.Equal("Morning Show", title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_ReturnsNothing(string? raw)
    {
        var (artist, title) = NowPlayingParser.Parse(raw);
        Assert.Null(artist);
        Assert.Null(title);
    }

    [Fact]
    public void Parse_TrimsSurroundingWhitespace()
    {
        var (artist, title) = NowPlayingParser.Parse("   Pink Floyd - Time   ");
        Assert.Equal("Pink Floyd", artist);
        Assert.Equal("Time", title);
    }

    [Fact]
    public void Parse_SeparatorsArePreferredInOrder_NotByPosition()
    {
        // " - " is tried across the whole string before the dash variants, so a later
        // hyphen wins over an earlier en dash. Pinning current behaviour: a change here
        // would silently re-split every station using mixed punctuation.
        var (artist, title) = NowPlayingParser.Parse("A – B - C");
        Assert.Equal("A – B", artist);
        Assert.Equal("C", title);
    }

    [Fact]
    public void Parse_LeadingSeparator_IsNotTreatedAsArtist()
    {
        // idx > 0 guard: a title starting with the separator has no artist.
        var (artist, title) = NowPlayingParser.Parse("- Unknown Track");
        Assert.Null(artist);
        Assert.Equal("- Unknown Track", title);
    }

    [Fact]
    public void Parse_EmptyTitleAfterSeparator_FallsBackToWholeString()
    {
        var (artist, title) = NowPlayingParser.Parse("Artist - ");
        Assert.Null(artist);
        Assert.Equal("Artist -", title);
    }
}
