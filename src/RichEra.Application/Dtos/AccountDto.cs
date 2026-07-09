using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    decimal OpeningBalance,
    decimal CurrentBalance,
    bool IsArchived);

public record AccountUpsertDto(
    Guid? Id,
    string Name,
    AccountType Type,
    decimal OpeningBalance);

// Directly sets an account's CurrentBalance. When RecordAdjustment is true, the difference
// from the existing balance is also written as a signed "Balance adjustment" transaction so
// the transaction history still reconciles to the balance.
public record SetBalanceDto(
    Guid AccountId,
    decimal NewBalance,
    bool RecordAdjustment,
    string? Note);
