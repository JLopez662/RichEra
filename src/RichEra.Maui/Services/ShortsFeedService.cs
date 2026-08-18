using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;

namespace RichEra.Maui.Services;

/// <summary>
/// Pools every followed channel's and saved search's Shorts into one hat and deals from it.
///
/// <para>
/// Singleton, and deliberately nowhere near the EF layer: the whole feature is HTTP + files, so
/// it never touches the shared scoped <c>AppDbContext</c> and the per-source fetches below are
/// free to run in parallel.
/// </para>
/// </summary>
public sealed class ShortsFeedService : IShortsFeedService
{
    private readonly YouTubeCatalog _catalog;
    private readonly IFeedSourceService _sources;

    public ShortsFeedService(YouTubeCatalog catalog, IFeedSourceService sources)
    {
        _catalog = catalog;
        _sources = sources;
    }

    public async Task<IReadOnlyList<ShortCard>> GetShortsAsync(
        int count, IReadOnlySet<string>? exclude = null, FeedFilter? filter = null,
        CancellationToken ct = default)
    {
        if (count <= 0) return [];

        var pool = await BuildPoolAsync(filter, ct).ConfigureAwait(false);
        if (pool.Count == 0) return [];

        if (exclude is { Count: > 0 })
            pool = pool.Where(card => !exclude.Contains(card.VideoId)).ToList();
        if (pool.Count == 0) return [];

        // Everything is in the same hat, so a source with more shorts shows up more — the same way
        // a busier channel fills more of a real subscription feed. Drawn without replacement, so a
        // batch never repeats itself.
        return Shuffle(pool).Take(count).ToList();
    }

    public async Task<IReadOnlyList<ShortCard>> GetSavedShortsAsync(
        FeedFilter? filter = null, CancellationToken ct = default)
    {
        var saved = await _sources.GetSavedAsync(ct).ConfigureAwait(false);
        if (saved.Count == 0) return [];

        var pool = await BuildPoolAsync(filter, ct).ConfigureAwait(false);

        // Newest first, and anything undated (a Shorts-tab hit states no publish date) sorts last
        // rather than jumping the queue as DateTimeOffset.MinValue would if compared directly.
        return pool.Where(card => card.Saved)
                   .OrderByDescending(card => card.Published.HasValue)
                   .ThenByDescending(card => card.Published)
                   .ToList();
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        _catalog.ForgetAll();

        // Warm it straight back up so the page that asked doesn't render an empty feed for the
        // second or two the re-fetch takes.
        await BuildPoolAsync(null, ct).ConfigureAwait(false);
    }

    public long CacheBytes() => _catalog.CacheBytes();

    // ----------------------------------------------------------------

    /// <summary>
    /// Every short currently reachable from the configured sources, deduped and with the viewer's
    /// marks applied. A source that can't be reached contributes nothing rather than throwing —
    /// one dead channel must not empty the feed.
    /// <para>
    /// A <paramref name="filter"/> narrows which sources are *asked*, not which results are kept:
    /// filtering to one channel then costs one request instead of one per source.
    /// </para>
    /// </summary>
    private async Task<List<ShortCard>> BuildPoolAsync(FeedFilter? filter, CancellationToken ct)
    {
        // Muted sources are dropped before anything is fetched, so a switched-off channel costs
        // no request at all — and its cached listing stays on disk for when it's switched back on.
        var channels = (await _sources.GetChannelsAsync(ct).ConfigureAwait(false))
            .Where(c => c.Enabled).ToList();
        var searches = (await _sources.GetSearchesAsync(ct).ConfigureAwait(false))
            .Where(s => s.Enabled).ToList();

        // Selections union: two channels and a search draw from all three. Applied to BOTH lists
        // in one pass — narrowing channels while leaving searches untouched (or vice versa) would
        // quietly widen the result past what was actually selected.
        if (filter is { IsEmpty: false })
        {
            channels = channels.Where(c => filter.ChannelIds.Contains(c.ChannelId)).ToList();
            searches = searches.Where(s => filter.SearchKeys.Contains(s.Key)).ToList();
        }

        if (channels.Count == 0 && searches.Count == 0) return [];

        var saved = await _sources.GetSavedAsync(ct).ConfigureAwait(false);
        var hidden = await _sources.GetHiddenAsync(ct).ConfigureAwait(false);

        var fromChannels = Task.WhenAll(channels.Select(async channel =>
        {
            var videos = await _catalog.GetChannelShortsAsync(channel.ChannelId, ct).ConfigureAwait(false);

            // A channel's own listing carries no per-video creator: it's the channel, by definition.
            return videos.Select(v => new ShortCard
            {
                VideoId = v.Id,
                Title = v.Title,
                ChannelTitle = channel.Title,
                ChannelHandle = channel.Handle,
                Published = v.Published,
            });
        }));

        var fromSearches = Task.WhenAll(searches.Select(async search =>
        {
            var videos = await _catalog.SearchShortsAsync(search.Query, ct).ConfigureAwait(false);

            // A search spans creators, so each hit is attributed to whoever posted it — and the
            // card also says which search surfaced it.
            return videos.Select(v => new ShortCard
            {
                VideoId = v.Id,
                Title = v.Title,
                ChannelTitle = v.ChannelTitle ?? "YouTube",
                ChannelHandle = v.ChannelHandle ?? "",
                FoundVia = search.Query,
                Published = v.Published,
            });
        }));

        await Task.WhenAll(fromChannels, fromSearches).ConfigureAwait(false);

        // Deduped by video id: a short can be both on a followed channel and in a followed search,
        // and it shouldn't get two tickets. The channel listing is preferred (it's grouped first),
        // so "Following · X" wins over the search attribution for the same video.
        return fromChannels.Result.SelectMany(x => x)
            .Concat(fromSearches.Result.SelectMany(x => x))
            .Where(card => !hidden.Contains(card.VideoId))
            .GroupBy(card => card.VideoId)
            .Select(group => group.First() with { Saved = saved.Contains(group.Key) })
            .ToList();
    }

    /// <summary>Fisher–Yates over a copy. <see cref="Random.Shared"/> is thread-safe.</summary>
    private static List<ShortCard> Shuffle(List<ShortCard> pool)
    {
        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }
}
