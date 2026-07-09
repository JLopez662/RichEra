using Microsoft.Extensions.DependencyInjection;

namespace RichEra.Infrastructure.Persistence;

/// <summary>
/// Runs EF Core migration + seeding exactly once, off the app-startup critical path.
/// <para>
/// <see cref="EnsureInitializedAsync"/> starts the work on the thread pool on first call
/// and caches the resulting task, so every caller awaits the same single run. MauiProgram
/// kicks it off in the background (so the window/WebView/loading screen appear immediately),
/// and the Blazor UI awaits the same task before touching data — guaranteeing nothing queries
/// the database before it has been migrated and seeded.
/// </para>
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private volatile Task? _initialization;

    public DatabaseInitializer(IServiceProvider services) => _services = services;

    /// <summary>
    /// Returns the shared initialization task, starting it on the thread pool on the first
    /// call. Safe to call from any thread; migration runs only once and all callers await it.
    /// </summary>
    public Task EnsureInitializedAsync()
    {
        if (_initialization is { } existing) return existing;
        lock (_gate)
        {
            return _initialization ??= Task.Run(InitializeAsync);
        }
    }

    private async Task InitializeAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DbInitializer.MigrateAndSeedAsync(db).ConfigureAwait(false);
    }
}
