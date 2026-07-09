using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<AccountDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<AccountDto> UpsertAsync(AccountUpsertDto dto, CancellationToken ct = default);

    // Directly sets CurrentBalance, optionally logging the difference as a transaction.
    Task<AccountDto> SetBalanceAsync(SetBalanceDto dto, CancellationToken ct = default);

    Task ArchiveAsync(Guid id, CancellationToken ct = default);
}
