using RichEra.Application.Dtos;

namespace RichEra.Tests;

/// <summary>
/// The "add a source" box takes whatever a person pastes and decides whether it's a channel
/// reference (→ resolve it) or a plain search (→ null). These pin the shapes YouTube actually
/// hands out, and the one rule that isn't obvious: a bare word is a search, not a handle.
/// </summary>
public class FeedChannelInputTests
{
    // ---- handles ----

    [Theory]
    [InlineData("@GrahamStephan", "GrahamStephan")]
    [InlineData("@humphrey", "humphrey")]
    [InlineData("youtube.com/@marktilbury", "marktilbury")]
    [InlineData("https://www.youtube.com/@MinorityMindset", "MinorityMindset")]
    [InlineData("https://youtube.com/@nischa/shorts", "nischa")]   // tab suffix dropped
    [InlineData("https://m.youtube.com/@nischa", "nischa")]        // mobile host
    public void Parses_handles(string input, string expectedHandle)
    {
        var query = FeedChannelInput.Parse(input);

        Assert.NotNull(query);
        Assert.Equal(expectedHandle, query!.Handle);
        Assert.Null(query.ChannelId);
        Assert.Null(query.VideoId);
    }

    // ---- channel ids ----

    [Theory]
    [InlineData("UCV6KDgJskWaEckne5aPA0aQ")]                                        // bare id
    [InlineData("https://www.youtube.com/channel/UCV6KDgJskWaEckne5aPA0aQ")]        // channel URL
    [InlineData("youtube.com/channel/UCV6KDgJskWaEckne5aPA0aQ/videos")]             // + tab
    public void Parses_channel_ids(string input)
    {
        var query = FeedChannelInput.Parse(input);

        Assert.NotNull(query);
        Assert.Equal("UCV6KDgJskWaEckne5aPA0aQ", query!.ChannelId);
        Assert.Null(query.Handle);
        Assert.Null(query.VideoId);
    }

    // ---- videos (stand in for "the channel that posted this") ----

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?feature=share&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc123", "dQw4w9WgXcQ")]   // share tracking dropped
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Parses_videos(string input, string expectedVideoId)
    {
        var query = FeedChannelInput.Parse(input);

        Assert.NotNull(query);
        Assert.Equal(expectedVideoId, query!.VideoId);
        Assert.Null(query.Handle);
        Assert.Null(query.ChannelId);
    }

    // ---- searches (null = "not a channel reference") ----

    [Theory]
    [InlineData("budgeting tips")]      // a bare phrase is a search, never a silent @budgeting
    [InlineData("index funds")]
    [InlineData("how to save money")]
    [InlineData("")]                    // nothing typed
    [InlineData("   ")]
    [InlineData("@ab")]                 // handle too short (min 3) → treated as not-a-channel
    public void Treats_non_channel_text_as_search(string input)
    {
        Assert.Null(FeedChannelInput.Parse(input));
    }

    [Fact]
    public void Bare_word_is_a_search_even_though_it_could_be_a_handle()
    {
        // "mrbeast" with no '@' and no host is a search, not @mrbeast — following a channel the
        // user never asked for would be worse than a search that returns his clips anyway.
        Assert.Null(FeedChannelInput.Parse("mrbeast"));
    }
}
