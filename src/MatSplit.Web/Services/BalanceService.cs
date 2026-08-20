using MatSplit.Web.Data;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Turns expenses, expense shares and payments of a group into per-member
/// balances and a minimal set of settlement transfers.
/// All arithmetic happens in whole cents; splitting uses the largest remainder
/// method so the distributed cents always add up to the expense total.
/// </summary>
public sealed class BalanceService(AppDbContext db)
{
    /// <summary>
    /// Calculates the complete balance snapshot of a group.
    /// Positive balance = the member gets money back.
    /// </summary>
    public async Task<BalanceResult> CalculateBalancesAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return new BalanceResult { GroupId = groupId };
        }

        var members = await db.GroupMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .ToListAsync(cancellationToken);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .Select(x => new
            {
                x.Id,
                x.AmountCents,
                x.PaidByUserId,
                Shares = x.Shares
                    .Where(s => s.UpdateState != UpdateState.Deleted)
                    .Select(s => new { s.UserId, s.ShareFactor, s.ShareAmountCents })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var payments = await db.Payments
            .AsNoTracking()
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .Select(x => new { x.FromUserId, x.ToUserId, x.AmountCents })
            .ToListAsync(cancellationToken);

        var shareFactors = members.ToDictionary(x => x.UserId, x => Math.Max(1, x.ShareFactor));
        var displayNames = members
            .Where(x => x.User is not null)
            .ToDictionary(x => x.UserId, x => x.User!.DisplayName);

        var payPalAddresses = members
            .Where(x => x.User is not null)
            .ToDictionary(x => x.UserId, x => x.User!.PayPalAddress);

        var expensesPaid = new Dictionary<long, long>();
        var owed = new Dictionary<long, long>();
        var paymentsSent = new Dictionary<long, long>();
        var paymentsReceived = new Dictionary<long, long>();

        // Default split when an expense carries no explicit shares.
        var defaultShares = members
            .Select(m => new ExpenseShareInput { UserId = m.UserId, ShareFactor = Math.Max(1, m.ShareFactor) })
            .ToList();

        long totalExpenses = 0;

        foreach (var expense in expenses)
        {
            totalExpenses += expense.AmountCents;
            Add(expensesPaid, expense.PaidByUserId, expense.AmountCents);

            var shares = expense.Shares.Count > 0
                ? expense.Shares
                    .Select(s => new ExpenseShareInput
                    {
                        UserId = s.UserId,
                        ShareFactor = Math.Max(1, s.ShareFactor),
                        ShareAmountCents = s.ShareAmountCents
                    })
                    .ToList()
                : defaultShares;

            foreach (var (userId, amount) in Distribute(expense.AmountCents, shares))
            {
                Add(owed, userId, amount);
            }
        }

        long totalPayments = 0;
        foreach (var payment in payments)
        {
            totalPayments += payment.AmountCents;
            Add(paymentsSent, payment.FromUserId, payment.AmountCents);
            Add(paymentsReceived, payment.ToUserId, payment.AmountCents);
        }

        var userIds = new HashSet<long>(shareFactors.Keys);
        userIds.UnionWith(expensesPaid.Keys);
        userIds.UnionWith(owed.Keys);
        userIds.UnionWith(paymentsSent.Keys);
        userIds.UnionWith(paymentsReceived.Keys);

        await FillMissingUsersAsync(userIds, displayNames, payPalAddresses, cancellationToken);

        var balances = userIds
            .Select(userId =>
            {
                var paid = Get(expensesPaid, userId) + Get(paymentsSent, userId);
                var owes = Get(owed, userId) + Get(paymentsReceived, userId);

                return new MemberBalance
                {
                    UserId = userId,
                    DisplayName = displayNames.TryGetValue(userId, out var name) ? name : $"Benutzer {userId}",
                    ShareFactor = shareFactors.TryGetValue(userId, out var factor) ? factor : 0,
                    PaidCents = paid,
                    OwedCents = owes,
                    BalanceCents = paid - owes,
                    ExpensesPaidCents = Get(expensesPaid, userId),
                    PaymentsSentCents = Get(paymentsSent, userId),
                    PaymentsReceivedCents = Get(paymentsReceived, userId)
                };
            })
            .OrderByDescending(x => x.BalanceCents)
            .ThenBy(x => x.DisplayName)
            .ToList();

        var settlements = BuildSettlements(balances, payPalAddresses, group.Currency);

        return new BalanceResult
        {
            GroupId = groupId,
            Currency = group.Currency,
            TotalExpensesCents = totalExpenses,
            TotalPaymentsCents = totalPayments,
            Balances = balances,
            Settlements = settlements
        };
    }

    /// <summary>
    /// Splits an amount across shares. Fixed shares are honoured first, the
    /// rest is distributed by factor using the largest remainder method.
    /// </summary>
    public static IReadOnlyList<(long UserId, long AmountCents)> Distribute(long amountCents, IReadOnlyList<ExpenseShareInput> shares)
    {
        if (shares.Count == 0 || amountCents == 0)
        {
            return [];
        }

        var result = new Dictionary<long, long>();

        var fixedShares = shares.Where(s => s.ShareAmountCents.HasValue).ToList();
        var factorShares = shares.Where(s => !s.ShareAmountCents.HasValue).ToList();

        long fixedTotal = 0;
        foreach (var share in fixedShares)
        {
            var value = share.ShareAmountCents!.Value;
            Add(result, share.UserId, value);
            fixedTotal += value;
        }

        var remaining = amountCents - fixedTotal;

        if (factorShares.Count == 0)
        {
            // Only fixed shares: push any rounding difference onto the first line.
            if (remaining != 0 && fixedShares.Count > 0)
            {
                Add(result, fixedShares[0].UserId, remaining);
            }

            return [.. result.Select(x => (x.Key, x.Value))];
        }

        if (remaining <= 0)
        {
            foreach (var share in factorShares)
            {
                Add(result, share.UserId, 0);
            }

            // Fixed shares that already exceed the total would make the sum of
            // all shares differ from the expense amount, which would break the
            // "every cent is assigned exactly once" invariant of the balance.
            // The overhang is corrected on the first fixed line.
            if (remaining < 0 && fixedShares.Count > 0)
            {
                Add(result, fixedShares[0].UserId, remaining);
            }

            return [.. result.Select(x => (x.Key, x.Value))];
        }

        var totalFactor = factorShares.Sum(x => (long)Math.Max(1, x.ShareFactor));
        if (totalFactor <= 0)
        {
            totalFactor = factorShares.Count;
        }

        var portions = new List<(long UserId, long Base, long Remainder)>(factorShares.Count);
        long distributed = 0;

        foreach (var share in factorShares)
        {
            var factor = Math.Max(1, share.ShareFactor);
            var exact = remaining * factor;
            var baseAmount = exact / totalFactor;
            var remainder = exact % totalFactor;

            portions.Add((share.UserId, baseAmount, remainder));
            distributed += baseAmount;
        }

        var leftover = remaining - distributed;

        // Largest remainder first, deterministic tie break by user id.
        foreach (var portion in portions.OrderByDescending(x => x.Remainder).ThenBy(x => x.UserId))
        {
            var extra = leftover > 0 ? 1 : 0;
            leftover -= extra;
            Add(result, portion.UserId, portion.Base + extra);
        }

        return [.. result.Select(x => (x.Key, x.Value))];
    }

    /// <summary>
    /// Greedy minimal settlement: repeatedly move money from the biggest debtor
    /// to the biggest creditor.
    /// </summary>
    private static List<Settlement> BuildSettlements(
        IReadOnlyList<MemberBalance> balances,
        IReadOnlyDictionary<long, string?> payPalAddresses,
        string currency)
    {
        var creditors = balances
            .Where(x => x.BalanceCents > 0)
            .Select(x => new MutableBalance(x.UserId, x.DisplayName, x.BalanceCents))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var debtors = balances
            .Where(x => x.BalanceCents < 0)
            .Select(x => new MutableBalance(x.UserId, x.DisplayName, -x.BalanceCents))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var settlements = new List<Settlement>();
        var creditorIndex = 0;
        var debtorIndex = 0;
        var guard = 0;
        var maxIterations = (creditors.Count + debtors.Count + 1) * 4;

        while (creditorIndex < creditors.Count && debtorIndex < debtors.Count && guard++ < maxIterations)
        {
            var creditor = creditors[creditorIndex];
            var debtor = debtors[debtorIndex];
            var amount = Math.Min(creditor.Amount, debtor.Amount);

            if (amount <= 0)
            {
                if (creditor.Amount <= 0)
                {
                    creditorIndex++;
                }

                if (debtor.Amount <= 0)
                {
                    debtorIndex++;
                }

                continue;
            }

            payPalAddresses.TryGetValue(creditor.UserId, out var payPalAddress);

            settlements.Add(new Settlement
            {
                FromUserId = debtor.UserId,
                FromDisplayName = debtor.DisplayName,
                ToUserId = creditor.UserId,
                ToDisplayName = creditor.DisplayName,
                AmountCents = amount,
                PayPalUrl = PayPalLinkBuilder.BuildLink(payPalAddress, amount, currency)
            });

            creditor.Amount -= amount;
            debtor.Amount -= amount;

            if (creditor.Amount == 0)
            {
                creditorIndex++;
            }

            if (debtor.Amount == 0)
            {
                debtorIndex++;
            }
        }

        return settlements;
    }

    private async Task FillMissingUsersAsync(
        HashSet<long> userIds,
        Dictionary<long, string> displayNames,
        Dictionary<long, string?> payPalAddresses,
        CancellationToken cancellationToken)
    {
        var missing = userIds.Where(id => !displayNames.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var users = await db.Users
            .AsNoTracking()
            .Where(x => missing.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName, x.PayPalAddress })
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            displayNames[user.Id] = user.DisplayName;
            payPalAddresses[user.Id] = user.PayPalAddress;
        }
    }

    private static void Add(Dictionary<long, long> target, long key, long value)
    {
        target[key] = target.TryGetValue(key, out var current) ? current + value : value;
    }

    private static long Get(Dictionary<long, long> source, long key)
        => source.TryGetValue(key, out var value) ? value : 0;

    private sealed class MutableBalance(long userId, string displayName, long amount)
    {
        public long UserId { get; } = userId;

        public string DisplayName { get; } = displayName;

        public long Amount { get; set; } = amount;
    }
}
