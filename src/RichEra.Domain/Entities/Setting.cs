namespace RichEra.Domain.Entities;

// Tiny key-value store for app-level knobs that don't warrant their own table —
// e.g. "budget.override.2026-07" for a manually pinned monthly spend budget.
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
