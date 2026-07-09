using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using RichEra.Application.Abstractions;

namespace RichEra.Maui.Services;

// Background auto-save: every successful SaveChangesAsync on the DbContext schedules a
// debounced (5s) write of the full JSON export to a fixed on-device location. A burst of
// edits collapses into a single file write. Enable state is mirrored to a preference so it
// survives restarts (default ON). Registered as a singleton; the timer lives for the app's
// lifetime. The actual file write happens off the timer thread via a fresh DI scope, so it
// never shares the UI's DbContext.
public sealed class AutoBackupService : IDisposable
{
    private const string EnabledKey = "auto_backup_enabled";
    public const string BackupFileName = "richera-autobackup.json";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly System.Threading.Timer _debounceTimer;
    private readonly object _gate = new();
    private bool _disposed;

    public AutoBackupService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _debounceTimer = new System.Threading.Timer(OnDebounceFired, state: null,
            dueTime: System.Threading.Timeout.InfiniteTimeSpan,
            period: System.Threading.Timeout.InfiniteTimeSpan);

        try { IsEnabled = Preferences.Default.Get(EnabledKey, true); }
        catch { IsEnabled = true; }
    }

    // True while a backup is being written — the toggle shows a spinner so the silent
    // disk write is still visible to the user.
    public bool IsRunning { get; private set; }

    // True when changes schedule a backup. Setter persists to preferences.
    public bool IsEnabled { get; private set; }

    // UTC instant the most recent auto-save finished, or null if none yet this session.
    public DateTime? LastAutoBackupUtc { get; private set; }

    // Message from the most recent failed auto-save, or null when the last attempt succeeded.
    public string? LastAutoBackupError { get; private set; }

    // Absolute path the auto-backup file is written to.
    public string BackupFilePath => ResolveBackupPath();

    // Fired after each auto-save attempt (success or failure) so the UI can refresh.
    public event Action? StateChanged;

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        try { Preferences.Default.Set(EnabledKey, enabled); }
        catch { /* preferences unavailable — keep the in-memory flag */ }

        if (!enabled)
        {
            // Cancel any queued write so a flip-off is immediate.
            _debounceTimer.Change(System.Threading.Timeout.InfiniteTimeSpan,
                                  System.Threading.Timeout.InfiniteTimeSpan);
        }
        StateChanged?.Invoke();
    }

    // Schedule a backup 5s from now; another change in that window resets the timer.
    public void NotifyChange()
    {
        if (!IsEnabled || _disposed) return;
        try { _debounceTimer.Change(DebounceDelay, System.Threading.Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* torn down during shutdown — ignore */ }
    }

    private void OnDebounceFired(object? state)
    {
        if (_disposed) return;
        _ = RunBackupAsync();
    }

    private async Task RunBackupAsync()
    {
        // Drop this tick if a write is already in flight; the next NotifyChange reschedules.
        lock (_gate)
        {
            if (IsRunning) return;
            IsRunning = true;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var portability = scope.ServiceProvider.GetRequiredService<IDataPortabilityService>();
            var json = await portability.ExportAsJsonAsync();

            var path = ResolveBackupPath();
            await File.WriteAllTextAsync(path, json);

            LastAutoBackupUtc = DateTime.UtcNow;
            LastAutoBackupError = null;
        }
        catch (Exception ex)
        {
            LastAutoBackupError = ex.Message;
        }
        finally
        {
            lock (_gate) IsRunning = false;
            try { StateChanged?.Invoke(); } catch { /* listener threw — don't crash the timer */ }
        }
    }

    private static string ResolveBackupPath()
    {
        string dir;
#if ANDROID
        // App-private external dir (Android/data/<pkg>/files) — reachable via a file manager
        // or adb without run-as, unlike the internal AppDataDirectory.
        dir = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
              ?? FileSystem.AppDataDirectory;
#else
        dir = FileSystem.AppDataDirectory;
#endif
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, BackupFileName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
    }
}
