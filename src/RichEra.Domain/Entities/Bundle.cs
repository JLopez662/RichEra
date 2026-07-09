namespace RichEra.Domain.Entities;

// A saved cart — the "recipe" of the Money Log concept. "Costco run" or "Payday routine"
// is a named list of payable things that loads back into the shop cart in one tap.
public class Bundle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }

    public List<BundleLine> Lines { get; set; } = new();
}

// One cart line, frozen. Mirrors the shop cart's shape on purpose: Kind + OwnerId is the
// same soft reference ItemImage uses ("expense" = Item, "card" = Account, "savings" =
// SavingsGoal) — no FK, because the owner lives in one of three tables. TitleSnapshot keeps
// the row renderable even if the target is later renamed or archived; loading into the cart
// re-resolves live data and skips lines whose target no longer exists.
public class BundleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BundleId { get; set; }
    public Bundle? Bundle { get; set; }

    public string Kind { get; set; } = string.Empty;   // "expense" | "card" | "savings"
    public Guid OwnerId { get; set; }
    public decimal Amount { get; set; }
    public Guid? FromAccountId { get; set; }           // funding account (card/savings lines)
    public string TitleSnapshot { get; set; } = string.Empty;
}
