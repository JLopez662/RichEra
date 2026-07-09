namespace RichEra.Domain.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public CategoryKind Kind { get; set; }
    public string? Icon { get; set; }
    public string? ColorHex { get; set; }
    public bool IsArchived { get; set; }

    // Macro roll-up for transactions that carry only a category (no Item). An Item's own
    // Bucket wins when both are present. Null = unassigned; income categories stay null.
    public MacroBucket? Bucket { get; set; }
}

public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}
