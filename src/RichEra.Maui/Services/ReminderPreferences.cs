using Microsoft.Maui.Storage;

namespace RichEra.Maui.Services;

// Device-local reminder settings via MAUI Preferences — the same lightweight-flag pattern as
// AutoBackupService. No DB row, so these stay out of the JSON backup and are readable on any
// thread. The /settings UI (Phase 4) reads and writes these; the scheduler reads them.
public static class ReminderPreferences
{
    private const string EnabledKey = "reminder_daily_enabled_v1";
    private const string TimeKey    = "reminder_daily_minutes_v1";   // minutes since local midnight
    private const string SmartKey   = "reminder_daily_smart_v1";

    private const int DefaultMinutes = 20 * 60;   // 8:00 PM

    /// <summary>Whether the daily "log your spending" reminder is on. Default on.</summary>
    public static bool Enabled
    {
        get { try { return Preferences.Default.Get(EnabledKey, true); } catch { return true; } }
        set { try { Preferences.Default.Set(EnabledKey, value); } catch { /* keep default */ } }
    }

    /// <summary>Local time-of-day the reminder fires. Default 8:00 PM.</summary>
    public static TimeOnly Time
    {
        get
        {
            int m;
            try { m = Preferences.Default.Get(TimeKey, DefaultMinutes); } catch { m = DefaultMinutes; }
            m = Math.Clamp(m, 0, 24 * 60 - 1);
            return new TimeOnly(m / 60, m % 60);
        }
        set { try { Preferences.Default.Set(TimeKey, value.Hour * 60 + value.Minute); } catch { /* keep default */ } }
    }

    /// <summary>Skip days a transaction was already logged. Wired up in Phase 4. Default on.</summary>
    public static bool SmartSkip
    {
        get { try { return Preferences.Default.Get(SmartKey, true); } catch { return true; } }
        set { try { Preferences.Default.Set(SmartKey, value); } catch { /* keep default */ } }
    }

    // Secondary alert kinds (Phase 5). Each defaults on; toggled from the Settings page.
    public static bool BillsDueEnabled
    {
        get { try { return Preferences.Default.Get("reminder_bills_v1", true); } catch { return true; } }
        set { try { Preferences.Default.Set("reminder_bills_v1", value); } catch { /* keep default */ } }
    }

    public static bool BudgetAlertsEnabled
    {
        get { try { return Preferences.Default.Get("reminder_budget_v1", true); } catch { return true; } }
        set { try { Preferences.Default.Set("reminder_budget_v1", value); } catch { /* keep default */ } }
    }

    public static bool SavingsMilestonesEnabled
    {
        get { try { return Preferences.Default.Get("reminder_savings_v1", true); } catch { return true; } }
        set { try { Preferences.Default.Set("reminder_savings_v1", value); } catch { /* keep default */ } }
    }

    public static bool WeighInEnabled
    {
        get { try { return Preferences.Default.Get("reminder_weighin_v1", true); } catch { return true; } }
        set { try { Preferences.Default.Set("reminder_weighin_v1", value); } catch { /* keep default */ } }
    }

    // Custom notification sound (Android 10+). The picked audio is copied into MediaStore and its
    // content:// URI stored here; MauiNotificationSoundService creates a channel that plays it. A
    // channel's sound is immutable once created, so each change bumps SoundChannelVersion to mint a
    // fresh channel id (see ActiveChannelId). Empty URI = the built-in default sound.

    /// <summary>MediaStore content URI of the user's chosen sound, or "" for the default sound.</summary>
    public static string CustomSoundUri
    {
        get { try { return Preferences.Default.Get("reminder_sound_uri_v1", ""); } catch { return ""; } }
        set { try { Preferences.Default.Set("reminder_sound_uri_v1", value ?? ""); } catch { /* keep default */ } }
    }

    /// <summary>Display name of the chosen sound (e.g. "coins.mp3"), for the Settings UI.</summary>
    public static string CustomSoundName
    {
        get { try { return Preferences.Default.Get("reminder_sound_name_v1", ""); } catch { return ""; } }
        set { try { Preferences.Default.Set("reminder_sound_name_v1", value ?? ""); } catch { /* keep default */ } }
    }

    /// <summary>Bumped on every sound change so a new (immutable-sound) channel id is minted.</summary>
    public static int SoundChannelVersion
    {
        get { try { return Preferences.Default.Get("reminder_sound_ver_v1", 0); } catch { return 0; } }
        set { try { Preferences.Default.Set("reminder_sound_ver_v1", value); } catch { /* keep default */ } }
    }

    /// <summary>
    /// The notification channel the scheduler should post on: the base channel (default sound) when
    /// no custom sound is set, else a per-version channel carrying the chosen sound.
    /// </summary>
    public static string ActiveChannelId =>
        string.IsNullOrEmpty(CustomSoundUri)
            ? MauiNotificationScheduler.ChannelId
            : $"{MauiNotificationScheduler.ChannelId}.v{SoundChannelVersion}";
}
