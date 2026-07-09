using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface ICashFlowService
{
    Task<MonthlySummaryDto> GetMonthlySummaryAsync(int year, int month, CancellationToken ct = default);
}
