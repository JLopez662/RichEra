using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public record TransactionDto(
    Guid Id,
    DateOnly Date,
    Guid AccountId,
    string AccountName,
    Guid? CategoryId,
    string? CategoryName,
    decimal Amount,
    TransactionDirection Direction,
    string? Note,
    string? Name,
    Guid? TransferGroupId,
    DateTime CreatedUtc,        // when the row was added — shown in the detail view
    string? ExternalId,         // OFX FITID for imported rows (the bank's reference); null otherwise
    string? ImportSource,       // e.g. "ofx" for imported rows; null for manual/transfer
    Guid? ItemId,               // set when the row logs an Item (confirmed occurrence or ad-hoc log)
    decimal Quantity = 1m,      // servings logged; Amount is the row total, not per-serving
    MacroBucket? Bucket = null, // effective macro: the item's bucket, else the category's
    string? ItemName = null,    // when the row logs a library item
    string? UnitName = null);   // that item's serving name ("month", "tank"…)

public record TransactionUpsertDto(
    Guid? Id,
    DateOnly Date,
    Guid AccountId,
    Guid? CategoryId,
    decimal Amount,
    TransactionDirection Direction,
    string? Note,
    Guid? ItemId = null,      // links the row to a library item (quick-log). Applied on create
                              // only — edits never re-link or unlink; recurrence flows own that.
    decimal Quantity = 1m,    // servings this row represents; Amount stays the row TOTAL
    string? Name = null);     // user-given row name; primary display label for manual rows

public record TransferUpsertDto(
    DateOnly Date,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Note);
