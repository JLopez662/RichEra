using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

// Imports OFX rows as transactions and keeps the rest of the app consistent with them:
//  - rows matching an existing transaction (same amount/direction, close date, similar title)
//    are reconciled into it instead of inserted, so pre-logged purchases don't duplicate;
//  - rows matching a recurring rule (Item) are linked to it, marking the occurrence
//    paid with the bank's actual date and amount;
//  - the account balance is adjusted by the net of what actually changed (opt-out per commit).
// The statement's own LEDGERBAL is NOT used for balances on existing accounts — BPPR exports
// it as 0 — only as the seed for a newly created account.
public class StatementImportService : IStatementImportService
{
    private readonly AppDbContext _db;
    private readonly IOfxParser _parser;

    public StatementImportService(AppDbContext db, IOfxParser parser)
    {
        _db = db;
        _parser = parser;
    }

    // What the import will do with one parsed row, decided by AnalyzeAsync and shared by
    // Preview (display only) and Commit (applies it).
    private enum PlanKind { New, Duplicate, Adopt, RecurringNew, RecurringUpdate }

    private sealed record RowPlan(
        ParsedTransaction Row,
        PlanKind Kind,
        Guid? TargetTransactionId, // Adopt / RecurringUpdate: the existing row to modify
        Guid? RuleId,              // RecurringNew / RecurringUpdate: the rule being paid
        Guid? RuleCategoryId,      // RecurringNew: category stamped on the inserted transaction
        string? MatchLabel,        // what it matched, for display (rule name / existing note)
        decimal BalanceDelta);     // signed effect on the account balance if committed

    private sealed record TxnSnapshot(
        Guid Id, DateOnly Date, decimal Amount, TransactionDirection Direction,
        string? Note, string? ExternalId, Guid? ItemId, string? ImportSource);

    private sealed record Analysis(IReadOnlyList<RowPlan> Plans, decimal DeletedNet, int DeleteCount);

    public async Task<StatementInspectionDto> InspectAsync(Stream stream, CancellationToken ct = default)
    {
        var s = _parser.Parse(stream);
        var type = MapAccountType(s.AccountType);
        var masked = Mask(s.AccountId);

        Guid? matchedId = null;
        string? matchedName = null;
        if (!string.IsNullOrWhiteSpace(s.AccountId))
        {
            var acctId = s.AccountId;
            var bankId = s.BankId;
            // Match on account id, disambiguated by bank id when both sides know it. Ordered by
            // CreatedUtc so the match stays deterministic even if two accounts ever share an id.
            var match = await _db.Accounts.AsNoTracking()
                .Where(a => !a.IsArchived && a.ExternalAccountId == acctId
                            && (bankId == null || a.BankId == null || a.BankId == bankId))
                .OrderBy(a => a.CreatedUtc).ThenBy(a => a.Id)
                .Select(a => new { a.Id, a.Name })
                .FirstOrDefaultAsync(ct);
            if (match is not null)
            {
                matchedId = match.Id;
                matchedName = match.Name;
            }
        }

        DateOnly? first = null, last = null;
        foreach (var t in s.Transactions)
        {
            if (first is null || t.Date < first) first = t.Date;
            if (last is null || t.Date > last) last = t.Date;
        }

        return new StatementInspectionDto(
            s.BankId, s.AccountId, masked, type, s.Currency, s.LedgerBalance,
            s.Transactions.Count, first, last, matchedId, matchedName,
            SuggestedName(type, masked));
    }

    public async Task<ImportPreviewDto> PreviewAsync(Guid? accountId, Stream stream, StatementImportMode mode = StatementImportMode.Merge, CancellationToken ct = default)
    {
        var statement = _parser.Parse(stream);

        Guid accId;
        string accountName;
        decimal? currentBalance = null;

        if (accountId is Guid id)
        {
            var account = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == id, ct);
            accId = account.Id;
            accountName = account.Name;
            currentBalance = account.CurrentBalance;
        }
        else
        {
            // Previewing into a not-yet-created account: nothing exists to match or delete,
            // and Replace is meaningless, so treat it as Merge against an empty slate.
            accId = Guid.Empty;
            accountName = SuggestedName(MapAccountType(statement.AccountType), Mask(statement.AccountId));
            mode = StatementImportMode.Merge;
        }

        var analysis = await AnalyzeAsync(accountId, statement, mode, ct);

        int newCount = 0, dupCount = 0, matchedCount = 0, recurringCount = 0;
        decimal net = 0m;
        var rows = new List<ImportPreviewRow>(analysis.Plans.Count);
        foreach (var p in analysis.Plans)
        {
            var kind = p.Kind switch
            {
                PlanKind.Duplicate => ImportRowKind.Duplicate,
                PlanKind.Adopt => ImportRowKind.MatchesExisting,
                PlanKind.RecurringNew or PlanKind.RecurringUpdate => ImportRowKind.PaysRecurring,
                _ => ImportRowKind.New,
            };
            switch (p.Kind)
            {
                case PlanKind.Duplicate: dupCount++; break;
                case PlanKind.Adopt: matchedCount++; break;
                case PlanKind.RecurringUpdate: matchedCount++; recurringCount++; break;
                case PlanKind.RecurringNew: newCount++; recurringCount++; break;
                default: newCount++; break;
            }
            net += p.BalanceDelta;
            rows.Add(new ImportPreviewRow(p.Row.FitId, p.Row.Date, p.Row.Amount, p.Row.Direction,
                p.Row.Description, kind, p.MatchLabel));
        }
        if (mode == StatementImportMode.Replace) net -= analysis.DeletedNet;

        return new ImportPreviewDto(
            accId, accountName, statement.AccountId, mode,
            newCount, dupCount, analysis.DeleteCount, net, rows,
            matchedCount, recurringCount,
            currentBalance, currentBalance is decimal b ? b + net : null);
    }

    public async Task<ImportResultDto> CommitAsync(Guid accountId, Stream stream, StatementImportMode mode = StatementImportMode.Merge, bool updateBalance = true, CancellationToken ct = default)
    {
        var statement = _parser.Parse(stream);
        var account = await _db.Accounts.FirstAsync(a => a.Id == accountId, ct);

        // Safety: never wipe an account on the strength of an empty/unparseable file.
        if (mode == StatementImportMode.Replace && statement.Transactions.Count == 0)
        {
            Debug.WriteLine($"[Import] account={accountId} mode=Replace aborted: file parsed to 0 transactions; nothing deleted.");
            return new ImportResultDto(mode, 0, 0, 0, AccountId: accountId, NewBalance: account.CurrentBalance);
        }

        var analysis = await AnalyzeAsync(accountId, statement, mode, ct);

        int deleted = 0;
        if (mode == StatementImportMode.Replace)
        {
            var toDelete = await _db.Transactions.Where(t => t.AccountId == accountId).ToListAsync(ct);
            deleted = toDelete.Count;
            _db.Transactions.RemoveRange(toDelete);
            // Commit deletes before inserting so the (AccountId, FITID) unique index can't
            // collide with rows that are on their way out.
            await _db.SaveChangesAsync(ct);
        }

        // Rows that modify an existing transaction need tracked entities.
        var targetIds = analysis.Plans
            .Where(p => p.TargetTransactionId is not null)
            .Select(p => p.TargetTransactionId!.Value)
            .ToList();
        var targets = targetIds.Count == 0
            ? new Dictionary<Guid, Transaction>()
            : await _db.Transactions.Where(t => targetIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        int imported = 0, skipped = 0, adopted = 0, recurring = 0;
        DateOnly? first = null, last = null;
        decimal balanceDelta = 0m;

        void TrackDates(DateOnly d)
        {
            if (first is null || d < first) first = d;
            if (last is null || d > last) last = d;
        }

        foreach (var plan in analysis.Plans)
        {
            switch (plan.Kind)
            {
                case PlanKind.Duplicate:
                    skipped++;
                    break;

                case PlanKind.Adopt:
                {
                    // The user already recorded this one — just stamp the bank identity on it.
                    var t = targets[plan.TargetTransactionId!.Value];
                    t.ExternalId = plan.Row.FitId;
                    t.ImportSource = "ofx";
                    adopted++;
                    break;
                }

                case PlanKind.RecurringUpdate:
                {
                    // Occurrence was confirmed ahead of time — correct it to the bank's actuals.
                    var t = targets[plan.TargetTransactionId!.Value];
                    t.Date = plan.Row.Date;
                    t.Amount = plan.Row.Amount;
                    t.ExternalId = plan.Row.FitId;
                    t.ImportSource = "ofx";
                    adopted++;
                    recurring++;
                    balanceDelta += plan.BalanceDelta;
                    TrackDates(plan.Row.Date);
                    break;
                }

                case PlanKind.RecurringNew:
                case PlanKind.New:
                    _db.Transactions.Add(new Transaction
                    {
                        Date = plan.Row.Date,
                        AccountId = accountId,
                        Amount = plan.Row.Amount,
                        Direction = plan.Row.Direction,
                        Note = Truncate(plan.Row.Description, 500),
                        ExternalId = plan.Row.FitId,
                        ImportSource = "ofx",
                        ItemId = plan.RuleId,
                        CategoryId = plan.RuleCategoryId,
                    });
                    imported++;
                    if (plan.Kind == PlanKind.RecurringNew) recurring++;
                    balanceDelta += plan.BalanceDelta;
                    TrackDates(plan.Row.Date);
                    break;
            }
        }

        if (mode == StatementImportMode.Replace) balanceDelta -= analysis.DeletedNet;

        var applied = updateBalance ? balanceDelta : 0m;
        if (applied != 0m) account.CurrentBalance += applied;

        // Learn the bank-account link so future imports auto-match this account.
        await StampIdentityAsync(account, statement, ct);

        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);

        Debug.WriteLine($"[Import] account={accountId} mode={mode} parsed={statement.Transactions.Count} " +
                        $"deleted={deleted} imported={imported} matched={adopted} recurring={recurring} skipped={skipped} " +
                        $"balanceDelta={applied} newBalance={account.CurrentBalance} " +
                        $"dateRange={first?.ToString("yyyy-MM-dd") ?? "-"}..{last?.ToString("yyyy-MM-dd") ?? "-"}");

        return new ImportResultDto(mode, imported, skipped, deleted, first, last, accountId,
            MatchedExistingCount: adopted, RecurringPaidCount: recurring,
            BalanceDelta: applied, NewBalance: account.CurrentBalance);
    }

    public async Task<ImportResultDto> CreateAndImportAsync(NewAccountFromStatementDto info, Stream stream, CancellationToken ct = default)
    {
        var statement = _parser.Parse(stream);

        // Don't materialize a phantom account from an empty/unparseable file.
        if (statement.Transactions.Count == 0)
        {
            Debug.WriteLine("[Import] CreateAndImport aborted: file parsed to 0 transactions; no account created.");
            return new ImportResultDto(StatementImportMode.Merge, 0, 0, 0);
        }

        // Don't mint a second account sharing an external id already in use — leave the new one
        // unlinked so the existing account stays the canonical match. Rows still import here.
        var externalId = string.IsNullOrWhiteSpace(info.ExternalAccountId) ? null : info.ExternalAccountId.Trim();
        if (externalId is not null &&
            await _db.Accounts.AsNoTracking().AnyAsync(a => a.ExternalAccountId == externalId, ct))
            externalId = null;

        // The statement balance (when the bank exports a real one) is as-of the statement end,
        // i.e. it already includes every row being imported — so it seeds CurrentBalance as-is.
        var account = new Account
        {
            Name = string.IsNullOrWhiteSpace(info.Name) ? "Imported account" : info.Name.Trim(),
            Type = info.Type,
            OpeningBalance = info.OpeningBalance,
            CurrentBalance = info.OpeningBalance,
            ExternalAccountId = externalId,
            BankId = externalId is null ? null : (string.IsNullOrWhiteSpace(info.BankId) ? null : info.BankId.Trim()),
        };
        _db.Accounts.Add(account);

        // Account.Id is client-generated (Guid.NewGuid()), so it is known before saving. Stage the
        // rows and persist account + transactions in ONE SaveChanges — atomic, so a mid-import
        // failure can never leave an orphan empty account behind.
        int imported = 0, skipped = 0;
        DateOnly? first = null, last = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in statement.Transactions)
        {
            if (!seen.Add(t.FitId))
            {
                skipped++;
                continue;
            }
            _db.Transactions.Add(new Transaction
            {
                Date = t.Date,
                AccountId = account.Id,
                Amount = t.Amount,
                Direction = t.Direction,
                Note = Truncate(t.Description, 500),
                ExternalId = t.FitId,
                ImportSource = "ofx",
            });
            if (first is null || t.Date < first) first = t.Date;
            if (last is null || t.Date > last) last = t.Date;
            imported++;
        }
        await _db.SaveChangesAsync(ct);

        Debug.WriteLine($"[Import] created account={account.Id} name='{account.Name}' " +
                        $"imported={imported} skipped={skipped} openingBalance={account.OpeningBalance}");

        return new ImportResultDto(StatementImportMode.Merge, imported, skipped, 0, first, last, account.Id,
            NewBalance: account.CurrentBalance);
    }

    // Classifies every statement row: duplicate, reconcile-into-existing, pays-a-recurring-rule,
    // or plain new — plus the balance effect of each. Pure read; Commit applies the plans.
    // accountId null = previewing a not-yet-created account (nothing to match against).
    private async Task<Analysis> AnalyzeAsync(Guid? accountId, ParsedStatement statement, StatementImportMode mode, CancellationToken ct)
    {
        var rows = statement.Transactions;

        var existingFitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nearby = new List<TxnSnapshot>();
        var rules = new List<Item>();
        decimal deletedNet = 0m;
        int deleteCount = 0;

        if (accountId is Guid id)
        {
            if (mode == StatementImportMode.Replace)
            {
                // Everything gets deleted before inserting, so nothing existing can be a
                // duplicate or a match target — but its net must come off the balance.
                var doomed = await _db.Transactions.AsNoTracking()
                    .Where(t => t.AccountId == id)
                    .Select(t => new { t.Amount, t.Direction })
                    .ToListAsync(ct);
                deleteCount = doomed.Count;
                deletedNet = doomed.Sum(t => Signed(t.Direction, t.Amount));
            }
            else
            {
                existingFitIds = await LoadExistingFitIdsAsync(id, ct);
            }

            if (rows.Count > 0)
            {
                var minDate = rows.Min(r => r.Date).AddDays(-2 * TransactionMatcher.OccurrenceWindowDays);
                var maxDate = rows.Max(r => r.Date).AddDays(2 * TransactionMatcher.OccurrenceWindowDays);

                if (mode == StatementImportMode.Merge)
                {
                    nearby = await _db.Transactions.AsNoTracking()
                        .Where(t => t.AccountId == id && t.Date >= minDate && t.Date <= maxDate)
                        .Select(t => new TxnSnapshot(t.Id, t.Date, t.Amount, t.Direction, t.Note, t.ExternalId, t.ItemId, t.ImportSource))
                        .ToListAsync(ct);
                }

                rules = await _db.Items.AsNoTracking()
                    .Where(s => !s.IsArchived && s.AccountId == id)
                    .ToListAsync(ct);
            }
        }

        var seenFitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedTxns = new HashSet<Guid>();               // each existing row matches at most once
        var claimedOccurrences = new HashSet<(Guid, DateOnly)>(); // each rule occurrence paid at most once
        var plans = new List<RowPlan>(rows.Count);

        // Chronological order so greedy claiming is deterministic and prefers earlier rows.
        foreach (var row in rows.OrderBy(r => r.Date).ThenBy(r => r.FitId, StringComparer.OrdinalIgnoreCase))
        {
            if (existingFitIds.Contains(row.FitId) || !seenFitIds.Add(row.FitId))
            {
                plans.Add(new RowPlan(row, PlanKind.Duplicate, null, null, null, null, 0m));
                continue;
            }

            var rowText = row.Name + " " + row.Memo;
            var signed = Signed(row.Direction, row.Amount);

            // 1) Reconcile into a transaction the user already recorded: exact amount+direction,
            //    posted within the date window, similar title. Already counted in the balance.
            //    Only genuinely manual rows qualify (ImportSource null) — synthetic rows like
            //    balance-reconciliation adjustments never correspond to a bank posting.
            var adopt = nearby
                .Where(t => t.ExternalId is null
                            && t.ImportSource is null
                            && !claimedTxns.Contains(t.Id)
                            && t.Amount == row.Amount
                            && t.Direction == row.Direction
                            && Math.Abs(t.Date.DayNumber - row.Date.DayNumber) <= TransactionMatcher.AdoptDateWindowDays)
                .Select(t => (Txn: t, Score: TransactionMatcher.Similarity(t.Note, rowText)))
                .Where(x => x.Score >= 0.5)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Math.Abs(x.Txn.Date.DayNumber - row.Date.DayNumber))
                .ThenBy(x => x.Txn.Id)
                .Select(x => x.Txn)
                .FirstOrDefault();
            if (adopt is not null)
            {
                claimedTxns.Add(adopt.Id);
                plans.Add(new RowPlan(row, PlanKind.Adopt, adopt.Id, null, null, adopt.Note, 0m));
                continue;
            }

            // 2) Pay a recurring rule occurrence: name matches the rule, amount within
            //    tolerance, and a projected occurrence date is close by.
            var best = default(RowPlan);
            var bestOcc = default(DateOnly);
            var bestKey = (Score: 0d, AmountDist: decimal.MaxValue, DateDist: int.MaxValue);
            foreach (var rule in rules)
            {
                if (rule.Direction != row.Direction) continue;
                var score = TransactionMatcher.Similarity(rule.Name, rowText);
                if (score < 0.5) continue;
                // Bills drift (fees, price changes) — allow 25% or $5, whichever is larger.
                var amountDist = Math.Abs(row.Amount - rule.Amount);
                if (amountDist > Math.Max(5m, rule.Amount * 0.25m)) continue;

                var occ = RecurrenceSchedule
                    .EnumeratePayDates(rule,
                        row.Date.AddDays(-2 * TransactionMatcher.OccurrenceWindowDays),
                        row.Date.AddDays(2 * TransactionMatcher.OccurrenceWindowDays))
                    .Where(p => Math.Abs(p.DayNumber - row.Date.DayNumber) <= TransactionMatcher.OccurrenceWindowDays)
                    .Where(p => !claimedOccurrences.Contains((rule.Id, p)))
                    .OrderBy(p => Math.Abs(p.DayNumber - row.Date.DayNumber))
                    .Cast<DateOnly?>()
                    .FirstOrDefault();
                if (occ is not DateOnly occurrence) continue;

                // Is the occurrence already paid by a transaction linked to this rule?
                var occupier = nearby.FirstOrDefault(t => t.ItemId == rule.Id
                    && Math.Abs(t.Date.DayNumber - occurrence.DayNumber) <= TransactionMatcher.OccurrenceWindowDays);

                RowPlan candidate;
                if (occupier is null)
                {
                    candidate = new RowPlan(row, PlanKind.RecurringNew, null, rule.Id, rule.CategoryId, rule.Name, signed);
                }
                else if (occupier.ExternalId is null && !claimedTxns.Contains(occupier.Id))
                {
                    // Confirmed ahead of time with an estimated amount — the bank row corrects
                    // it, so the balance moves by the difference only.
                    var delta = signed - Signed(occupier.Direction, occupier.Amount);
                    candidate = new RowPlan(row, PlanKind.RecurringUpdate, occupier.Id, rule.Id, null, rule.Name, delta);
                }
                else
                {
                    continue; // occurrence already reconciled with a bank row — not ours
                }

                var dateDist = Math.Abs(occurrence.DayNumber - row.Date.DayNumber);
                if (best is null || score > bestKey.Score
                    || (score == bestKey.Score && (amountDist < bestKey.AmountDist
                    || (amountDist == bestKey.AmountDist && dateDist < bestKey.DateDist))))
                {
                    best = candidate;
                    bestOcc = occurrence;
                    bestKey = (score, amountDist, dateDist);
                }
            }
            if (best is not null)
            {
                claimedOccurrences.Add((best.RuleId!.Value, bestOcc));
                if (best.TargetTransactionId is Guid target) claimedTxns.Add(target);
                plans.Add(best);
                continue;
            }

            // 3) Plain new transaction.
            plans.Add(new RowPlan(row, PlanKind.New, null, null, null, null, signed));
        }

        return new Analysis(plans, deletedNet, deleteCount);
    }

    // Record the statement's bank-account identity on an account that has none yet, so future
    // imports auto-match. Never overwrites an existing link, and won't stamp an id already used
    // by another account (which would make matching ambiguous).
    private async Task StampIdentityAsync(Account account, ParsedStatement statement, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(account.ExternalAccountId)) return;
        if (string.IsNullOrWhiteSpace(statement.AccountId)) return;

        var acctId = statement.AccountId;
        var alreadyLinked = await _db.Accounts.AsNoTracking()
            .AnyAsync(a => a.Id != account.Id && a.ExternalAccountId == acctId, ct);
        if (alreadyLinked) return; // keep the existing account as the canonical match

        account.ExternalAccountId = acctId;
        account.BankId = string.IsNullOrWhiteSpace(statement.BankId) ? null : statement.BankId;
    }

    private async Task<HashSet<string>> LoadExistingFitIdsAsync(Guid accountId, CancellationToken ct)
    {
        var ids = await _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.ExternalId != null)
            .Select(t => t.ExternalId!)
            .ToListAsync(ct);
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    private static AccountType MapAccountType(string? ofxType) => ofxType?.Trim().ToUpperInvariant() switch
    {
        "CHECKING" => AccountType.Checking,
        "SAVINGS" => AccountType.Savings,
        "MONEYMRKT" or "MONEYMARKET" => AccountType.Savings,
        "CREDITLINE" or "CREDITCARD" or "CC" => AccountType.CreditCard,
        _ => AccountType.Checking, // OFX bank exports are overwhelmingly checking; user can change it
    };

    // Show only the last 3 digits of the account number, e.g. "••633".
    private static string? Mask(string? acctId)
    {
        if (string.IsNullOrWhiteSpace(acctId)) return null;
        var id = acctId.Trim();
        return "••" + (id.Length <= 3 ? id : id[^3..]);
    }

    private static string SuggestedName(AccountType type, string? maskedId)
    {
        var label = type == AccountType.CreditCard ? "Credit card" : type.ToString();
        return maskedId is null ? label : $"{label} {maskedId}";
    }

    private static decimal Signed(TransactionDirection direction, decimal amount)
        => direction == TransactionDirection.Income ? amount : -amount;

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
