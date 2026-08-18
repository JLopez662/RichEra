using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

/// <summary>
/// Evaluates current app state and returns the secondary notifications that are due right now —
/// bills approaching, budget thresholds crossed, savings milestones, the weekly check-in. Each
/// result carries a stable DedupeKey so the Center records it exactly once. Platform-free; the
/// MAUI scheduler records the results and pushes any newly-added ones to the OS.
/// </summary>
public interface INotificationRuleEvaluator
{
    Task<IReadOnlyList<NotificationInput>> EvaluateAsync(NotificationRuleContext ctx, CancellationToken ct = default);
}
