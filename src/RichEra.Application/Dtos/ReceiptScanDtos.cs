namespace RichEra.Application.Dtos;

// One candidate purchase line parsed from a scanned receipt. RawText is the original OCR line
// (kept so the review screen can show what a row came from); Name/Amount are the parser's best
// guess and are expected to be edited by the user before anything is logged.
public record ReceiptLineDto(string RawText, string Name, decimal Amount);

// The result of scanning one receipt image: the parsed candidate lines plus the full recognized
// text (surfaced behind a "show raw text" toggle for when the parse looks off).
public record ReceiptScanResult(IReadOnlyList<ReceiptLineDto> Lines, string RawText)
{
    public static readonly ReceiptScanResult Empty = new(Array.Empty<ReceiptLineDto>(), string.Empty);
}
