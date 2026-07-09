using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// Whole-database backup and restore via a single JSON document.
public interface IDataPortabilityService
{
    // Serializes every account, category, income source and transaction into one JSON string.
    Task<string> ExportAsJsonAsync(CancellationToken ct = default);

    // Replaces ALL existing data with the contents of the backup, inside a single transaction —
    // on any error the database is rolled back to its previous state.
    Task<DataImportResult> ImportFromJsonAsync(string json, CancellationToken ct = default);
}
