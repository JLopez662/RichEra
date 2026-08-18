using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

/// <summary>A notification to record into the Center (mirrors a scheduled push). FireTimeUtc is UTC.</summary>
public record NotificationInput(
    NotificationKind Kind,
    string Title,
    string Body,
    DateTime FireTimeUtc,
    int? PlatformNotificationId = null,
    string? DedupeKey = null);   // explicit stable key for event alerts; null → derived from kind|minute|title

/// <summary>A delivered notification as shown in the Center. FireTimeUtc is stamped UTC on the way out.</summary>
public record NotificationItemDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Body,
    DateTime FireTimeUtc,
    bool IsRead);

/// <summary>Current-state inputs + which kinds are enabled, passed to INotificationRuleEvaluator.</summary>
public record NotificationRuleContext(
    DateOnly Today,
    bool BillsDue,
    bool BudgetAlerts,
    bool SavingsMilestones,
    bool WeighIn,
    int BillsLeadDays = 3,
    int BudgetThresholdPct = 85);
