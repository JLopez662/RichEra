namespace RichEra.Domain.Entities;

// A user-supplied picture for a "shop" item (a recurring expense, a credit card, or a savings
// goal). Stored as bytes in the database — not a file path — for two reasons: the Blazor WebView
// can't load arbitrary on-device file paths, but renders bytes directly as a data: URI; and the
// bytes travel with the JSON backup so an image survives backup/restore.
//
// Keyed by (Kind, OwnerId): a soft reference to the owning entity that matches the shop's
// /shop/item/{kind}/{id} addressing. There is no foreign key because the owner is one of three
// different tables — the Kind disambiguates which one. Images are downscaled before storage, so a
// row is a few tens of KB, not megabytes.
public class ItemImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // "expense" | "card" | "savings" — the lowercased ShopItemKind, same token the routes use.
    public string Kind { get; set; } = string.Empty;

    // Id of the owning Item / Account / SavingsGoal.
    public Guid OwnerId { get; set; }

    public string ContentType { get; set; } = "image/jpeg";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
