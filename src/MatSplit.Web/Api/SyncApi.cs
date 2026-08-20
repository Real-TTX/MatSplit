using System.Text.Json;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Api;

/// <summary>
/// Minimal API surface used by the PWA (wwwroot/js/offline-sync.js) to flush
/// expenses and payments that were captured while the device was offline.
/// The signature of <see cref="MapSyncApi"/> is a fixed contract - Program.cs
/// only calls this one method.
/// <para>
/// Idempotency: every queued entry carries a client generated
/// <c>clientId</c> (uuid). After a successful write the endpoint logs an extra
/// history entry with action <see cref="SyncAction"/> that contains the
/// clientId both in <c>Summary</c> (searchable through
/// <see cref="HistoryService.ListHistoryAsync"/>) and in <c>DetailsJson</c>.
/// A replay of the same clientId therefore returns the already stored entity
/// id instead of creating a duplicate. Nothing is written to
/// <c>Expenses.Category</c>.
/// </para>
/// <para>Conflicts always resolve in favour of the server: a rejected item is
/// reported back with its German error message and stays in the client
/// outbox.</para>
/// </summary>
public static class SyncApi
{
    public const string RoutePrefix = "/api/sync";

    /// <summary>History action used as the idempotency marker.</summary>
    public const string SyncAction = "Synced";

    private const int MaxBatchSize = 200;
    private const int MaxClientIdLength = 64;
    private const int DuplicateLookupPageSize = 10;

    /// <summary>
    /// Registers every sync endpoint. Everything below /api/sync requires an
    /// authenticated user, the ping endpoint included.
    /// </summary>
    public static void MapSyncApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(RoutePrefix)
            .RequireAuthorization(MatSplitClaims.AuthenticatedUserPolicy)
            .WithTags("Sync");

        group.MapGet("/ping", (CurrentUserService currentUser) => Results.Ok(new
        {
            status = "ok",
            utc = DateTime.UtcNow,
            userId = currentUser.UserId,
            displayName = currentUser.DisplayName
        }))
        .WithName("SyncPing");

        group.MapGet("/status", GetStatusAsync).WithName("SyncStatus");
        group.MapPost("/expenses", PostExpensesAsync).WithName("SyncExpenses");
        group.MapPost("/payments", PostPaymentsAsync).WithName("SyncPayments");
    }

    // -----------------------------------------------------------------------
    // GET /status - everything the offline forms need to stay usable.
    // -----------------------------------------------------------------------

    private static async Task<IResult> GetStatusAsync(
        CurrentUserService currentUser,
        GroupService groups,
        AppConfigService appConfig,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var config = await appConfig.GetAsync(cancellationToken);
        var myGroups = await groups.ListGroupsForUserAsync(userId.Value, cancellationToken: cancellationToken);
        var payload = new List<object>(myGroups.Count);

        foreach (var group in myGroups)
        {
            var members = await groups.ListMembersAsync(group.Id, cancellationToken);

            payload.Add(new
            {
                id = group.Id,
                token = group.Token,
                name = group.Name,
                currency = group.Currency,
                members = members.Select(member => new
                {
                    userId = member.UserId,
                    displayName = member.User?.DisplayName ?? "Unbekannt",
                    shareFactor = member.ShareFactor,
                    isGroupAdmin = member.IsGroupAdmin
                }).ToList()
            });
        }

        return Results.Ok(new
        {
            status = "ok",
            utc = DateTime.UtcNow,
            userId = userId.Value,
            displayName = currentUser.DisplayName,
            role = currentUser.Role.ToString(),
            isAnonymous = currentUser.IsAnonymousUser,
            defaultCurrency = config.DefaultCurrency,
            maxReceiptSizeMb = config.MaxReceiptSizeMb,
            groups = payload
        });
    }

    // -----------------------------------------------------------------------
    // POST /expenses
    // -----------------------------------------------------------------------

    /// <summary>
    /// Accepts a batch of offline captured expenses and writes them through
    /// <see cref="ExpenseService"/>. Every item is reported back individually so
    /// the client can keep the failed ones in its outbox.
    /// </summary>
    private static async Task<IResult> PostExpensesAsync(
        List<SyncExpenseDto> expenses,
        ExpenseService expenseService,
        HistoryService history,
        CurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (expenses is null || expenses.Count == 0)
        {
            return Results.BadRequest(new { error = "Es wurden keine Ausgaben übermittelt." });
        }

        if (expenses.Count > MaxBatchSize)
        {
            return Results.BadRequest(new { error = $"Es sind maximal {MaxBatchSize} Eintraege pro Anfrage erlaubt." });
        }

        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var response = new SyncExpenseResponseDto();

        foreach (var dto in expenses)
        {
            var itemResult = new SyncExpenseResultDto { ClientId = dto.ClientId };
            var clientId = NormalizeClientId(dto.ClientId);

            if (dto.ClientId is not null && clientId is null)
            {
                Reject(response, itemResult, "Die ClientId hat ein unerlaubtes Format.");
                continue;
            }

            if (!await currentUser.CanViewGroupAsync(dto.GroupId, cancellationToken))
            {
                Reject(response, itemResult, "Kein Zugriff auf diese Gruppe.");
                continue;
            }

            // Replay of an entry that already made it through - answer with the
            // stored id so the client can drop it from its outbox.
            var duplicate = await FindSyncedEntityIdAsync(
                history, dto.GroupId, HistoryService.EntityTypes.Expense, clientId, cancellationToken);

            if (duplicate is not null)
            {
                itemResult.Success = true;
                itemResult.ExpenseId = duplicate.Value;
                response.Accepted++;
                response.Results.Add(itemResult);
                continue;
            }

            var model = new ExpenseEditModel
            {
                Id = 0,
                GroupId = dto.GroupId,
                Description = dto.Description,
                AmountCents = dto.AmountCents,
                Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "EUR" : dto.Currency,
                PaidByUserId = dto.PaidByUserId > 0 ? dto.PaidByUserId : userId.Value,
                ExpenseDate = dto.ExpenseDate ?? DateTime.UtcNow.Date,
                Category = dto.Category,
                Shares = dto.Shares
                    .Select(share => new ExpenseShareInput
                    {
                        UserId = share.UserId,
                        ShareFactor = share.ShareFactor,
                        ShareAmountCents = share.ShareAmountCents
                    })
                    .ToList()
            };

            var saved = await expenseService.SaveExpenseAsync(model, cancellationToken);
            if (saved.IsFailure)
            {
                Reject(response, itemResult, saved.Error);
                continue;
            }

            await LogSyncMarkerAsync(
                history,
                dto.GroupId,
                userId.Value,
                HistoryService.EntityTypes.Expense,
                saved.Value.Id,
                clientId,
                "Ausgabe",
                cancellationToken);

            itemResult.Success = true;
            itemResult.ExpenseId = saved.Value.Id;
            response.Accepted++;
            response.Results.Add(itemResult);
        }

        return Results.Ok(new
        {
            accepted = response.Accepted,
            rejected = response.Rejected,
            results = response.Results,
            acceptedClientIds = CollectClientIds(response.Results, accepted: true),
            rejectedClientIds = CollectClientIds(response.Results, accepted: false)
        });
    }

    // -----------------------------------------------------------------------
    // POST /payments
    // -----------------------------------------------------------------------

    /// <summary>
    /// Same contract as <see cref="PostExpensesAsync"/> for payments
    /// ("wer hat wem wieviel gegeben").
    /// </summary>
    private static async Task<IResult> PostPaymentsAsync(
        List<SyncPaymentDto> payments,
        PaymentService paymentService,
        HistoryService history,
        CurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (payments is null || payments.Count == 0)
        {
            return Results.BadRequest(new { error = "Es wurden keine Zahlungen übermittelt." });
        }

        if (payments.Count > MaxBatchSize)
        {
            return Results.BadRequest(new { error = $"Es sind maximal {MaxBatchSize} Eintraege pro Anfrage erlaubt." });
        }

        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var accepted = 0;
        var rejected = 0;
        var items = new List<SyncPaymentResultDto>(payments.Count);

        foreach (var dto in payments)
        {
            var itemResult = new SyncPaymentResultDto { ClientId = dto.ClientId };
            var clientId = NormalizeClientId(dto.ClientId);

            if (dto.ClientId is not null && clientId is null)
            {
                itemResult.Error = "Die ClientId hat ein unerlaubtes Format.";
                rejected++;
                items.Add(itemResult);
                continue;
            }

            if (!await currentUser.CanViewGroupAsync(dto.GroupId, cancellationToken))
            {
                itemResult.Error = "Kein Zugriff auf diese Gruppe.";
                rejected++;
                items.Add(itemResult);
                continue;
            }

            var duplicate = await FindSyncedEntityIdAsync(
                history, dto.GroupId, HistoryService.EntityTypes.Payment, clientId, cancellationToken);

            if (duplicate is not null)
            {
                itemResult.Success = true;
                itemResult.PaymentId = duplicate.Value;
                accepted++;
                items.Add(itemResult);
                continue;
            }

            var model = new PaymentEditModel
            {
                Id = 0,
                GroupId = dto.GroupId,
                FromUserId = dto.FromUserId > 0 ? dto.FromUserId : userId.Value,
                ToUserId = dto.ToUserId,
                AmountCents = dto.AmountCents,
                PaymentDate = dto.PaymentDate ?? DateTime.UtcNow.Date,
                Note = dto.Note
            };

            var saved = await paymentService.SavePaymentAsync(model, cancellationToken);
            if (saved.IsFailure)
            {
                itemResult.Error = saved.Error;
                rejected++;
                items.Add(itemResult);
                continue;
            }

            await LogSyncMarkerAsync(
                history,
                dto.GroupId,
                userId.Value,
                HistoryService.EntityTypes.Payment,
                saved.Value.Id,
                clientId,
                "Zahlung",
                cancellationToken);

            itemResult.Success = true;
            itemResult.PaymentId = saved.Value.Id;
            accepted++;
            items.Add(itemResult);
        }

        return Results.Ok(new
        {
            accepted,
            rejected,
            results = items,
            acceptedClientIds = items.Where(x => x.Success).Select(x => x.ClientId).Where(x => x is not null).ToList(),
            rejectedClientIds = items.Where(x => !x.Success).Select(x => x.ClientId).Where(x => x is not null).ToList()
        });
    }

    // -----------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------

    private static void Reject(SyncExpenseResponseDto response, SyncExpenseResultDto item, string? error)
    {
        item.Success = false;
        item.Error = string.IsNullOrWhiteSpace(error) ? "Der Eintrag konnte nicht gespeichert werden." : error;
        response.Rejected++;
        response.Results.Add(item);
    }

    private static List<string?> CollectClientIds(IEnumerable<SyncExpenseResultDto> results, bool accepted)
        => results
            .Where(result => result.Success == accepted && result.ClientId is not null)
            .Select(result => result.ClientId)
            .ToList();

    /// <summary>
    /// Only ids that are safe to use inside a LIKE search are accepted.
    /// Returns null for a missing or malformed id.
    /// </summary>
    private static string? NormalizeClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var trimmed = clientId.Trim();
        if (trimmed.Length > MaxClientIdLength)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or ':'))
            {
                return null;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Looks for the idempotency marker of an earlier replay. Returns the id of
    /// the entity that was created back then, or null when this clientId is new.
    /// </summary>
    private static async Task<long?> FindSyncedEntityIdAsync(
        HistoryService history,
        long groupId,
        string entityType,
        string? clientId,
        CancellationToken cancellationToken)
    {
        if (clientId is null)
        {
            return null;   // no id, no deduplication (documented in docs/pwa.md)
        }

        var marker = await history.ListHistoryAsync(
            groupId,
            clientId,
            SyncAction,
            page: 1,
            pageSize: DuplicateLookupPageSize,
            cancellationToken: cancellationToken);

        foreach (var entry in marker.Items)
        {
            if (!string.Equals(entry.EntityType, entityType, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.EntityId is null)
            {
                continue;
            }

            var hit = entry.Summary.Contains(clientId, StringComparison.OrdinalIgnoreCase)
                || (entry.DetailsJson?.Contains(clientId, StringComparison.OrdinalIgnoreCase) ?? false);

            if (hit)
            {
                return entry.EntityId;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the idempotency marker. The clientId lands in the summary (that is
    /// what <see cref="HistoryService.ListHistoryAsync"/> can search) and in
    /// DetailsJson for machine readable access.
    /// </summary>
    private static async Task LogSyncMarkerAsync(
        HistoryService history,
        long groupId,
        long userId,
        string entityType,
        long entityId,
        string? clientId,
        string label,
        CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            source = "offline-sync",
            clientId,
            entityType,
            entityId,
            receivedUtc = DateTime.UtcNow
        });

        var summary = clientId is null
            ? $"{label} wurde offline erfasst und synchronisiert."
            : $"{label} wurde offline erfasst und synchronisiert (Sync-Id {clientId}).";

        await history.LogAsync(
            groupId,
            userId,
            entityType,
            entityId,
            SyncAction,
            summary,
            details,
            saveChanges: true,
            cancellationToken: cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Payment DTOs.
    // Deliberately declared here (nested) and not in Services/Models, because
    // that folder belongs to the core agent - SyncModels.cs only covers
    // expenses. The JSON shape mirrors the expense endpoint.
    // -----------------------------------------------------------------------

    /// <summary>One offline captured payment pushed to <c>POST /api/sync/payments</c>.</summary>
    public sealed class SyncPaymentDto
    {
        /// <summary>Client side idempotency key, may be null.</summary>
        public string? ClientId { get; set; }

        public long GroupId { get; set; }

        /// <summary>Payer. 0 falls back to the signed in user.</summary>
        public long FromUserId { get; set; }

        public long ToUserId { get; set; }

        public long AmountCents { get; set; }

        public DateTime? PaymentDate { get; set; }

        public string? Note { get; set; }
    }

    /// <summary>Per item outcome of a payment sync push.</summary>
    public sealed class SyncPaymentResultDto
    {
        public string? ClientId { get; set; }

        public long PaymentId { get; set; }

        public bool Success { get; set; }

        public string? Error { get; set; }
    }
}
