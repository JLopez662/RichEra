using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using RichEra.Application.Abstractions;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Maui;

// Re-arm the daily reminder after a device reboot. Android clears scheduled alarms on reboot;
// this fires on BOOT_COMPLETED, starts the app process headless, resolves its services, and
// reschedules — a backstop on top of Plugin.LocalNotification's own restore.
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;

        // Keep the receiver alive while we reschedule off the main thread.
        var pending = GoAsync();
        _ = ReArmAsync(pending);
    }

    private static async Task ReArmAsync(PendingResult? pending)
    {
        try
        {
            var services = Microsoft.Maui.IPlatformApplication.Current?.Services;
            if (services is not null)
            {
                await services.GetRequiredService<DatabaseInitializer>().EnsureInitializedAsync();
                await services.GetRequiredService<INotificationScheduler>().RescheduleAsync();
            }
        }
        catch { /* best-effort */ }
        finally { pending?.Finish(); }
    }
}
