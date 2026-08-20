using System.Globalization;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Money that actually moved between two members of a group. Payments are
/// netted against the expense shares by <see cref="BalanceService"/>.
/// </summary>
public sealed class PaymentService(AppDbContext db, HistoryService history)
{
    /// <summary>
    /// Paged payment list. Sort keys: date, date_desc (default), amount,
    /// amount_desc, from, from_desc, to, to_desc.
    /// </summary>
    public async Task<PagedResult<Payment>> ListPaymentsAsync(
        long groupId,
        string? search = null,
        long? memberUserId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var query = db.Payments
            .AsNoTracking()
            .Include(x => x.FromUser)
            .Include(x => x.ToUser)
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.Note != null && EF.Functions.Like(x.Note, "%" + term + "%"))
                || (x.FromUser != null && EF.Functions.Like(x.FromUser.DisplayName, "%" + term + "%"))
                || (x.ToUser != null && EF.Functions.Like(x.ToUser.DisplayName, "%" + term + "%")));
        }

        if (memberUserId is > 0)
        {
            query = query.Where(x => x.FromUserId == memberUserId.Value || x.ToUserId == memberUserId.Value);
        }

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(x => x.PaymentDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.PaymentDate <= to);
        }

        query = sort switch
        {
            "date" => query.OrderBy(x => x.PaymentDate).ThenBy(x => x.Id),
            "amount" => query.OrderBy(x => x.AmountCents),
            "amount_desc" => query.OrderByDescending(x => x.AmountCents),
            "from" => query.OrderBy(x => x.FromUser!.DisplayName),
            "from_desc" => query.OrderByDescending(x => x.FromUser!.DisplayName),
            "to" => query.OrderBy(x => x.ToUser!.DisplayName),
            "to_desc" => query.OrderByDescending(x => x.ToUser!.DisplayName),
            _ => query.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Payment>(items, safePage, safeSize, total);
    }

    public async Task<Payment?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        if (paymentId <= 0)
        {
            return null;
        }

        return await db.Payments
            .AsNoTracking()
            .Include(x => x.FromUser)
            .Include(x => x.ToUser)
            .FirstOrDefaultAsync(x => x.Id == paymentId && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    /// <summary>Inserts or updates a payment. Id = 0 inserts.</summary>
    public async Task<Result<Payment>> SavePaymentAsync(PaymentEditModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var validation = await ValidateAsync(model, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Fail<Payment>(validation.Error!);
        }

        var isInsert = model.Id <= 0;

        Payment payment;
        if (isInsert)
        {
            payment = new Payment { GroupId = model.GroupId, UpdateState = UpdateState.Created };
            db.Payments.Add(payment);
        }
        else
        {
            var existing = await db.Payments.FirstOrDefaultAsync(
                x => x.Id == model.Id && x.UpdateState != UpdateState.Deleted, cancellationToken);

            if (existing is null)
            {
                return Result.Fail<Payment>("Die Zahlung wurde nicht gefunden.");
            }

            // A payment never changes its group: otherwise the id of a payment
            // of another group could be overwritten with members of this one.
            if (existing.GroupId != model.GroupId)
            {
                return Result.Fail<Payment>("Die Zahlung gehört nicht zu dieser Gruppe.");
            }

            payment = existing;
            payment.UpdateState = UpdateState.Updated;
        }

        payment.FromUserId = model.FromUserId;
        payment.ToUserId = model.ToUserId;
        payment.AmountCents = model.AmountCents;
        payment.PaymentDate = DateTime.SpecifyKind(model.PaymentDate.Date, DateTimeKind.Utc);
        payment.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();

        var currency = await GetCurrencyAsync(model.GroupId, cancellationToken);

        await history.LogAsync(
            model.GroupId,
            null,   // acting user, resolved by HistoryService (not the payer)
            HistoryService.EntityTypes.Payment,
            isInsert ? null : payment.Id,
            isInsert ? HistoryService.Actions.Created : HistoryService.Actions.Updated,
            $"Zahlung über {FormatAmount(payment.AmountCents, currency)} wurde "
                + (isInsert ? "erfasst." : "geändert."),
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(payment);
    }

    public async Task<Result> SoftDeletePaymentAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(
            x => x.Id == paymentId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (payment is null)
        {
            return Result.Fail("Die Zahlung wurde nicht gefunden.");
        }

        payment.UpdateState = UpdateState.Deleted;

        var currency = await GetCurrencyAsync(payment.GroupId, cancellationToken);

        await history.LogAsync(
            payment.GroupId,
            null,   // acting user, resolved by HistoryService
            HistoryService.EntityTypes.Payment,
            payment.Id,
            HistoryService.Actions.Deleted,
            $"Zahlung über {FormatAmount(payment.AmountCents, currency)} wurde gelöscht.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Sum of all active payments of a group.</summary>
    public async Task<long> GetTotalCentsAsync(long groupId, CancellationToken cancellationToken = default)
    {
        return await db.Payments
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .SumAsync(x => x.AmountCents, cancellationToken);
    }

    /// <summary>Currency of the group, EUR when the group has none.</summary>
    private async Task<string> GetCurrencyAsync(long groupId, CancellationToken cancellationToken)
    {
        var currency = await db.Groups
            .Where(x => x.Id == groupId)
            .Select(x => x.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(currency) ? "EUR" : currency;
    }

    private static string FormatAmount(long cents, string currency)
        => string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{cents / 100m:N2} {currency}");

    private async Task<Result> ValidateAsync(PaymentEditModel model, CancellationToken cancellationToken)
    {
        if (model.AmountCents <= 0)
        {
            return Result.Fail("Der Betrag muss größer als 0 sein.");
        }

        if (model.FromUserId == model.ToUserId)
        {
            return Result.Fail("Zahler und Empfänger dürfen nicht identisch sein.");
        }

        var groupExists = await db.Groups.AnyAsync(
            x => x.Id == model.GroupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (!groupExists)
        {
            return Result.Fail("Die Gruppe wurde nicht gefunden.");
        }

        var memberIds = await db.GroupMembers
            .Where(x => x.GroupId == model.GroupId && x.UpdateState != UpdateState.Deleted)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        if (!memberIds.Contains(model.FromUserId) || !memberIds.Contains(model.ToUserId))
        {
            return Result.Fail("Zahler und Empfänger müssen Mitglieder dieser Gruppe sein.");
        }

        return Result.Ok();
    }
}
