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
    Guid? TransferGroupId,
    DateTime CreatedUtc,        // when the row was added — shown in the detail view
    string? ExternalId,         // OFX FITID for imported rows (the bank's reference); null otherwise
    string? ImportSource,       // e.g. "ofx" for imported rows; null for manual/transfer
    Guid? ItemId,               // set when the row logs an Item (confirmed occurrence or ad-hoc log)
    decimal Quantity = 1m);     // servings logged; Amount is the row total, not per-serving

public record TransactionUpsertDto(
    Guid? Id,
    DateOnly Date,
    Guid AccountId,
    Guid? CategoryId,
    decimal Amount,
    TransactionDirection Direction,
    string? Note);

public record TransferUpsertDto(
    DateOnly Date,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Note);
