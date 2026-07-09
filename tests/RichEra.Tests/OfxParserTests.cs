using System.Text;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

public class OfxParserTests
{
    // Synthetic OFX 1.0.2 SGML fixture mirroring the BPPR "Microsoft Money" dialect:
    // unclosed leaf tags, closed aggregates, DTPOSTED with a time component, signed TRNAMT.
    // NOT derived from any real account data.
    private const string SampleOfx = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE

        <OFX>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <TRNUID>0
        <STATUS>
        <CODE>0
        <SEVERITY>INFO
        </STATUS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>001
        <ACCTID>000111222
        <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260501000000
        <DTEND>20260531000000
        <STMTTRN>
        <TRNTYPE>CREDIT
        <DTPOSTED>20260502040000
        <TRNAMT>1500.00
        <FITID>AAA111
        <NAME>PAYROLL DEPOSIT
        <MEMO>Biweekly salary deposit
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20260503040000
        <TRNAMT>-700.00
        <FITID>BBB222
        <NAME>RENT PAYMENT
        <MEMO>Monthly rent
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20260505040000
        <TRNAMT>-42.50
        <FITID>CCC333
        <NAME>COFFEE SHOP
        </STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>0
        <DTASOF>20260531000000
        </LEDGERBAL>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    private static Stream ToStream(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Parse_ReadsAccountIdentity()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));

        Assert.Equal("001", stmt.BankId);
        Assert.Equal("000111222", stmt.AccountId);
        Assert.Equal("CHECKING", stmt.AccountType);
    }

    [Fact]
    public void Parse_ReadsAllTransactions()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));
        Assert.Equal(3, stmt.Transactions.Count);
    }

    [Fact]
    public void Parse_PositiveAmount_BecomesIncome()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));
        var credit = stmt.Transactions.Single(t => t.FitId == "AAA111");

        Assert.Equal(TransactionDirection.Income, credit.Direction);
        Assert.Equal(1500.00m, credit.Amount);
        Assert.Equal(new DateOnly(2026, 5, 2), credit.Date);
    }

    [Fact]
    public void Parse_NegativeAmount_BecomesExpense_StoredPositive()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));
        var debit = stmt.Transactions.Single(t => t.FitId == "BBB222");

        Assert.Equal(TransactionDirection.Expense, debit.Direction);
        Assert.Equal(700.00m, debit.Amount); // always positive; sign lives in Direction
    }

    [Fact]
    public void Parse_PrefersMemoOverName_ForDescription()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));
        var withMemo = stmt.Transactions.Single(t => t.FitId == "AAA111");
        var memoless = stmt.Transactions.Single(t => t.FitId == "CCC333");

        Assert.Equal("Biweekly salary deposit", withMemo.Description); // MEMO wins
        Assert.Equal("COFFEE SHOP", memoless.Description);             // falls back to NAME
    }

    [Fact]
    public void Parse_ReadsCurrencyAndLedgerBalance()
    {
        var stmt = new OfxParser().Parse(ToStream(SampleOfx));

        Assert.Equal("USD", stmt.Currency);
        Assert.Equal(0m, stmt.LedgerBalance); // <LEDGERBAL><BALAMT>0
    }

    [Fact]
    public void Parse_DecodesWindows1252AccentedText()
    {
        // OFX 1.x SGML is Windows-1252, not UTF-8. 'Ñ' (0xD1) is a single byte that is invalid
        // UTF-8; confirm it round-trips into the description instead of becoming U+FFFD.
        const string ofx = """
            OFXHEADER:100
            DATA:OFXSGML
            ENCODING:USASCII
            CHARSET:1252

            <OFX>
            <BANKTRANLIST>
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260505040000
            <TRNAMT>-9.99
            <FITID>NTILDE
            <MEMO>COMPRA EN ESPAÑA
            </STMTTRN>
            </BANKTRANLIST>
            </OFX>
            """;
        // Latin1 and CP1252 share the 0xA0–0xFF range, so this produces real CP1252 bytes.
        var bytes = Encoding.Latin1.GetBytes(ofx);

        var stmt = new OfxParser().Parse(new MemoryStream(bytes));
        var t = Assert.Single(stmt.Transactions);

        Assert.Equal("COMPRA EN ESPAÑA", t.Description);
        Assert.DoesNotContain('�', t.Description!); // no mojibake
    }

    [Fact]
    public void Parse_DropsRowsMissingRequiredFields()
    {
        // STMTTRN with no FITID/TRNAMT must be skipped, not throw.
        const string ofx = """
            OFXHEADER:100
            DATA:OFXSGML

            <OFX>
            <BANKTRANLIST>
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260505040000
            <NAME>INCOMPLETE ROW
            </STMTTRN>
            </BANKTRANLIST>
            </OFX>
            """;

        var stmt = new OfxParser().Parse(ToStream(ofx));
        Assert.Empty(stmt.Transactions);
    }

    [Fact]
    public void Parse_EmptyOrJunk_ReturnsNoTransactions()
    {
        var stmt = new OfxParser().Parse(ToStream("not an ofx file at all"));
        Assert.Empty(stmt.Transactions);
    }
}
