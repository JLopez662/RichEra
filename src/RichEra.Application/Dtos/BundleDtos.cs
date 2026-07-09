namespace RichEra.Application.Dtos;

// One line of a saved bundle, resolved against live data at read time: Title/FromName come
// from the current entities when they still exist (rename-aware), falling back to the
// snapshot; IsAvailable = false means the target was archived or deleted since saving,
// so loading the bundle into the cart skips it.
public record BundleLineDto(
    Guid Id,
    string Kind,             // "expense" | "card" | "savings" — same token the shop routes use
    Guid OwnerId,
    decimal Amount,
    Guid? FromAccountId,
    string? FromName,
    string Title,
    bool IsAvailable);

public record BundleDto(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    IReadOnlyList<BundleLineDto> Lines)
{
    public int LineCount => Lines.Count;
    public decimal Total => Lines.Sum(l => l.Amount);
    public int UnavailableCount => Lines.Count(l => !l.IsAvailable);
}

// Input shape when saving a cart as a bundle.
public record BundleLineInput(
    string Kind,
    Guid OwnerId,
    decimal Amount,
    Guid? FromAccountId,
    string Title);

// One point of the Home net-worth trend (the weigh-in chart).
public record BalanceTrendPointDto(DateOnly Date, decimal Total);
