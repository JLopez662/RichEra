using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RichEra.Maui.Services;

// EF Core interceptor that pings AutoBackupService after every successful SaveChangesAsync.
// The service debounces, so notifying on every save is cheap. Registered as an
// ISaveChangesInterceptor in DI; the DbContext picks it up via AddRichEraInfrastructure.
internal sealed class BackupAutoSaveInterceptor : SaveChangesInterceptor
{
    private readonly AutoBackupService _autoBackup;

    public BackupAutoSaveInterceptor(AutoBackupService autoBackup) => _autoBackup = autoBackup;

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _autoBackup.NotifyChange();
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        _autoBackup.NotifyChange();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
