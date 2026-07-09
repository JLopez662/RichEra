using System.Text.RegularExpressions;

namespace RichEra.Infrastructure.Services;

// Fuzzy title matching between imported statement rows and data already in the app: existing
// transactions (so a purchase the user logged before the bank posted it isn't duplicated) and
// recurring rules (so an imported row can mark a rule occurrence as paid).
//
// Bank descriptions are noisy ("PURCHASE WENDY S PUNTA CANA ON 06/27/26 HIGUEY DO") while the
// user's side is short ("Wendy's", "Planet Fitness"), so similarity is containment of the
// shorter side's significant tokens in the longer side's — not symmetric overlap.
internal static partial class TransactionMatcher
{
    // How far apart the user's date and the bank's posted date may be and still mean the same
    // transaction. BPPR posts card purchases 1–4 days after the purchase date (more over
    // weekends), and users typically log on the purchase date.
    public const int AdoptDateWindowDays = 4;

    // How far an actual payment may land from a rule's projected occurrence date and still
    // count as paying that occurrence.
    public const int OccurrenceWindowDays = 5;

    // Processor/channel words that appear on almost every row and carry no merchant identity.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "PURCHASE", "PAYMENT", "PMT", "EFT", "ACH", "POS", "ATM", "DEBIT", "CREDIT", "DEPOSIT",
        "TRANF", "TRANSF", "TRANSFER", "TRANSFERENCIA", "WITHDRAWAL", "RECURRING", "CHECKCARD",
        "ON", "AT", "THE", "OF", "TO", "FROM", "FOR", "AND", "INC", "LLC", "CORP", "CO",
        "DE", "LA", "EL", "EN", "CON", "PARA", "CUENTA",
    };

    // True when the two titles plausibly describe the same merchant/payee.
    public static bool Similar(string? a, string? b) => Similarity(a, b) >= 0.5;

    // 0..1: fraction of the shorter side's significant tokens found in the longer side.
    // 0 when either side has no significant tokens (an empty note never matches anything).
    // A hit requires at least one shared token of length >= 3 so single two-letter overlaps
    // ("PR", "SJ") can't carry a match by themselves.
    public static double Similarity(string? a, string? b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;

        var (small, large) = ta.Count <= tb.Count ? (ta, tb) : (tb, ta);
        int hits = 0;
        bool strongHit = false;
        foreach (var t in small)
        {
            if (!large.Any(l => TokensMatch(t, l))) continue;
            hits++;
            if (t.Length >= 3) strongHit = true;
        }
        return strongHit ? (double)hits / small.Count : 0;
    }

    // Equal, or a prefix of the other (>= 3 chars) — bank exports truncate merchant names
    // ("ZACKRADFIEL" vs "Zack", "WAL" in "WAL-MART" vs "Walmart").
    private static bool TokensMatch(string x, string y)
    {
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true;
        var (shorter, longer) = x.Length <= y.Length ? (x, y) : (y, x);
        return shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
    }

    // Significant tokens: alphanumeric runs, minus pure numbers (dates, phone numbers, store
    // numbers), masked account tails (XXXX8540), single letters, and processor noise words.
    public static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (Match m in TokenRegex().Matches(text))
        {
            var t = m.Value;
            if (t.Length < 2) continue;
            if (t.All(char.IsDigit)) continue;
            if (t.Count(c => c is 'X' or 'x') >= t.Length / 2 && t.Any(char.IsDigit)) continue; // masked tail
            if (Noise.Contains(t)) continue;
            if (!result.Contains(t, StringComparer.OrdinalIgnoreCase)) result.Add(t);
        }
        return result;
    }

    [GeneratedRegex("[\\p{L}\\p{Nd}]+")]
    private static partial Regex TokenRegex();
}
