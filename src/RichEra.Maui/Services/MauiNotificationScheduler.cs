using Microsoft.Extensions.DependencyInjection;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

#if ANDROID
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;
#endif

namespace RichEra.Maui.Services;

// Platform implementation of INotificationScheduler. Schedules the daily "log your spending"
// reminder on Android (Plugin.LocalNotification) and mirrors it into the Notification Center in the
// same pass. A no-op notifier on other targets (the Windows dev loop) — the Center mirror still
// runs so the in-app side stays exercisable. Singleton, so scoped services are resolved per call.
//
// Smart-skip: when enabled and a manual transaction is already logged today, today's reminder is
// skipped — the OS fire is anchored to tomorrow and no Center row is written for today.
public sealed class MauiNotificationScheduler : INotificationScheduler
{
    // Android notification channel for reminders (registered in MauiProgram).
    public const string ChannelId = "richera.reminders";

    // Fixed id so a reschedule REPLACES the daily reminder rather than stacking duplicates.
    private const int DailyReminderId = 1001;

    private const string ReminderTitle = "Track today's spending";
    private const string ReminderBody =
        "Take a minute to log what you spent and keep your budget honest.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationSoundService _sound;
    public MauiNotificationScheduler(IServiceScopeFactory scopeFactory, INotificationSoundService sound)
    {
        _scopeFactory = scopeFactory;
        _sound = sound;
    }

    public Task<bool> EnsurePermissionAsync(CancellationToken ct = default)
    {
#if ANDROID
        return RequestPermissionAsync();
#else
        return Task.FromResult(false);
#endif
    }

    public async Task RescheduleAsync(CancellationToken ct = default)
    {
        // Make sure the channel carrying the user's custom sound exists before we post on it.
        await _sound.EnsureActiveChannelAsync(ct);
        await ScheduleDailyReminderAsync(ct);
        await EvaluateAlertsAsync(ct);
    }

    private async Task ScheduleDailyReminderAsync(CancellationToken ct)
    {
        if (!ReminderPreferences.Enabled)
        {
            await CancelDailyAsync();
            return;
        }

        var now = DateTime.Now;
        var time = ReminderPreferences.Time;
        var todayAt = new DateTime(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, DateTimeKind.Local);

        // Smart-skip: only when the toggle is on AND today already has a manual log.
        var loggedToday = ReminderPreferences.SmartSkip && await HasLoggedTodayAsync(ct);
        var remindToday = !loggedToday;

        // Daily-repeating OS notification. Anchor to today if today's slot is still ahead and not
        // skipped, else tomorrow; the repeat then fires every day at the chosen time. Each
        // reschedule (resume / save / boot) re-anchors it, so smart-skip stays honest day to day.
        var anchorLocal = (remindToday && todayAt > now) ? todayAt : todayAt.AddDays(1);
        await ScheduleDailyAsync(anchorLocal);

        // Mirror ONLY the reminder that has already fired today (history). Future fires are mirrored
        // on the next reschedule once their time passes; skipped days never fire, so never mirror.
        if (remindToday && todayAt <= now)
            await MirrorDailyAsync(todayAt.ToUniversalTime(), ct);
    }

    public Task CancelDailyReminderAsync(CancellationToken ct = default) => CancelDailyAsync();

    // Evaluate the secondary alerts (bills / budget / savings / weekly check-in), record any new
    // ones into the Center, and push the newly-added ones to the OS.
    private async Task EvaluateAlertsAsync(CancellationToken ct)
    {
        var ctx = new NotificationRuleContext(
            DateOnly.FromDateTime(DateTime.Today),
            ReminderPreferences.BillsDueEnabled,
            ReminderPreferences.BudgetAlertsEnabled,
            ReminderPreferences.SavingsMilestonesEnabled,
            ReminderPreferences.WeighInEnabled);
        if (!ctx.BillsDue && !ctx.BudgetAlerts && !ctx.SavingsMilestones && !ctx.WeighIn) return;

        IReadOnlyList<NotificationItemDto> added = Array.Empty<NotificationItemDto>();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<INotificationRuleEvaluator>();
            var center = scope.ServiceProvider.GetRequiredService<INotificationCenter>();
            var due = await evaluator.EvaluateAsync(ctx, ct);
            if (due.Count == 0) return;
            added = await center.RecordManyAsync(due, ct: ct);
        }
        catch { return; }

        await ShowAlertsAsync(added);
    }

    private async Task ShowAlertsAsync(IReadOnlyList<NotificationItemDto> added)
    {
#if ANDROID
        foreach (var a in added)
        {
            try
            {
                await LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = AlertId(a),
                    Title = a.Title,
                    Description = a.Body,
                    Android = new AndroidOptions
                    {
                        ChannelId = ReminderPreferences.ActiveChannelId,
                        VisibilityType = AndroidVisibilityType.Public,
                        Priority = AndroidPriority.High,
                    },
                });   // no Schedule → fires immediately
            }
            catch { /* best-effort */ }
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    // Per-alert id kept clear of the daily reminder's fixed id. Alerts are one-shot, so an
    // occasional hash collision would only replace an existing one, never duplicate.
    private static int AlertId(NotificationItemDto a) => 2000 + (int)((uint)a.Id.GetHashCode() % 900_000u);
#endif

    // "Did I already track something today?" — a manual (non-transfer, non-import) log dated today.
    private async Task<bool> HasLoggedTodayAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var txns = scope.ServiceProvider.GetRequiredService<ITransactionService>();
            return await txns.HasManualLogOnAsync(DateOnly.FromDateTime(DateTime.Today), ct);
        }
        catch { return false; }   // on error, don't skip — better to remind than to go silent
    }

    // Mirror the reminder into the Center in a fresh DI scope. The dedupe key keeps repeated
    // reschedules from duplicating the row.
    private async Task MirrorDailyAsync(DateTime fireUtc, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var center = scope.ServiceProvider.GetRequiredService<INotificationCenter>();
            await center.RecordManyAsync(new[]
            {
                new NotificationInput(NotificationKind.DailyLogReminder, ReminderTitle, ReminderBody, fireUtc),
            }, ct: ct);
        }
        catch { /* mirror is best-effort */ }
    }

    private async Task ScheduleDailyAsync(DateTime anchorLocal)
    {
#if ANDROID
        try
        {
            await LocalNotificationCenter.Current.RequestNotificationPermission();
            LocalNotificationCenter.Current.Cancel(DailyReminderId);   // replace any existing schedule
            var request = new NotificationRequest
            {
                NotificationId = DailyReminderId,
                Title = ReminderTitle,
                Description = ReminderBody,
                BadgeNumber = 1,
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = anchorLocal,
                    RepeatType = NotificationRepeat.Daily,
                },
                Android = new AndroidOptions
                {
                    ChannelId = ReminderPreferences.ActiveChannelId,
                    VisibilityType = AndroidVisibilityType.Public,
                    Priority = AndroidPriority.High,
                },
            };
            await LocalNotificationCenter.Current.Show(request);
        }
        catch { /* scheduling is best-effort; the in-app mirror still records */ }
#else
        await Task.CompletedTask;
#endif
    }

    private async Task CancelDailyAsync()
    {
#if ANDROID
        try { LocalNotificationCenter.Current.Cancel(DailyReminderId); } catch { /* ignore */ }
#endif
        await Task.CompletedTask;
    }

#if ANDROID
    private static async Task<bool> RequestPermissionAsync()
    {
        try { return await LocalNotificationCenter.Current.RequestNotificationPermission(); }
        catch { return false; }
    }
#endif
}
