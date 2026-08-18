using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

/// <summary>
/// The followed-channel and saved-search lists behind the /feed page, plus the two per-short
/// marks the feed keeps (saved, hidden). Everything here is device-local (MAUI Preferences).
/// </summary>
public interface IFeedSourceService
{
    /// <summary>Followed channels, in the order they were added (seeds first).</summary>
    Task<IReadOnlyList<FollowedChannel>> GetChannelsAsync(CancellationToken ct = default);

    /// <summary>Saved searches, in the order they were added (seeds first).</summary>
    Task<IReadOnlyList<SavedSearch>> GetSearchesAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds whatever the user typed: a channel (handle / any YouTube URL / a link to one short)
    /// or, when it isn't a channel reference at all, a search term. Adding a channel costs one
    /// request to resolve it; adding a search costs nothing.
    /// </summary>
    Task<SourceAddResult> AddAsync(string input, CancellationToken ct = default);

    /// <summary>Unfollows a channel and drops its cached listing.</summary>
    Task RemoveChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Removes a saved search and drops its cached results.</summary>
    Task RemoveSearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Mutes or un-mutes a channel. A disabled source keeps its row and its cached listing, so
    /// this is the cheap, reversible alternative to unfollowing. No-op for an unknown id.
    /// </summary>
    Task SetChannelEnabledAsync(string channelId, bool enabled, CancellationToken ct = default);

    /// <summary>The same, for a saved search.</summary>
    Task SetSearchEnabledAsync(string query, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Resolves the finance seed channels (stored by handle) to their UC… ids once per install,
    /// over the network. Best-effort and idempotent: kicked off in the background at startup so a
    /// fresh install lands on a populated feed without blocking the first frame. Searches are
    /// seeded synchronously and carry the feed until this completes.
    /// </summary>
    Task EnsureSeedChannelsAsync(CancellationToken ct = default);

    // ---------------------------------------------------------------- per-short marks

    /// <summary>Video ids the viewer bookmarked.</summary>
    Task<IReadOnlySet<string>> GetSavedAsync(CancellationToken ct = default);

    /// <summary>Toggles the bookmark. Returns the new state.</summary>
    Task<bool> ToggleSavedAsync(string videoId, CancellationToken ct = default);

    /// <summary>Video ids the viewer asked never to see again. Removed from the pool outright.</summary>
    Task<IReadOnlySet<string>> GetHiddenAsync(CancellationToken ct = default);

    /// <summary>Hides a short for good. Undone in bulk by <see cref="ClearHiddenAsync"/>.</summary>
    Task HideAsync(string videoId, CancellationToken ct = default);

    /// <summary>Un-hides everything, so all those shorts re-enter the pool.</summary>
    Task ClearHiddenAsync(CancellationToken ct = default);
}
