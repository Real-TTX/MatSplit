using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Presentation union of a group's expenses and payments into a single,
/// chronological transaction list for the group hub (/Groups/Details).
///
/// The underlying data is never merged: expenses stay in the <c>Expenses</c>
/// table and payments in the <c>Payments</c> table (and the balance logic keeps
/// reading them separately). This service only projects both onto a common
/// <see cref="TransactionRow"/>, sorts them newest first, optionally filters by
/// kind and pages the result. Filtering and paging happen in memory: both sets
/// are fully materialised first. That is fine at the self-hosted scale MatSplit
/// targets, but would need a keyset/database-side union for very large groups.
/// </summary>
public sealed class TransactionService(AppDbContext db)
{
    /// <summary>
    /// Builds the combined transaction list of a group.
    /// </summary>
    /// <param name="groupId">Group to load transactions for.</param>
    /// <param name="kind">
    /// Optional kind filter: <c>null</c> returns expenses and payments,
    /// otherwise only the matching kind.
    /// </param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page.</param>
    public async Task<PagedResult<TransactionRow>> ListTransactionsAsync(
        long groupId,
        TransactionKind? kind = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var rows = new List<SortableRow>();

        if (kind is null or TransactionKind.Expense)
        {
            var expenses = await db.Expenses
                .AsNoTracking()
                .Include(x => x.PaidByUser)
                .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
                .Select(x => new
                {
                    x.Id,
                    x.Description,
                    x.AmountCents,
                    x.ExpenseDate,
                    PayerName = x.PaidByUser != null ? x.PaidByUser.DisplayName : null
                })
                .ToListAsync(cancellationToken);

            foreach (var expense in expenses)
            {
                var title = string.IsNullOrWhiteSpace(expense.Description)
                    ? "Ausgabe"
                    : expense.Description;

                rows.Add(new SortableRow(
                    expense.ExpenseDate,
                    expense.Id,
                    new TransactionRow
                    {
                        Kind = TransactionKind.Expense,
                        Title = title,
                        Subtitle = $"bezahlt von {expense.PayerName ?? "Unbekannt"}",
                        AmountCents = expense.AmountCents,
                        Date = expense.ExpenseDate,
                        EditUrl = $"/Groups/Expenses/Edit?groupId={groupId}&id={expense.Id}",
                        Icon = "expense",
                        TypeLabel = "Ausgabe"
                    }));
            }
        }

        if (kind is null or TransactionKind.Payment)
        {
            var payments = await db.Payments
                .AsNoTracking()
                .Include(x => x.FromUser)
                .Include(x => x.ToUser)
                .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
                .Select(x => new
                {
                    x.Id,
                    x.Note,
                    x.AmountCents,
                    x.PaymentDate,
                    FromName = x.FromUser != null ? x.FromUser.DisplayName : null,
                    ToName = x.ToUser != null ? x.ToUser.DisplayName : null
                })
                .ToListAsync(cancellationToken);

            foreach (var payment in payments)
            {
                var title = string.IsNullOrWhiteSpace(payment.Note)
                    ? "Zahlung"
                    : payment.Note;

                rows.Add(new SortableRow(
                    payment.PaymentDate,
                    payment.Id,
                    new TransactionRow
                    {
                        Kind = TransactionKind.Payment,
                        Title = title,
                        Subtitle = $"{payment.FromName ?? "Unbekannt"} → {payment.ToName ?? "Unbekannt"}",
                        AmountCents = payment.AmountCents,
                        Date = payment.PaymentDate,
                        EditUrl = $"/Groups/Payments/Edit?groupId={groupId}&id={payment.Id}",
                        Icon = "paypal",
                        TypeLabel = "Zahlung"
                    }));
            }
        }

        // Newest first; ties broken by descending id so the ordering is stable.
        var ordered = rows
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Row)
            .ToList();

        var total = ordered.Count;

        var items = ordered
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToList();

        return new PagedResult<TransactionRow>(items, safePage, safeSize, total);
    }

    /// <summary>Internal carrier keeping the sort keys next to the projected row.</summary>
    private readonly record struct SortableRow(DateTime Date, long Id, TransactionRow Row);
}
