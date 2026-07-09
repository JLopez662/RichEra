namespace RichEra.Domain.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }

    // Bank-account identity captured from an imported OFX statement (BANKACCTFROM). Lets
    // statement imports auto-match this account on re-import instead of creating duplicates.
    // Null until an OFX file is linked to this account.
    public string? ExternalAccountId { get; set; }  // OFX <ACCTID>
    public string? BankId { get; set; }             // OFX <BANKID>
}

public enum AccountType
{
    Checking = 0,
    Savings = 1,
    CreditCard = 2,
    Cash = 3,
    Investment = 4,
    Loan = 5,
    Other = 99,
}
