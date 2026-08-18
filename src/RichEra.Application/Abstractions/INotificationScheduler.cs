namespace RichEra.Application.Abstractions;

/// <summary>
/// Schedules (and cancels) the device's local notifications and mirrors each one into the
/// <see cref="INotificationCenter"/>. Implemented in the MAUI head against the platform
/// notification API; a no-op on platforms without one (e.g. the Windows dev loop). Same
/// "platform port" shape as IReceiptTextRecognizer.
/// </summary>
public interface INotificationScheduler
{
    /// <summary>Ensure OS notification permission (Android 13+ prompts once). Returns whether granted.</summary>
    Task<bool> EnsurePermissionAsync(CancellationToken ct = default);

    /// <summary>
    /// Rebuild the scheduled notifications from the user's reminder preferences and mirror the
    /// current day's entry into the Center. Idempotent — safe to run on startup and on every resume.
    /// </summary>
    Task RescheduleAsync(CancellationToken ct = default);

    /// <summary>Cancel the daily transaction reminder (when it's switched off, or — from Phase 4 —
    /// once the day's transaction has been logged and smart-skip is on).</summary>
    Task CancelDailyReminderAsync(CancellationToken ct = default);
}
