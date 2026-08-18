using System.Globalization;
using System.Text.RegularExpressions;
using RichEra.Application.Dtos;

namespace RichEra.Infrastructure.Services;

// Turns the raw text an OCR engine reads off a store receipt into candidate purchase lines.
//
// This is deliberately a *heuristic*, not a bank-grade parser: receipts vary wildly by retailer
// and OCR adds its own noise. The goal is to get the user ~80% of the way there — a list of
// {name, price} they then review and correct on-screen — rather than to be right every time.
// It is the main tuning surface for the feature; OCR, UI, and logging around it stay stable
// while this evolves. Kept pure + public so it can be unit-tested without any device/OCR.
//
// Strategy, per text line:
//   * skip blank lines and known non-item lines (totals, tax, tender, change, card info…);
//   * take the right-most money token (e.g. "2.58", "11.78") as the price — on the vast
//     majority of receipts the line total sits at the far right;
//   * treat the text left of the first long digit-run (the UPC/PLU column), or of the price if
//     there is no such run, as the product name, then clean it up.
// Lines with no plausible price, or whose name is too short after cleaning, are dropped.
public static class ReceiptParser
{
    // A money amount like 2.58 / 19.97 / 1,299.00 that is NOT part of a longer number, so a tax
    // *rate* such as "8.625" won't be misread as "8.62".
    private static readonly Regex Money = new(
        @"(?<!\d)(\d{1,3}(?:,\d{3})*|\d+)\.(\d{2})(?!\d)",
        RegexOptions.Compiled);

    // A run of 5+ digits: a UPC/PLU/SKU column we don't want inside the product name.
    private static readonly Regex LongDigitRun = new(@"\d{5,}", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Substrings that mark a line as summary/metadata rather than a purchased item.
    private static readonly string[] StopWords =
    {
        "SUBTOTAL", "TOTAL", "TAX", "TEND", "CHANGE", "BALANCE", "DEBIT", "CREDIT",
        "CASH", "VISA", "MASTERCARD", "MCARD", "ACCOUNT", "APPROVAL", "AUTH", "REF",
        "ITEMS SOLD", "ROUNDING", "CASHBACK", "CASH BACK", "AMOUNT", "PAYMENT", "PURCHASE",
    };

    public static IReadOnlyList<ReceiptLineDto> Parse(string? ocrText)
    {
        var results = new List<ReceiptLineDto>();
        if (string.IsNullOrWhiteSpace(ocrText))
            return results;

        foreach (var raw in ocrText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var upper = line.ToUpperInvariant();

            // A percent sign only appears on tax-rate lines, never on an item's price line.
            if (upper.Contains('%'))
                continue;

            if (StopWords.Any(w => upper.Contains(w)))
                continue;

            // Price = right-most money token on the line.
            var moneyMatches = Money.Matches(line);
            if (moneyMatches.Count == 0)
                continue;
            var priceMatch = moneyMatches[^1];
            if (!decimal.TryParse(priceMatch.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                || price <= 0m)
                continue;

            // Name = text before the UPC run, else before the price. Strip any leftover digit
            // runs, collapse whitespace, and trim stray separators/punctuation.
            var upc = LongDigitRun.Match(line);
            var nameEnd = upc.Success ? upc.Index : priceMatch.Index;
            var name = line[..Math.Max(0, nameEnd)];
            name = LongDigitRun.Replace(name, " ");
            name = Whitespace.Replace(name, " ").Trim(' ', '-', '*', '#', ':', '.', ',');

            if (name.Length < 2)
                continue;

            results.Add(new ReceiptLineDto(line, name, price));
        }

        return results;
    }
}
