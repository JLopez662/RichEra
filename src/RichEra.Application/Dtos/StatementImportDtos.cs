using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public enum StatementImportMode
{
    // Add only rows not already present (deduped by FITID); keep existing data.
    Merge = 0,
    // Delete ALL existing transactions in the target account, then import the file fresh.
    Replace = 1,
}

// Raw output of the OFX parser — pure data, no database knowledge.
public record ParsedStatement(
    string? BankId,
    string? AccountId,
    string? AccountType,
    string? Currency,           // OFX <CURDEF>
    decimal? LedgerBalance,     // OFX <LEDGERBAL><BALAMT> — used as a new account's starting balance
    IReadOnlyList<ParsedTransaction> Transactions);

public record ParsedTransaction(
    string FitId,               // OFX <FITID> — the bank's unique id, our dedupe key
    DateOnly Date,
    decimal Amount,             // always positive; sign carried by Direction
    TransactionDirection Direction,
    string? Name,
    string? Memo)
{
    // What we'll store as the transaction Note: prefer the fuller MEMO text.
    public string? Description => string.IsNullOrWhiteSpace(Memo) ? Name : Memo;
}

// Read-only look at a statement file: its bank-account identity, a match against existing
// accounts, and a suggested new-account name — so the import UI can offer "use existing"
// vs "create new" before anything is written.
public record StatementInspectionDto(
    string? BankId,
    string? StatementAccountId,   // raw OFX <ACCTID>
    string? MaskedAccountId,      // e.g. "••633" for display
    AccountType SuggestedType,    // mapped from <ACCTTYPE>
    string? Currency,
    decimal? LedgerBalance,       // starting balance offered when creating a new account
    int TransactionCount,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    Guid? MatchedAccountId,       // existing account already linked to this StatementAccountId
    string? MatchedAccountName,
    string SuggestedAccountName); // default name when creating a new account

// Instruction to create a new account from a statement and import into it in one step.
public record NewAccountFromStatementDto(
    string Name,
    AccountType Type,
    string? ExternalAccountId,
    string? BankId,
    decimal OpeningBalance);

// What the import will do with one statement row.
public enum ImportRowKind
{
    // Inserted as a brand-new transaction.
    New = 0,
    // Skipped: FITID already in the account, or repeated within this file.
    Duplicate = 1,
    // Reconciled into an existing transaction (same amount/direction, close date, similar
    // title) — the existing row gets the FITID stamped; nothing new is inserted and the
    // balance is untouched (it was already counted when the transaction was created).
    MatchesExisting = 2,
    // Marks a recurring rule's occurrence as paid: either inserted linked to the rule, or —
    // when the user already confirmed the occurrence — the confirmed transaction's date and
    // amount are updated to the bank's actual values.
    PaysRecurring = 3,
}

// One candidate row shown in the import preview before the user commits.
public record ImportPreviewRow(
    string FitId,
    DateOnly Date,
    decimal Amount,
    TransactionDirection Direction,
    string? Description,
    ImportRowKind Kind,
    string? MatchLabel = null); // what it matched: the rule name, or the existing row's note

public record ImportPreviewDto(
    Guid AccountId,             // Guid.Empty when previewing into a not-yet-created account
    string AccountName,
    string? StatementAccountId, // OFX <ACCTID>, surfaced so the user can sanity-check the file
    StatementImportMode Mode,
    int NewCount,               // rows inserted as transactions (includes recurring-linked inserts)
    int DuplicateCount,         // rows skipped (already present, or repeated within this file)
    int ExistingDeleteCount,    // Replace mode only: existing rows that would be deleted first
    decimal NetImported,        // net balance change if committed with the balance update enabled
    IReadOnlyList<ImportPreviewRow> Rows,
    int MatchedExistingCount = 0,      // rows reconciled into transactions that already existed
    int RecurringPaidCount = 0,        // rows that mark a recurring occurrence as paid
    decimal? CurrentBalance = null,    // target account's balance now (null when creating new)
    decimal? ProjectedBalance = null); // balance after commit with the update enabled

public record ImportResultDto(
    StatementImportMode Mode,
    int ImportedCount,
    int SkippedCount,
    int DeletedCount,            // Replace mode only
    DateOnly? FirstDate = null,  // earliest date among imported rows; null when nothing imported
    DateOnly? LastDate = null,   // latest date among imported rows — the month to jump to
    Guid? AccountId = null,      // the target account (useful when one was just created)
    int MatchedExistingCount = 0, // rows reconciled into transactions that already existed
    int RecurringPaidCount = 0,   // rows that marked a recurring occurrence as paid
    decimal BalanceDelta = 0m,    // net applied to the account balance (0 when update disabled)
    decimal? NewBalance = null);  // account balance after the import
