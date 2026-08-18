namespace RichEra.Domain.Entities;

// One entry in the in-app Notification Center. Every notification the app schedules is
// mirrored here in the same write, so the phone and the Center always show the same items
// (the Ascending Tracker model — a single write path, not two independent systems).
// Rows may be written with a FUTURE FireTimeUtc: the Center only surfaces them once that
// time has passed, and seeds IsRead from whether the fire time was already in the past.
public class NotificationLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime FireTimeUtc { get; set; }          // when it fires / fired (UTC); "delivered" = past
    public bool IsRead { get; set; }

    // Idempotency key so re-scheduling the same notification upserts instead of duplicating.
    // Shape: "{(byte)kind}|{fireMinuteUtc:yyyyMMddHHmm}|{title}". Unique per row.
    public string DedupeKey { get; set; } = string.Empty;

    // The OS notification id (Android) when this row mirrors a scheduled push — lets an early
    // cancel pull the still-pending row back out. Null for rows with no OS counterpart.
    public int? PlatformNotificationId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

// Notification categories. DailyLogReminder ships first (the "track your transactions" nudge);
// the rest are reserved so the scheduler, Center, and settings already understand them when the
// later kinds land. Stored as a byte. System is app-authored notices (e.g. the welcome note).
public enum NotificationKind : byte
{
    DailyLogReminder = 0,
    BillDue = 1,
    BudgetAlert = 2,
    SavingsMilestone = 3,
    WeighInReminder = 4,
    System = 5,
}
