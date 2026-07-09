namespace RichEra.Maui.Services;

// What kind of real thing a shop "product" maps to.
public enum ShopItemKind
{
    Expense,   // an expense item (Item, Direction = Expense)
    Card,      // a credit-card account with a balance
    Savings,   // a savings goal
}

public sealed class ShopCartLine
{
    public ShopItemKind Kind { get; init; }
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "shopping-bag";
    public decimal Amount { get; set; }

    // Funding account the money comes from — required for Card payments and MoveToSavings
    // deposits, ignored for Expense (uses the rule's own account) and TrackOnly savings.
    public Guid? FromAccountId { get; set; }
    public string? FromName { get; set; }

    public bool PaymentMade { get; set; } // receipt: marked as actually paid on the real biller
}

// In-memory shopping cart for the "pay like you shop" experience. The order itself is a
// simulation (no money moves); the real action happens later via the "Payment made" confirm.
// Registered as a singleton — single-user app, one cart (and one open receipt) for the session.
public sealed class ShopCartState
{
    private readonly List<ShopCartLine> _lines = new();

    public IReadOnlyList<ShopCartLine> Lines => _lines;
    public IReadOnlyList<ShopCartLine> LastOrder { get; private set; } = Array.Empty<ShopCartLine>();

    // Identity of the last placed order — captured here so the receipt view survives navigating
    // away and back (the page renders the receipt straight from this singleton).
    public string? OrderNumber { get; private set; }
    public DateTime PlacedUtc { get; private set; }

    public int Count => _lines.Count;
    public decimal Total => _lines.Sum(l => l.Amount);

    public bool Contains(ShopItemKind kind, Guid id) => _lines.Any(l => l.Kind == kind && l.Id == id);

    // One line per item; adding the same item again just updates its amount + funding account.
    public void Add(ShopCartLine line)
    {
        var existing = _lines.FirstOrDefault(l => l.Kind == line.Kind && l.Id == line.Id);
        if (existing is not null)
        {
            existing.Amount = line.Amount;
            existing.FromAccountId = line.FromAccountId;
            existing.FromName = line.FromName;
        }
        else _lines.Add(line);
    }

    public void Remove(ShopItemKind kind, Guid id) => _lines.RemoveAll(l => l.Kind == kind && l.Id == id);

    public void Clear() => _lines.Clear();

    // "Place order" (simulated): snapshot the cart as the last order, stamp it, then empty the cart.
    public void PlaceOrder()
    {
        LastOrder = _lines
            .Select(l => new ShopCartLine
            {
                Kind = l.Kind,
                Id = l.Id,
                Title = l.Title,
                Icon = l.Icon,
                Amount = l.Amount,
                FromAccountId = l.FromAccountId,
                FromName = l.FromName,
            })
            .ToList();
        OrderNumber = "RE-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        PlacedUtc = DateTime.UtcNow;
        _lines.Clear();
    }

    // Whether there's a placed order with at least one line still awaiting a "Payment made".
    public bool HasOpenReceipt => LastOrder.Count > 0;

    // Dismiss the receipt (e.g. the user tapped "Done").
    public void ClearReceipt()
    {
        LastOrder = Array.Empty<ShopCartLine>();
        OrderNumber = null;
    }
}
