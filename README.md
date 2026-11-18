# Rich Era

A personal finance tracker built to answer one question: *how far am I from buying back my time?*

It logs money the way a food diary logs meals: name the things you pay or get paid for once, then log them in a tap.

## Features
- **Money log**: a library of named money items (bills, subscriptions, paychecks, debt payments), saved bundles like "Payday routine", and a pay shop cart to log several payments at once.
- **Runway to payday**: every expense due before your next paycheck, in date order, with a running balance.
- **Paychecks**: recurring income with a calendar, a year-long forecast of projected vs. actual pay, and flags for three-paycheck months.
- **Monthly budget**: planned income, savings target and spend budget, what's safe to spend per day, and a Needs / Wants / Debt / Future split.
- **Debts**: when a debt will be paid off and the interest it will cost at the current payment, based on its APR.
- **Savings goal**: set aside a percentage of each paycheck, as a running tally or moved into a linked savings account.
- **Net-worth weigh-in**: a daily snapshot of your total balance, shown as a trend line on Home.
- **Report card**: money in and out each month, the biggest money moves, and the hours worked to fund the month.
- **Accounts**: accounts, transfers and balance reconciliation.
- **Bank statement import**: OFX/QFX files; rows already imported are skipped, so overlapping date ranges are safe.
- **Receipt scanning**: photograph a receipt and review the purchase lines before they are saved.
- **Reminders**: notifications for upcoming bills, budget thresholds, savings milestones and a weekly check-in.
- **Backup and restore**: the whole database as one JSON file.
- **Feed**: a YouTube Shorts feed from the channels and searches you follow.

## Stack
- .NET 9, MAUI Blazor Hybrid (Windows + Android)
- EF Core 9 + SQLite (local-first, single user)
- Plugin.Maui.OCR for receipt text, Plugin.LocalNotification for reminders
- xUnit tests for budgets, savings, runway, statement import, OFX parsing and backups

## Solution layout
- `src/RichEra.Domain` — entities, enums, value objects
- `src/RichEra.Application` — service abstractions, DTOs
- `src/RichEra.Infrastructure` — EF Core, SQLite, repositories, import and parsing services
- `src/RichEra.Maui` — MAUI Blazor Hybrid host
- `tests/RichEra.Tests` — xUnit tests

## Next
- Blazor web port sharing the Domain and Application layers
- Bank API integration (deferred)

## Running it
Requires the .NET 9 SDK with the .NET MAUI workload. Open `RichEra.sln` and start `RichEra.Maui` on Windows or Android.
