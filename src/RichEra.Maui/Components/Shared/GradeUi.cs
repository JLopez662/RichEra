using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Maui.Components.Shared;

// The A–D "food grade" for money items — computed, never assigned, and deliberately simple
// enough to explain in one line each. Null = not enough signal to grade (unsorted, no micros).
public static class GradeUi
{
    public static (string Letter, string Css, string Label)? For(ItemDto it)
    {
        if (it.Direction == TransactionDirection.Income)
            return ("A", "grade--a", "money in");

        // Debt outranks everything else about the item: the APR decides how bad.
        if (it.Bucket == MacroBucket.Debt || it.PrincipalBalance is > 0m || it.AprPct is > 0m)
            return it.AprPct is >= 15m
                ? ("D", "grade--d", "high-interest debt")
                : ("C", "grade--c", "debt");

        if (it.Bucket == MacroBucket.Future || it.ExpectedReturnPct is > 0m)
            return ("A", "grade--a", "future you");

        return it.Bucket switch
        {
            MacroBucket.Needs => ("B", "grade--b", "a need"),
            MacroBucket.Wants => ("C", "grade--c", "a want"),
            _ => null,
        };
    }
}
