using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

// SQLite-backed Notification Center. Mirrors Ascending Tracker's log service: dedupe by key,
// seed read-state from whether the fire time already passed, and only surface/count rows whose
// time has arrived — which is what lets the Center stay correct with no while-closed callback.
public class NotificationCenter : INotificationCenter
{
    private readonly AppDbContext _db;
    public NotificationCenter(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationItemDto>> RecordManyAsync(IReadOnlyList<NotificationInput> items, int keepDays = 30, CancellationToken ct = default)
    {
        if (items.Count == 0) return Array.Empty<NotificationItemDto>();
        var now = DateTime.UtcNow;

        // De-dupe within the incoming batch by key, then skip keys already stored (preserving the
        // prior read state of anything we've seen before).
        var incoming = items.GroupBy(KeyOf).Select(g => g.First()).ToList();
        var keys = incoming.Select(KeyOf).ToList();

        var have = (await _db.NotificationLogEntries
                .Where(e => keys.Contains(e.DedupeKey))
                .Select(e => e.DedupeKey)
                .ToListAsync(ct))
            .ToHashSet();

        var added = new List<NotificationLogEntry>();
        foreach (var i in incoming)
        {
            var key = KeyOf(i);
            if (have.Contains(key)) continue;
            var entry = new NotificationLogEntry
            {
                Kind = i.Kind,
                Title = i.Title,
                Body = i.Body,
                FireTimeUtc = i.FireTimeUtc,
                IsRead = false,   // new rows are unread; they light the bell once their time passes
                DedupeKey = key,
                PlatformNotificationId = i.PlatformNotificationId,
            };
            _db.NotificationLogEntries.Add(entry);
            added.Add(entry);
        }

        // Prune anything older than the retention window.
        var cutoff = now.AddDays(-Math.Max(1, keepDays));
        var stale = await _db.NotificationLogEntries.Where(e => e.FireTimeUtc < cutoff).ToListAsync(ct);
        if (stale.Count > 0) _db.NotificationLogEntries.RemoveRange(stale);

        if (added.Count > 0 || stale.Count > 0) await _db.SaveChangesAsync(ct);

        return added
            .Select(e => new NotificationItemDto(
                e.Id, e.Kind, e.Title, e.Body,
                DateTime.SpecifyKind(e.FireTimeUtc, DateTimeKind.Utc), e.IsRead))
            .ToList();
    }

    public Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.NotificationLogEntries.CountAsync(e => !e.IsRead && e.FireTimeUtc <= now, ct);
    }

    public async Task<IReadOnlyList<NotificationItemDto>> GetDeliveredAsync(int keepDays = 30, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var from = now.AddDays(-Math.Max(1, keepDays));
        var rows = await _db.NotificationLogEntries.AsNoTracking()
            .Where(e => e.FireTimeUtc <= now && e.FireTimeUtc >= from)
            .OrderByDescending(e => e.FireTimeUtc)
            .Select(e => new NotificationItemDto(e.Id, e.Kind, e.Title, e.Body, e.FireTimeUtc, e.IsRead))
            .ToListAsync(ct);

        // SQLite hands back DateTimeKind.Unspecified — stamp UTC so ToLocalTime() is correct in the UI.
        return rows.Select(r => r with { FireTimeUtc = DateTime.SpecifyKind(r.FireTimeUtc, DateTimeKind.Utc) }).ToList();
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.NotificationLogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null || row.IsRead) return;
        row.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var unread = await _db.NotificationLogEntries.Where(e => !e.IsRead && e.FireTimeUtc <= now).ToListAsync(ct);
        if (unread.Count == 0) return;
        foreach (var e in unread) e.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveByPlatformIdAsync(int platformNotificationId, CancellationToken ct = default)
    {
        var rows = await _db.NotificationLogEntries
            .Where(e => e.PlatformNotificationId == platformNotificationId).ToListAsync(ct);
        if (rows.Count == 0) return;
        _db.NotificationLogEntries.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    // Explicit key for event alerts (stable across evaluations); else derived from kind|minute|title.
    private static string KeyOf(NotificationInput i)
        => i.DedupeKey ?? $"{(byte)i.Kind}|{i.FireTimeUtc:yyyyMMddHHmm}|{i.Title}";
}
