using RichEra.Application.Dtos;

namespace RichEra.Tests;

/// <summary>
/// The feed models carry no downloaded media — the card builds every YouTube URL from the id, and
/// a saved search folds case and spacing so "Budgeting " and "budgeting" are one row.
/// </summary>
public class FeedModelTests
{
    [Fact]
    public void ShortCard_builds_youtube_urls_from_the_id()
    {
        var card = new ShortCard { VideoId = "abc123DEF45", Title = "t", ChannelTitle = "c" };

        Assert.Equal("https://i.ytimg.com/vi/abc123DEF45/oar2.jpg", card.PosterUrl);
        Assert.Equal("https://i.ytimg.com/vi/abc123DEF45/hqdefault.jpg", card.PosterFallbackUrl);
        Assert.Equal("https://www.youtube.com/shorts/abc123DEF45", card.WatchUrl);

        // The player is nocookie + playsinline + the js api the dead-embed watchdog needs.
        Assert.StartsWith("https://www.youtube-nocookie.com/embed/abc123DEF45?", card.EmbedUrl);
        Assert.Contains("playsinline=1", card.EmbedUrl);
        Assert.Contains("enablejsapi=1", card.EmbedUrl);
    }

    [Theory]
    [InlineData("Budgeting ", "budgeting")]
    [InlineData("  Index   Funds  ", "index funds")]   // collapses runs of whitespace
    [InlineData("SAVE money", "save money")]
    public void SavedSearch_normalizes_case_and_spacing(string query, string expectedKey)
    {
        Assert.Equal(expectedKey, SavedSearch.Normalize(query));
        Assert.Equal(expectedKey, new SavedSearch { Query = query }.Key);
    }

    [Fact]
    public void SavedSearch_same_text_different_spacing_is_one_key()
    {
        var a = new SavedSearch { Query = "budgeting tips" };
        var b = new SavedSearch { Query = "  Budgeting   Tips " };
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void FeedFilter_is_empty_only_with_no_selections()
    {
        Assert.True(new FeedFilter().IsEmpty);

        var withChannel = new FeedFilter { ChannelIds = new HashSet<string> { "UC1" } };
        Assert.False(withChannel.IsEmpty);
        Assert.Equal(1, withChannel.Count);
    }
}
