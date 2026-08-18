namespace RichEra.Application.Abstractions;

/// <summary>
/// Manages the custom sound that the device's notifications play. The user picks an audio file; it
/// is installed somewhere the OS notification service can read and a dedicated channel is created to
/// play it. Implemented in the MAUI head against the platform notification API; a no-op where there
/// is no such API (the Windows dev loop) or the OS is too old. Same "platform port" shape as
/// <see cref="INotificationScheduler"/> and <see cref="IReceiptTextRecognizer"/>.
/// </summary>
public interface INotificationSoundService
{
    /// <summary>Whether custom sounds can be set on this platform/OS version (Android 10+ only).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Prompt the user to pick an audio file, install it, and switch notifications to play it.
    /// Returns the chosen sound's display name, or null if the user cancelled or it couldn't be set.
    /// </summary>
    Task<string?> PickAsync(CancellationToken ct = default);

    /// <summary>Revert to the built-in default notification sound.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensure the channel for the currently-selected custom sound exists (recreate it if the OS lost
    /// it, e.g. after data clear). Idempotent; a no-op when no custom sound is set. Called by the
    /// scheduler before it posts, so every schedule path lands on a live channel.
    /// </summary>
    Task EnsureActiveChannelAsync(CancellationToken ct = default);
}
