namespace RichEra.Application.Dtos;

public sealed class DataImportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ImportCounts? Counts { get; init; }

    public static DataImportResult Failed(string message) =>
        new() { Success = false, Message = message };

    public static DataImportResult Succeeded(string message, ImportCounts counts) =>
        new() { Success = true, Message = message, Counts = counts };
}

public sealed record ImportCounts(int Accounts, int Categories, int Items, int Transactions);
