# Rich Era

A personal finance tracker built to answer one question: *how far am I from buying back my time?*

## Stack
- .NET 9, MAUI Blazor Hybrid (Windows + Android)
- EF Core + SQLite (local-first, single user)
- Future: Blazor web port sharing Domain + Application layers

## Solution layout
- `src/RichEra.Domain` — entities, enums, value objects
- `src/RichEra.Application` — service abstractions, DTOs
- `src/RichEra.Infrastructure` — EF Core, SQLite, repositories
- `src/RichEra.Maui` — MAUI Blazor Hybrid host

## MVP scope
Accounts, transactions (income/expense + transfers), categories, monthly cash-flow dashboard.

## Roadmap
1. Manual entry MVP
2. Debts + payoff projections
3. Budgets + net-worth tracking
4. CSV import
5. Bank API integration (deferred)
