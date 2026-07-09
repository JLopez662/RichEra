using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Infrastructure.Services;

// Tolerant parser for OFX 1.x SGML statement exports (BPPR "Microsoft Money" format).
// Leaf tags (<FITID>, <TRNAMT>, ...) are NOT closed, so we read each value from just
// after the '>' up to the next '<' or end of line. Aggregate tags (<STMTTRN>) are closed.
public partial class OfxParser : IOfxParser
{
    // Windows-1252 is the de-facto charset for OFX 1.x SGML. Resolve once; fall back to Latin1
    // (built-in) if the code-pages provider is unavailable on the platform.
    private static readonly Encoding Windows1252 = ResolveCp1252();

    private static Encoding ResolveCp1252()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    // Pick the text encoding from the OFX header: explicit UTF-8 → UTF-8; otherwise (USASCII /
    // CHARSET:1252 / unknown) decode as Windows-1252 — a superset of ASCII, so pure-ASCII files
    // are unaffected while Spanish accents/ñ decode correctly instead of becoming U+FFFD.
    private static Encoding ResolveEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(false);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

        var headerLen = Math.Min(bytes.Length, 512);
        var header = Encoding.ASCII.GetString(bytes, 0, headerLen).ToUpperInvariant();
        if (header.Contains("ENCODING:UTF-8") || header.Contains("ENCODING:UTF8")) return new UTF8Encoding(false);
        return Windows1252;
    }

    public ParsedStatement Parse(Stream stream)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }
        var text = ResolveEncoding(bytes).GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..]; // drop any decoded BOM

        // Account identity comes from the first BANKACCTFROM (or CCACCTFROM) block; these
        // tags don't recur inside STMTTRN entries, so the first match is the right one.
        var bankId = ReadTag(text, "BANKID");
        var acctId = ReadTag(text, "ACCTID");
        var acctType = ReadTag(text, "ACCTTYPE");
        // Credit-card statements use <CCACCTFROM> and carry no <ACCTTYPE>; flag that so the
        // suggested account type is "credit card" rather than the bank-account default.
        if (string.IsNullOrWhiteSpace(acctType) && Regex.IsMatch(text, "<CCACCTFROM>", RegexOptions.IgnoreCase))
            acctType = "CREDITCARD";
        var currency = ReadTag(text, "CURDEF");

        // Ledger balance lives in a closed <LEDGERBAL> aggregate; read BALAMT from within it
        // so we don't accidentally grab an AVAILBAL value elsewhere in the file.
        decimal? ledgerBalance = null;
        var ledgerBlock = Regex.Match(text, "<LEDGERBAL>(.*?)</LEDGERBAL>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (ledgerBlock.Success &&
            decimal.TryParse(ReadTag(ledgerBlock.Groups[1].Value, "BALAMT"), NumberStyles.Number, CultureInfo.InvariantCulture, out var bal))
            ledgerBalance = bal;

        var transactions = new List<ParsedTransaction>();
        foreach (Match m in StmtTrnRegex().Matches(text))
        {
            var block = m.Value;
            var fitId = ReadTag(block, "FITID")?.Trim();
            var dtPosted = ReadTag(block, "DTPOSTED");
            var trnAmt = ReadTag(block, "TRNAMT");

            if (string.IsNullOrWhiteSpace(fitId) ||
                string.IsNullOrWhiteSpace(dtPosted) ||
                string.IsNullOrWhiteSpace(trnAmt))
                continue;

            if (!TryParseOfxDate(dtPosted, out var date)) continue;
            if (!decimal.TryParse(trnAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out var signed)) continue;

            var direction = signed < 0 ? TransactionDirection.Expense : TransactionDirection.Income;
            var amount = Math.Abs(signed);
            var name = ReadTag(block, "NAME")?.Trim();
            var memo = ReadTag(block, "MEMO")?.Trim();

            transactions.Add(new ParsedTransaction(fitId!, date, amount, direction, name, memo));
        }

        return new ParsedStatement(bankId?.Trim(), acctId?.Trim(), acctType?.Trim(), currency?.Trim(), ledgerBalance, transactions);
    }

    // Value of an SGML leaf tag: text after '>' up to the next '<', newline, or EOL.
    // Works whether or not the tag has a matching close tag.
    private static string? ReadTag(string text, string tag)
    {
        var m = Regex.Match(text, "<" + Regex.Escape(tag) + ">([^<\r\n]*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static bool TryParseOfxDate(string raw, out DateOnly date)
    {
        // OFX dates are YYYYMMDD optionally followed by HHMMSS[.xxx][TZ]; we only need the day.
        date = default;
        if (raw.Length < 8) return false;
        if (DateTime.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }

    [GeneratedRegex("<STMTTRN>.*?</STMTTRN>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StmtTrnRegex();
}
