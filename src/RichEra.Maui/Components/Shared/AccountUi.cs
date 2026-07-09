using RichEra.Domain.Entities;

namespace RichEra.Maui.Components.Shared;

/// <summary>
/// Account-type visuals (icon name, tile tint, label) shared by Home, Accounts,
/// and the total-balance sheet so the mapping can't drift between pages.
/// </summary>
public static class AccountUi
{
    public static string IconName(AccountType t) => t switch
    {
        AccountType.Checking => "landmark",
        AccountType.Savings => "piggy-bank",
        AccountType.CreditCard => "credit-card",
        AccountType.Cash => "banknote",
        AccountType.Investment => "trending-up",
        AccountType.Loan => "home",
        _ => "folder",
    };

    public static string TileClass(AccountType t) => t switch
    {
        AccountType.Checking => "tile--blue",
        AccountType.Savings => "tile--green",
        AccountType.CreditCard => "tile--amber",
        AccountType.Cash => "tile--green",
        AccountType.Investment => "tile--violet",
        AccountType.Loan => "tile--slate",
        _ => "tile--slate",
    };

    public static string Label(AccountType t) => t switch
    {
        AccountType.CreditCard => "Credit card",
        _ => t.ToString(),
    };
}
