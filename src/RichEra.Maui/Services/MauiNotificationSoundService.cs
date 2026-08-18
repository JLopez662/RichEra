using RichEra.Application.Abstractions;

#if ANDROID
using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Provider;
#endif

namespace RichEra.Maui.Services;

// Platform implementation of INotificationSoundService. On Android 10+ it copies a user-picked audio
// file into MediaStore (the one place the system notification service can reliably read from) and
// creates a notification channel whose sound is that file. Because a channel's sound is immutable
// once created, each change mints a new channel id (ReminderPreferences.SoundChannelVersion) and the
// stale channels are deleted. A no-op on Windows and on Android < 10, where IsSupported is false.
// Singleton, mirroring MauiNotificationScheduler.
public sealed class MauiNotificationSoundService : INotificationSoundService
{
    private const string ChannelName = "Reminders";
    private const string ChannelDescription = "Daily reminder to log your transactions.";

    public bool IsSupported =>
#if ANDROID
        OperatingSystem.IsAndroidVersionAtLeast(29);
#else
        false;
#endif

    public async Task<string?> PickAsync(CancellationToken ct = default)
    {
#if ANDROID
        // Direct version check (not the IsSupported property) so the platform analyzer treats the
        // MediaStore/channel calls below as reachable only on Android 10+.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;

        var audioType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = new[] { "audio/*" },
        });

        FileResult? picked;
        try
        {
            picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick a notification sound",
                FileTypes = audioType,
            });
        }
        catch { return null; }   // picker unavailable / cancelled with an error
        if (picked is null) return null;   // user cancelled

        Android.Net.Uri? mediaUri = null;
        try
        {
            mediaUri = await InstallToMediaStoreAsync(picked, ct);
            if (mediaUri is null) return null;

            // Mint a fresh channel id (a channel's sound is immutable) and CREATE the channel before
            // committing the preferences — so if creation fails the app never points at a channel
            // that doesn't exist. Only after the channel is live do we record the selection.
            var oldUri = ReminderPreferences.CustomSoundUri;
            var newVersion = ReminderPreferences.SoundChannelVersion + 1;
            var newChannelId = $"{MauiNotificationScheduler.ChannelId}.v{newVersion}";
            CreateSoundChannel(newChannelId, mediaUri);

            ReminderPreferences.SoundChannelVersion = newVersion;
            ReminderPreferences.CustomSoundUri = mediaUri.ToString() ?? "";
            ReminderPreferences.CustomSoundName = picked.FileName;

            DeleteStaleChannels(newChannelId);
            // Drop the previously-selected sound file so old audio doesn't accumulate in Notifications.
            if (!string.IsNullOrEmpty(oldUri))
            {
                try { Resolver.Delete(Android.Net.Uri.Parse(oldUri)!, null, null); } catch { }
            }
            return ReminderPreferences.CustomSoundName;
        }
        catch
        {
            // Creation failed before prefs were committed → just remove the orphaned media entry.
            if (mediaUri is not null) { try { Resolver.Delete(mediaUri, null, null); } catch { } }
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return;

        var oldUri = ReminderPreferences.CustomSoundUri;

        // Clear the selection first so ActiveChannelId reverts to the base channel; then delete every
        // versioned channel (all now != ActiveChannelId) and the MediaStore entry.
        ReminderPreferences.CustomSoundUri = "";
        ReminderPreferences.CustomSoundName = "";

        DeleteStaleChannels(ReminderPreferences.ActiveChannelId);
        if (!string.IsNullOrEmpty(oldUri))
        {
            try { Resolver.Delete(Android.Net.Uri.Parse(oldUri)!, null, null); } catch { }
        }
#endif
        await Task.CompletedTask;
    }

    public async Task EnsureActiveChannelAsync(CancellationToken ct = default)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return;
        var uri = ReminderPreferences.CustomSoundUri;
        if (string.IsNullOrEmpty(uri)) return;   // no custom sound → base channel handles it

        try
        {
            var id = ReminderPreferences.ActiveChannelId;
            if (Manager.GetNotificationChannel(id) is null)
            {
                CreateSoundChannel(id, Android.Net.Uri.Parse(uri)!);
                DeleteStaleChannels(id);
            }
        }
        catch { /* best-effort; the base channel still delivers */ }
#endif
        await Task.CompletedTask;
    }

#if ANDROID
    private static Context Context => Android.App.Application.Context;
    private static ContentResolver Resolver => Context.ContentResolver!;
    private static NotificationManager Manager =>
        (NotificationManager)Context.GetSystemService(Context.NotificationService)!;

    // Copy the picked file into MediaStore under Notifications with IS_NOTIFICATION=1, returning a
    // content:// URI the system notification service can read. Scoped-storage insert (API 29+).
    [SupportedOSPlatform("android29.0")]
    private static async Task<Android.Net.Uri?> InstallToMediaStoreAsync(FileResult picked, CancellationToken ct)
    {
        var values = new ContentValues();
        values.Put("_display_name", picked.FileName);
        values.Put("mime_type", ResolveMime(picked));
        values.Put("relative_path", Android.OS.Environment.DirectoryNotifications);
        values.Put("is_notification", 1);
        values.Put("is_pending", 1);   // hide until the bytes are written

        var uri = Resolver.Insert(MediaStore.Audio.Media.ExternalContentUri!, values);
        if (uri is null) return null;

        await using var input = await picked.OpenReadAsync();
        await using (var output = Resolver.OpenOutputStream(uri))
        {
            if (output is null) { Resolver.Delete(uri, null, null); return null; }
            await input.CopyToAsync(output, ct);
        }

        values.Clear();
        values.Put("is_pending", 0);
        Resolver.Update(uri, values, null, null);
        return uri;
    }

    [SupportedOSPlatform("android26.0")]
    private static void CreateSoundChannel(string channelId, Android.Net.Uri soundUri)
    {
        var attrs = new AudioAttributes.Builder()!
            .SetUsage(AudioUsageKind.Notification)!
            .SetContentType(AudioContentType.Sonification)!
            .Build();
        var channel = new NotificationChannel(channelId, ChannelName, NotificationImportance.High)
        {
            Description = ChannelDescription,
        };
        channel.SetSound(soundUri, attrs);
        Manager.CreateNotificationChannel(channel);
    }

    // Remove every versioned reminder channel except the active one. The base channel
    // ("richera.reminders", no trailing dot) is never matched, so the default-sound path survives.
    [SupportedOSPlatform("android26.0")]
    private static void DeleteStaleChannels(string activeId)
    {
        var prefix = MauiNotificationScheduler.ChannelId + ".";
        var stale = new List<string>();
        foreach (var ch in Manager.NotificationChannels ?? Array.Empty<NotificationChannel>())
        {
            var id = ch.Id;
            if (id is not null && id.StartsWith(prefix, StringComparison.Ordinal) && id != activeId)
                stale.Add(id);
        }
        foreach (var id in stale)
            Manager.DeleteNotificationChannel(id);
    }

    private static string ResolveMime(FileResult picked)
    {
        if (!string.IsNullOrEmpty(picked.ContentType) && picked.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return picked.ContentType;
        var ext = System.IO.Path.GetExtension(picked.FileName)?.ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".ogg" or ".oga" => "audio/ogg",
            ".wav" => "audio/wav",
            ".m4a" or ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            _ => "audio/mpeg",
        };
    }
#endif
}
