namespace RichEra.Application.Dtos;

/// <summary>
/// A YouTube channel the user follows. Its Shorts ride in the /feed page.
/// <para>
/// The <see cref="ChannelId"/> (UC…) is the stable key — resolved once when the channel was
/// added and matched on ever after, so pasting the handle for a channel added by URL (or the
/// other way round) is recognised as the same channel rather than following it twice.
/// </para>
/// </summary>
public sealed record FollowedChannel
{
    /// <summary>The channel's UC… id.</summary>
    public required string ChannelId { get; init; }

    /// <summary>Handle without the '@', e.g. "GrahamStephan". Empty when the page didn't state one.</summary>
    public string Handle { get; init; } = "";

    /// <summary>Display name, e.g. "Graham Stephan".</summary>
    public string Title { get; init; } = "";

    /// <summary>Shipped with the app on first run, rather than added by the user. Purely a
    /// label — a seed is otherwise just another row that can be unfollowed.</summary>
    public bool IsSeed { get; init; }

    /// <summary>
    /// Off means the channel contributes nothing to the feed, but keeps its row and its cached
    /// listing — so muting a channel for a while and switching it back on is instant, where
    /// unfollowing and re-adding costs a fresh resolve and a fresh fetch.
    /// </summary>
    public bool Enabled { get; init; } = true;

    public DateTime AddedUtc { get; init; }
}

/// <summary>
/// A search whose Shorts results ride in the feed. Unlike a channel these span creators, so
/// each hit is attributed to whoever posted it and the card also says which search found it.
/// </summary>
public sealed record SavedSearch
{
    /// <summary>What the user typed, preserved for display.</summary>
    public required string Query { get; init; }

    public bool IsSeed { get; init; }

    /// <summary>Off means the search contributes nothing to the feed but keeps its row and its
    /// cached results. See <see cref="FollowedChannel.Enabled"/>.</summary>
    public bool Enabled { get; init; } = true;

    public DateTime AddedUtc { get; init; }

    /// <summary>Case- and whitespace-insensitive key, so "Budgeting " and "budgeting" are one search.</summary>
    public string Key => Normalize(Query);

    public static string Normalize(string query) =>
        string.Join(' ', (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries
                                                 | StringSplitOptions.TrimEntries))
              .ToLowerInvariant();
}

/// <summary>
/// A channel named one of the ways a person actually has one to hand: a handle, or any of the
/// URLs YouTube hands out. Exactly one of the three properties is set.
/// </summary>
public sealed record ChannelQuery
{
    private ChannelQuery() { }

    /// <summary>Handle without the '@'.</summary>
    public string? Handle { get; private init; }

    /// <summary>A UC… channel id.</summary>
    public string? ChannelId { get; private init; }

    /// <summary>A video or short — meaning "the channel that posted this", which is how someone
    /// who just watched something usually has the channel to hand.</summary>
    public string? VideoId { get; private init; }

    public static ChannelQuery FromHandle(string handle) => new() { Handle = handle };
    public static ChannelQuery FromChannelId(string channelId) => new() { ChannelId = channelId };
    public static ChannelQuery FromVideo(string videoId) => new() { VideoId = videoId };
}

/// <summary>A channel page, read. <paramref name="Handle"/> is empty when the page didn't state one.</summary>
public sealed record ResolvedChannel(string ChannelId, string Handle, string Title);

public enum SourceAddOutcome
{
    /// <summary>Followed / saved.</summary>
    Added,

    /// <summary>Already in the list — not an error, but not a change either.</summary>
    Duplicate,

    /// <summary>It parsed as a channel reference, but YouTube has no such channel.</summary>
    NotFound,

    /// <summary>We couldn't ask. Offline, rate-limited, or YouTube changed shape — deliberately
    /// NOT collapsed into <see cref="NotFound"/>: "doesn't exist" and "couldn't check" are
    /// different answers to the user.</summary>
    Unreachable,

    /// <summary>Nothing usable was typed.</summary>
    Empty,
}

/// <param name="ChannelTitle">The channel's display name, when a channel was what got added.</param>
public sealed record SourceAddResult(SourceAddOutcome Outcome, string? ChannelTitle = null, string? Query = null)
{
    public static readonly SourceAddResult Empty = new(SourceAddOutcome.Empty);
}

/// <summary>
/// Narrows the draw to a chosen set of sources — what selecting bubbles on the feed's source
/// rail does. Selections <b>union</b>: picking two channels and a search draws from all three.
/// An empty filter (or a null one) means the whole pool.
/// </summary>
public sealed record FeedFilter
{
    /// <summary>Followed channels' UC… ids.</summary>
    public IReadOnlySet<string> ChannelIds { get; init; } = new HashSet<string>();

    /// <summary>Saved searches, as <see cref="SavedSearch.Normalize"/> keys — not raw queries.</summary>
    public IReadOnlySet<string> SearchKeys { get; init; } = new HashSet<string>();

    /// <summary>Nothing selected, so nothing is narrowed.</summary>
    public bool IsEmpty => ChannelIds.Count == 0 && SearchKeys.Count == 0;

    public int Count => ChannelIds.Count + SearchKeys.Count;
}

/// <summary>
/// One YouTube Short in the feed. Nothing is downloaded: the card only carries the ids the UI
/// needs to build a poster frame and, once the post is tapped, a player URL.
/// </summary>
public sealed record ShortCard
{
    /// <summary>11-character YouTube video id.</summary>
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>Channel display name, e.g. "Graham Stephan".</summary>
    public required string ChannelTitle { get; init; }

    /// <summary>Channel handle without the '@'. Empty when a search turned this up and YouTube
    /// didn't say who posted it.</summary>
    public string ChannelHandle { get; init; } = "";

    /// <summary>The search that found this short, when it came from one. Null means it came from
    /// a followed channel, which the card says instead.</summary>
    public string? FoundVia { get; init; }

    /// <summary>The viewer bookmarked this one. Shown in the Saved filter.</summary>
    public bool Saved { get; init; }

    public DateTimeOffset? Published { get; init; }

    /// <summary>
    /// Poster frame at the video's own aspect — the vertical frame as shot. This is the one
    /// YouTube generates for vertical video; the "oardefault" name that turns up in older
    /// write-ups 404s. Not guaranteed, hence <see cref="PosterFallbackUrl"/>.
    /// </summary>
    public string PosterUrl => $"https://i.ytimg.com/vi/{VideoId}/oar2.jpg";

    /// <summary>Present for every video ever uploaded, but 4:3 with the short pillarboxed inside
    /// it — the CSS crops to the card, so the fallback costs framing, not layout.</summary>
    public string PosterFallbackUrl => $"https://i.ytimg.com/vi/{VideoId}/hqdefault.jpg";

    /// <summary>
    /// The player, loaded only after a tap. The nocookie host keeps the WebView from picking up
    /// tracking cookies; <c>playsinline</c> keeps the video in the card instead of handing it to
    /// the system player; <c>fs=0</c> hides the fullscreen button, which the WebView has no
    /// handler for and would otherwise do nothing; <c>enablejsapi</c> lets the card hear that a
    /// video refused to play here (see richEraShorts in feed-shorts.js) instead of showing a dead frame.
    /// </summary>
    public string EmbedUrl =>
        $"https://www.youtube-nocookie.com/embed/{VideoId}" +
        "?autoplay=1&playsinline=1&rel=0&modestbranding=1&fs=0&enablejsapi=1";

    /// <summary>Where "Open in YouTube" goes — and the way out when a video refuses to embed.</summary>
    public string WatchUrl => $"https://www.youtube.com/shorts/{VideoId}";
}
