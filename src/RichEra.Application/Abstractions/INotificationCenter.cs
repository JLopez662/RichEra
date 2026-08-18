using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

/// <summary>
/// The in-app Notification Center store. Both the phone notification and the in-app entry come
/// from a single call to <see cref="RecordManyAsync"/>, so the two never disagree. Rows may be
/// recorded with a future fire time; the Center only surfaces and counts them once it passes.
/// </summary>
public interface INotificationCenter
{
    /// <summary>
    /// Idempotently record notifications. Rows whose DedupeKey already exists are skipped (their
    /// read state is preserved); new rows are added unread. Entries older than <paramref name="keepDays"/>
    /// are pruned. Returns the rows actually added — so callers can push only the new ones to the OS.
    /// </summary>
    Task<IReadOnlyList<NotificationItemDto>> RecordManyAsync(IReadOnlyList<NotificationInput> items, int keepDays = 30, CancellationToken ct = default);

    /// <summary>Count of delivered-and-unread entries (FireTimeUtc &lt;= now &amp;&amp; !IsRead) — the bell badge.</summary>
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);

    /// <summary>Delivered entries inside the trailing window, newest first — the Center list.</summary>
    Task<IReadOnlyList<NotificationItemDto>> GetDeliveredAsync(int keepDays = 30, CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);

    /// <summary>Remove a still-pending row by its OS notification id (used when a scheduled push is cancelled).</summary>
    Task RemoveByPlatformIdAsync(int platformNotificationId, CancellationToken ct = default);
}
