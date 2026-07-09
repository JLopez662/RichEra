namespace RichEra.Domain.Entities;

// One "weigh-in": the total across active accounts on a given day. Recorded passively
// (opening Home upserts today's row) and on explicit balance corrections, so the Home
// trend line is the money analog of a weight chart — no manual tracking required.
public class BalanceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }        // unique — one weigh-in per day, last write wins
    public decimal TotalBalance { get; set; }
}
