using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// Imports a parsed bank statement into an account. Idempotent: rows are deduped by
// (AccountId, FITID), so re-importing overlapping date ranges is safe. Can also detect the
// statement's bank account and either match an existing app account or create a new one.
public interface IStatementImportService
{
    // Parses the file's identity (bank/account id, type, currency, ledger balance, date span)
    // and reports whether it already matches an existing account — without writing anything.
    Task<StatementInspectionDto> InspectAsync(Stream stream, CancellationToken ct = default);

    // Parses and classifies rows without writing anything: which rows are new, duplicates,
    // reconcile into existing transactions, or pay a recurring rule occurrence — plus the
    // balance change committing would apply. Pass a null accountId to preview as a brand-new
    // account (nothing existing to match against; mode is treated as Merge).
    Task<ImportPreviewDto> PreviewAsync(Guid? accountId, Stream stream, StatementImportMode mode = StatementImportMode.Merge, CancellationToken ct = default);

    // Imports into an existing account. Merge adds only new rows, reconciles rows that match
    // existing transactions (same amount/direction, close date, similar title), and links rows
    // that pay a recurring rule occurrence. Replace deletes all existing transactions in the
    // account first, then imports. When updateBalance is true (the default) the account balance
    // is adjusted by the net of the changes; pass false to leave it untouched.
    // If the account has no bank link yet, the statement's account id is recorded so future
    // imports auto-match it.
    Task<ImportResultDto> CommitAsync(Guid accountId, Stream stream, StatementImportMode mode = StatementImportMode.Merge, bool updateBalance = true, CancellationToken ct = default);

    // Creates a new account from the statement (seeding its balance from the ledger balance)
    // and imports every row into it, in one step. Returns the new account id in the result.
    Task<ImportResultDto> CreateAndImportAsync(NewAccountFromStatementDto newAccount, Stream stream, CancellationToken ct = default);
}
