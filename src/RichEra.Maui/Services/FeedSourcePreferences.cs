using System.Text.Json;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;

namespace RichEra.Maui.Services;

/// <summary>
/// The feed's source lists, Preferences-backed. A handful of rows that only this device cares
/// about — no SQLite table and no schema migration.
///
/// <para>
/// Every read re-parses the blob rather than holding a cached copy. That's deliberate: a restore
/// writes straight into Preferences without restarting the app, so a long-lived in-memory list
/// would keep serving the pre-restore sources until the next launch. The blobs are a few hundred
/// bytes and are read a handful of times per page, so the parse costs nothing worth keeping a
/// staleness bug for.
/// </para>
///
/// <para>
/// Singleton, with all read-modify-write behind one gate: "add" spans an await (resolving the
/// channel over HTTP) and two concurrent adds would otherwise clobber each other's list.
/// </para>
/// </summary>
public sealed class FeedSourcePreferences : IFeedSourceService
{
    private const string ChannelsKey = "feed_channels_v1";
    private const string SearchesKey = "feed_searches_v1";
    private const string SavedKey = "feed_saved_v1";
    private const string HiddenKey = "feed_hidden_v1";
    private const string SeededKey = "feed_seeded_v1";

    /// <summary>Its own flag: the search seeds land synchronously on first read, but the channel
    /// seeds are resolved over the network in the background (see <see cref="EnsureSeedChannelsAsync"/>),
    /// which can partially fail and must be retried on a later launch.</summary>
    private const string ChannelsSeededKey = "feed_channels_seeded_v1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The finance searches a fresh install follows, so day one isn't an empty page. Searches
    /// need no channel id and can't half-fail, so they're the reliable backbone of the seed —
    /// written synchronously on the first read, before any network is available.
    /// <para>Seeds are ordinary rows — unfollowing one sticks, and re-seeding never happens.</para>
    /// </summary>
    private static readonly string[] SeedSearches =
    [
        "personal finance",
        "budgeting tips",
        "how to save money",
        "index fund investing",
        "financial independence",
        "side hustle ideas",
    ];

    /// <summary>
    /// The finance channels a fresh install follows, named by handle. Resolved to their UC… ids
    /// once, in the background, by <see cref="EnsureSeedChannelsAsync"/> — a handle that no longer
    /// resolves is simply dropped rather than poisoning the seed with a dead id.
    /// </summary>
    private static readonly string[] SeedChannelHandles =
    [
        "GrahamStephan",
        "MinorityMindset",
        "marktilbury",
        "humphrey",
        "nischa",
    ];

    private readonly YouTubeCatalog _catalog;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FeedSourcePreferences(YouTubeCatalog catalog) => _catalog = catalog;

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<FollowedChannel>> GetChannelsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return LoadChannels(); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SavedSearch>> GetSearchesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return LoadSearches(); }
        finally { _gate.Release(); }
    }

    // ---------------------------------------------------------------- adding

    public async Task<SourceAddResult> AddAsync(string input, CancellationToken ct = default)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return SourceAddResult.Empty;

        var query = FeedChannelInput.Parse(text);

        // Not a channel reference at all — so it's a search. No network needed, which is also why
        // a typo here is cheap: the search simply returns nothing until it's corrected.
        if (query is null) return await AddSearchAsync(text, ct).ConfigureAwait(false);

        ResolvedChannel? resolved;
        try
        {
            resolved = await _catalog.ResolveAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Deliberately distinct from NotFound: the user can retry this one.
            return new SourceAddResult(SourceAddOutcome.Unreachable);
        }

        if (resolved is null) return new SourceAddResult(SourceAddOutcome.NotFound);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var channels = LoadChannels();

            // Matched on the UC… id, so pasting the handle for a channel added by URL (or a link
            // to one of its shorts) is recognised as the same channel rather than followed twice.
            var existing = channels.FirstOrDefault(c => c.ChannelId == resolved.ChannelId);
            if (existing is not null)
                return new SourceAddResult(SourceAddOutcome.Duplicate, existing.Title);

            channels.Add(new FollowedChannel
            {
                ChannelId = resolved.ChannelId,
                Handle = resolved.Handle,
                Title = string.IsNullOrWhiteSpace(resolved.Title) ? resolved.Handle : resolved.Title,
                IsSeed = false,
                AddedUtc = DateTime.UtcNow,
            });
            SaveChannels(channels);

            return new SourceAddResult(SourceAddOutcome.Added, resolved.Title);
        }
        finally { _gate.Release(); }
    }

    private async Task<SourceAddResult> AddSearchAsync(string query, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = SavedSearch.Normalize(query);
            if (key.Length == 0) return SourceAddResult.Empty;

            var searches = LoadSearches();
            if (searches.Any(s => s.Key == key))
                return new SourceAddResult(SourceAddOutcome.Duplicate, Query: query);

            searches.Add(new SavedSearch
            {
                Query = query,
                IsSeed = false,
                AddedUtc = DateTime.UtcNow,
            });
            SaveSearches(searches);

            return new SourceAddResult(SourceAddOutcome.Added, Query: query);
        }
        finally { _gate.Release(); }
    }

    // ---------------------------------------------------------------- removing

    public async Task RemoveChannelAsync(string channelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var channels = LoadChannels();
            if (channels.RemoveAll(c => c.ChannelId == channelId) == 0) return;
            SaveChannels(channels);
        }
        finally { _gate.Release(); }

        // Its cached listing is dead weight now, and stale if the channel is ever added back.
        _catalog.ForgetChannel(channelId);
    }

    public async Task RemoveSearchAsync(string query, CancellationToken ct = default)
    {
        var key = SavedSearch.Normalize(query);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var searches = LoadSearches();
            if (searches.RemoveAll(s => s.Key == key) == 0) return;
            SaveSearches(searches);
        }
        finally { _gate.Release(); }

        _catalog.ForgetSearch(query);
    }

    // ---------------------------------------------------------------- muting

    public async Task SetChannelEnabledAsync(string channelId, bool enabled, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var channels = LoadChannels();
            var index = channels.FindIndex(c => c.ChannelId == channelId);
            if (index < 0 || channels[index].Enabled == enabled) return;

            channels[index] = channels[index] with { Enabled = enabled };
            SaveChannels(channels);

            // The cached listing is deliberately kept — that's the whole point of muting rather
            // than unfollowing: switching back on costs nothing.
        }
        finally { _gate.Release(); }
    }

    public async Task SetSearchEnabledAsync(string query, bool enabled, CancellationToken ct = default)
    {
        var key = SavedSearch.Normalize(query);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var searches = LoadSearches();
            var index = searches.FindIndex(s => s.Key == key);
            if (index < 0 || searches[index].Enabled == enabled) return;

            searches[index] = searches[index] with { Enabled = enabled };
            SaveSearches(searches);
        }
        finally { _gate.Release(); }
    }

    // ---------------------------------------------------------------- seeding

    public async Task EnsureSeedChannelsAsync(CancellationToken ct = default)
    {
        if (Preferences.Default.Get(ChannelsSeededKey, false)) return;

        // A network failure means "couldn't ask yet", not "this channel is gone" — so the flag is
        // only set when the whole set was reachable. An offline first launch retries next time;
        // a definitive "no such channel" (resolved == null) is skipped for good.
        var hadNetworkFailure = false;

        foreach (var handle in SeedChannelHandles)
        {
            ct.ThrowIfCancellationRequested();

            ResolvedChannel? resolved;
            try
            {
                resolved = await _catalog.ResolveAsync(ChannelQuery.FromHandle(handle), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                hadNetworkFailure = true;
                continue;
            }

            if (resolved is null) continue;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var channels = LoadChannels();
                if (channels.Any(c => c.ChannelId == resolved.ChannelId)) continue;

                channels.Add(new FollowedChannel
                {
                    ChannelId = resolved.ChannelId,
                    Handle = resolved.Handle,
                    Title = string.IsNullOrWhiteSpace(resolved.Title) ? handle : resolved.Title,
                    IsSeed = true,
                    AddedUtc = DateTime.UtcNow,
                });
                SaveChannels(channels);
            }
            finally { _gate.Release(); }
        }

        if (!hadNetworkFailure) Preferences.Default.Set(ChannelsSeededKey, true);
    }

    // ---------------------------------------------------------------- per-short marks

    public async Task<IReadOnlySet<string>> GetSavedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return LoadIds(SavedKey); }
        finally { _gate.Release(); }
    }

    public async Task<bool> ToggleSavedAsync(string videoId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var saved = LoadIds(SavedKey);
            var nowSaved = saved.Add(videoId);
            if (!nowSaved) saved.Remove(videoId);
            SaveIds(SavedKey, saved);
            return nowSaved;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlySet<string>> GetHiddenAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return LoadIds(HiddenKey); }
        finally { _gate.Release(); }
    }

    public async Task HideAsync(string videoId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var hidden = LoadIds(HiddenKey);
            if (!hidden.Add(videoId)) return;
            SaveIds(HiddenKey, hidden);

            // A short can't be both hidden and bookmarked — hiding wins, and silently keeping it
            // in Saved would leave it showing in the Saved filter it was just banished from.
            var saved = LoadIds(SavedKey);
            if (saved.Remove(videoId)) SaveIds(SavedKey, saved);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearHiddenAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { Preferences.Default.Remove(HiddenKey); }
        finally { _gate.Release(); }
    }

    // ---------------------------------------------------------------- storage

    /// <summary>
    /// Parses the channel blob (seeding searches first if this device has never been seeded).
    /// Seeding is guarded by <see cref="SeededKey"/> rather than by "the list is empty", so
    /// unfollowing every seed stays unfollowed instead of being helpfully undone on the next launch.
    /// </summary>
    private List<FollowedChannel> LoadChannels()
    {
        EnsureSeeded();

        return Read<StoredChannel>(ChannelsKey)
            .Where(c => !string.IsNullOrWhiteSpace(c.ChannelId))
            .Select(c => new FollowedChannel
            {
                ChannelId = c.ChannelId!,
                Handle = c.Handle ?? "",
                Title = string.IsNullOrWhiteSpace(c.Title) ? (c.Handle ?? c.ChannelId!) : c.Title!,
                IsSeed = c.IsSeed,
                Enabled = c.Enabled ?? true,
                AddedUtc = c.AddedUtc,
            })
            .DistinctBy(c => c.ChannelId)
            .ToList();
    }

    private List<SavedSearch> LoadSearches()
    {
        EnsureSeeded();

        return Read<StoredSearch>(SearchesKey)
            .Where(s => !string.IsNullOrWhiteSpace(s.Query))
            .Select(s => new SavedSearch
            {
                Query = s.Query!,
                IsSeed = s.IsSeed,
                Enabled = s.Enabled ?? true,
                AddedUtc = s.AddedUtc,
            })
            .DistinctBy(s => s.Key)
            .ToList();
    }

    /// <summary>Writes the search seeds once per install. Cheap to call on every read: after the
    /// first run it's a single boolean lookup. Channels are seeded separately and over the network
    /// (see <see cref="EnsureSeedChannelsAsync"/>).</summary>
    private void EnsureSeeded()
    {
        if (Preferences.Default.Get(SeededKey, false)) return;

        var now = DateTime.UtcNow;

        // Merged rather than overwritten, so a restore that landed sources here before the first
        // read (the blobs exist, the flag doesn't) keeps them.
        var searches = Read<StoredSearch>(SearchesKey);
        foreach (var query in SeedSearches)
        {
            var key = SavedSearch.Normalize(query);
            if (searches.Any(s => SavedSearch.Normalize(s.Query ?? "") == key)) continue;
            searches.Add(new StoredSearch { Query = query, IsSeed = true, AddedUtc = now });
        }

        Preferences.Default.Set(SearchesKey, JsonSerializer.Serialize(searches, Json));
        Preferences.Default.Set(SeededKey, true);
    }

    private static List<T> Read<T>(string key)
    {
        var json = Preferences.Default.Get(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try { return JsonSerializer.Deserialize<List<T>>(json, Json) ?? []; }
        catch (JsonException) { return []; }   // a corrupt blob shouldn't brick the page
    }

    private static HashSet<string> LoadIds(string key) =>
        Read<string>(key).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.Ordinal);

    private static void SaveIds(string key, HashSet<string> ids)
    {
        if (ids.Count == 0) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, JsonSerializer.Serialize(ids.ToList(), Json));
    }

    private static void SaveChannels(List<FollowedChannel> channels) =>
        Preferences.Default.Set(ChannelsKey, JsonSerializer.Serialize(
            channels.Select(c => new StoredChannel
            {
                ChannelId = c.ChannelId, Handle = c.Handle, Title = c.Title,
                IsSeed = c.IsSeed, Enabled = c.Enabled, AddedUtc = c.AddedUtc,
            }).ToList(), Json));

    private static void SaveSearches(List<SavedSearch> searches) =>
        Preferences.Default.Set(SearchesKey, JsonSerializer.Serialize(
            searches.Select(s => new StoredSearch
            {
                Query = s.Query, IsSeed = s.IsSeed, Enabled = s.Enabled, AddedUtc = s.AddedUtc,
            }).ToList(), Json));

    /// <summary>Plain mutable shapes for the JSON blob, so the persisted form is decoupled from
    /// the domain records (which use <c>required</c> / init-only members).</summary>
    private sealed class StoredChannel
    {
        public string? ChannelId { get; set; }
        public string? Handle { get; set; }
        public string? Title { get; set; }
        public bool IsSeed { get; set; }

        /// <summary>
        /// Nullable on purpose. Blobs written before the enable/disable switch existed have no
        /// such field, and a plain <c>bool</c> would deserialize those to <c>false</c> — silently
        /// muting every source the user already had. Null reads as enabled.
        /// </summary>
        public bool? Enabled { get; set; }

        public DateTime AddedUtc { get; set; }
    }

    private sealed class StoredSearch
    {
        public string? Query { get; set; }
        public bool IsSeed { get; set; }

        /// <summary>Null reads as enabled — see <see cref="StoredChannel.Enabled"/>.</summary>
        public bool? Enabled { get; set; }

        public DateTime AddedUtc { get; set; }
    }
}
