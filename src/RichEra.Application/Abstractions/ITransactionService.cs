using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Application.Abstractions;

public interface ITransactionService
{
    Task<IReadOnlyList<TransactionDto>> ListAsync(int year, int month, Guid? accountId = null, CancellationToken ct = default);

    // Searches across ALL months. Every whitespace-separated token must appear (case-insensitive)
    // in the note, category name, or account name, so "paypal aft" matches "PURCHASE PAYPAL AFT P2P".
    // Newest first, capped at limit. Empty/whitespace query returns nothing.
    Task<IReadOnlyList<TransactionDto>> SearchAsync(string query, int limit = 300, CancellationToken ct = default);

    // Distinct (year, month) pairs that contain at least one transaction — lets the month picker
    // mark which months actually have data. Ordered oldest first.
    Task<IReadOnlyList<(int Year, int Month)>> GetActiveMonthsAsync(CancellationToken ct = default);
    Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TransactionDto> UpsertAsync(TransactionUpsertDto dto, CancellationToken ct = default);
    Task CreateTransferAsync(TransferUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Spins up a scheduled Item pre-filled from an existing transaction and links the transaction
    // to it as the first occurrence. Returns the new item's id. The account balance is not
    // touched — this transaction already applied it. Throws for transfers.
    Task<Guid> MakeRecurringAsync(Guid transactionId, RepeatFrequency frequency, CancellationToken ct = default);
}
