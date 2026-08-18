using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

/// <summary>
/// The /feed page's content: every followed channel's and saved search's Shorts, pooled,
/// deduped and shuffled.
/// </summary>
public interface IShortsFeedService
{
    /// <summary>
    /// Draws up to <paramref name="count"/> shorts from the pool, skipping any whose id is in
    /// <paramref name="exclude"/> (what the page is already showing) and, when
    /// <paramref name="filter"/> is set, drawing from that one source only. Returns fewer — or
    /// none — when the pool is exhausted.
    /// <para>
    /// Never throws for a source that can't be reached: a channel that's down falls back to its
    /// cached listing, and failing that simply contributes nothing. One dead source must not
    /// empty the whole feed.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ShortCard>> GetShortsAsync(
        int count, IReadOnlySet<string>? exclude = null, FeedFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Every bookmarked short still reachable from the current sources, newest first and shown
    /// whole rather than drawn. <paramref name="filter"/> narrows it the same way it narrows the
    /// feed, so "saved" and a source selection combine instead of overriding each other.
    /// </summary>
    Task<IReadOnlyList<ShortCard>> GetSavedShortsAsync(
        FeedFilter? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Drops every cached listing so the next draw re-fetches. What the "refresh" action on the
    /// sources page does.
    /// </summary>
    Task RefreshAllAsync(CancellationToken ct = default);

    /// <summary>Rough size of the on-disk listing cache, for the sources page to show.</summary>
    long CacheBytes();
}
