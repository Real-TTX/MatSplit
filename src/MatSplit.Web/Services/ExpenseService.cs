using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Expenses of a group plus their receipt images. Receipt binaries live on
/// disk under /data/receipts/&lt;groupId&gt;/&lt;expenseId&gt;/, only metadata is
/// stored in the database.
/// </summary>
public sealed class ExpenseService(
    AppDbContext db,
    HistoryService history,
    AppConfigService appConfig,
    MatSplitPaths paths,
    ILogger<ExpenseService> logger)
{
    private static readonly string[] AllowedReceiptExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".gif", ".pdf"];

    private static readonly string[] AllowedReceiptContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif", "image/gif", "application/pdf"];

    /// <summary>
    /// Paged expense list. Sort keys: date, date_desc (default), amount,
    /// amount_desc, description, description_desc, payer, payer_desc.
    /// </summary>
    public async Task<PagedResult<Expense>> ListExpensesAsync(
        long groupId,
        string? search = null,
        long? payerUserId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        // Shares and receipts come along: the list shows the participants and a
        // receipt marker per row. AsSplitQuery keeps that from turning into a
        // cartesian product of shares x receipts.
        var query = db.Expenses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.PaidByUser)
            .Include(x => x.Shares.Where(s => s.UpdateState != UpdateState.Deleted))
                .ThenInclude(s => s.User)
            .Include(x => x.Receipts.Where(r => r.UpdateState != UpdateState.Deleted))
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.Description, "%" + term + "%")
                || (x.Category != null && EF.Functions.Like(x.Category, "%" + term + "%")));
        }

        if (payerUserId is > 0)
        {
            query = query.Where(x => x.PaidByUserId == payerUserId.Value);
        }

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(x => x.ExpenseDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.ExpenseDate <= to);
        }

        query = sort switch
        {
            "date" => query.OrderBy(x => x.ExpenseDate).ThenBy(x => x.Id),
            "amount" => query.OrderBy(x => x.AmountCents),
            "amount_desc" => query.OrderByDescending(x => x.AmountCents),
            "description" => query.OrderBy(x => x.Description),
            "description_desc" => query.OrderByDescending(x => x.Description),
            "payer" => query.OrderBy(x => x.PaidByUser!.DisplayName),
            "payer_desc" => query.OrderByDescending(x => x.PaidByUser!.DisplayName),
            _ => query.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.Id)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Expense>(items, safePage, safeSize, total);
    }

    /// <summary>Loads an expense including shares (with users) and receipts.</summary>
    public async Task<Expense?> GetExpenseAsync(long expenseId, CancellationToken cancellationToken = default)
    {
        if (expenseId <= 0)
        {
            return null;
        }

        return await db.Expenses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.PaidByUser)
            .Include(x => x.Shares.Where(s => s.UpdateState != UpdateState.Deleted))
                .ThenInclude(s => s.User)
            .Include(x => x.Receipts.Where(r => r.UpdateState != UpdateState.Deleted))
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    /// <summary>Sum of all active expenses of a group.</summary>
    public async Task<long> GetTotalCentsAsync(long groupId, CancellationToken cancellationToken = default)
    {
        return await db.Expenses
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .SumAsync(x => x.AmountCents, cancellationToken);
    }

    /// <summary>Distinct categories of a group, for the filter dropdown.</summary>
    public async Task<IReadOnlyList<string>> ListCategoriesAsync(long groupId, CancellationToken cancellationToken = default)
    {
        return await db.Expenses
            .AsNoTracking()
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted && x.Category != null)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts or updates an expense together with its shares.
    /// An empty share list means "all current group members by group factor".
    /// </summary>
    public async Task<Result<Expense>> SaveExpenseAsync(ExpenseEditModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var validation = await ValidateAsync(model, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Fail<Expense>(validation.Error!);
        }

        var group = await db.Groups.AsNoTracking().FirstAsync(x => x.Id == model.GroupId, cancellationToken);
        var isInsert = model.Id <= 0;

        Expense expense;
        if (isInsert)
        {
            expense = new Expense { GroupId = model.GroupId, UpdateState = UpdateState.Created };
            db.Expenses.Add(expense);
        }
        else
        {
            var existing = await db.Expenses
                .Include(x => x.Shares)
                .FirstOrDefaultAsync(x => x.Id == model.Id && x.UpdateState != UpdateState.Deleted, cancellationToken);

            if (existing is null)
            {
                return Result.Fail<Expense>("Die Ausgabe wurde nicht gefunden.");
            }

            // An expense never changes its group. Without this check a caller
            // that may manage group A could pass the id of an expense of group B
            // and overwrite it with data (payer, shares) validated against A.
            if (existing.GroupId != model.GroupId)
            {
                return Result.Fail<Expense>("Die Ausgabe gehört nicht zu dieser Gruppe.");
            }

            expense = existing;
            expense.UpdateState = UpdateState.Updated;
        }

        expense.Description = model.Description.Trim();
        expense.AmountCents = model.AmountCents;
        expense.Currency = string.IsNullOrWhiteSpace(model.Currency)
            ? group.Currency
            : model.Currency.Trim().ToUpperInvariant();
        expense.PaidByUserId = model.PaidByUserId;
        expense.ExpenseDate = DateTime.SpecifyKind(model.ExpenseDate.Date, DateTimeKind.Utc);
        expense.Category = string.IsNullOrWhiteSpace(model.Category) ? null : model.Category.Trim();

        var shares = await ResolveSharesAsync(model, cancellationToken);
        await ApplySharesAsync(expense, shares, isInsert, cancellationToken);

        await history.LogAsync(
            model.GroupId,
            null,   // acting user, resolved by HistoryService (not the payer)
            HistoryService.EntityTypes.Expense,
            isInsert ? null : expense.Id,
            isInsert ? HistoryService.Actions.Created : HistoryService.Actions.Updated,
            $"Ausgabe \"{expense.Description}\" über {FormatAmount(expense.AmountCents, expense.Currency)} wurde "
                + (isInsert ? "erfasst." : "geändert."),
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(expense);
    }

    /// <summary>Soft deletes an expense including its shares and receipts.</summary>
    public async Task<Result> SoftDeleteExpenseAsync(long expenseId, CancellationToken cancellationToken = default)
    {
        var expense = await db.Expenses
            .AsSplitQuery()
            .Include(x => x.Shares)
            .Include(x => x.Receipts)
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (expense is null)
        {
            return Result.Fail("Die Ausgabe wurde nicht gefunden.");
        }

        expense.UpdateState = UpdateState.Deleted;

        foreach (var share in expense.Shares)
        {
            share.UpdateState = UpdateState.Deleted;
        }

        foreach (var receipt in expense.Receipts)
        {
            receipt.UpdateState = UpdateState.Deleted;
        }

        await history.LogAsync(
            expense.GroupId,
            null,
            HistoryService.EntityTypes.Expense,
            expense.Id,
            HistoryService.Actions.Deleted,
            $"Ausgabe \"{expense.Description}\" wurde gelöscht.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Active receipts of an expense.</summary>
    public async Task<IReadOnlyList<Receipt>> ListReceiptsAsync(long expenseId, CancellationToken cancellationToken = default)
    {
        return await db.Receipts
            .AsNoTracking()
            .Where(x => x.ExpenseId == expenseId && x.UpdateState != UpdateState.Deleted)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stores an uploaded receipt on disk and records its metadata. Enforces
    /// MaxReceiptSizeMb from the app config and a content type allow list.
    /// </summary>
    public async Task<Result<Receipt>> SaveReceiptAsync(
        long expenseId,
        Stream stream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var expense = await db.Expenses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (expense is null)
        {
            return Result.Fail<Receipt>("Die Ausgabe wurde nicht gefunden.");
        }

        var safeName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();

        if (extension.Length == 0 || !AllowedReceiptExtensions.Contains(extension))
        {
            return Result.Fail<Receipt>("Dieser Dateityp ist nicht erlaubt. Erlaubt sind Bilder und PDF.");
        }

        var normalizedContentType = NormalizeContentType(contentType, extension);
        if (!AllowedReceiptContentTypes.Contains(normalizedContentType))
        {
            return Result.Fail<Receipt>("Dieser Dateityp ist nicht erlaubt. Erlaubt sind Bilder und PDF.");
        }

        var config = await appConfig.GetAsync(cancellationToken);
        var maxBytes = (long)config.MaxReceiptSizeMb * 1024 * 1024;

        if (stream.CanSeek && stream.Length > maxBytes)
        {
            return Result.Fail<Receipt>($"Die Datei ist größer als {config.MaxReceiptSizeMb} MB.");
        }

        var relativeDirectory = Path.Combine(expense.GroupId.ToString(), expense.Id.ToString());
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
        var absolutePath = paths.ResolveReceiptPath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        long written;
        try
        {
            written = await WriteLimitedAsync(stream, absolutePath, maxBytes, cancellationToken);
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(absolutePath);
            return Result.Fail<Receipt>($"Die Datei ist größer als {config.MaxReceiptSizeMb} MB.");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Could not store receipt for expense {ExpenseId}", expenseId);
            TryDeleteFile(absolutePath);
            return Result.Fail<Receipt>("Die Datei konnte nicht gespeichert werden.");
        }

        if (written == 0)
        {
            TryDeleteFile(absolutePath);
            return Result.Fail<Receipt>("Die Datei ist leer.");
        }

        var receipt = new Receipt
        {
            ExpenseId = expenseId,
            FileName = safeName,
            ContentType = normalizedContentType,
            FileSizeBytes = written,
            StoragePath = relativePath,
            UpdateState = UpdateState.Created
        };

        db.Receipts.Add(receipt);

        await history.LogAsync(
            expense.GroupId,
            null,
            HistoryService.EntityTypes.Receipt,
            expenseId,
            HistoryService.Actions.Uploaded,
            $"Beleg \"{safeName}\" wurde zu \"{expense.Description}\" hochgeladen.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(receipt);
    }

    /// <summary>Soft deletes the metadata and removes the file from disk.</summary>
    public async Task<Result> DeleteReceiptAsync(long receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await db.Receipts
            .Include(x => x.Expense)
            .FirstOrDefaultAsync(x => x.Id == receiptId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (receipt is null)
        {
            return Result.Fail("Der Beleg wurde nicht gefunden.");
        }

        receipt.UpdateState = UpdateState.Deleted;

        await history.LogAsync(
            receipt.Expense?.GroupId,
            null,
            HistoryService.EntityTypes.Receipt,
            receipt.Id,
            HistoryService.Actions.Deleted,
            $"Beleg \"{receipt.FileName}\" wurde gelöscht.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        TryDeleteFile(paths.ResolveReceiptPath(receipt.StoragePath));
        return Result.Ok();
    }

    /// <summary>
    /// Resolves a receipt to its absolute path for the /receipts/{id} endpoint.
    /// Returns null when the receipt does not exist any more.
    /// </summary>
    public async Task<ReceiptFile?> GetReceiptPathAsync(long receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await db.Receipts
            .AsNoTracking()
            .Include(x => x.Expense)
            .FirstOrDefaultAsync(x => x.Id == receiptId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (receipt is null)
        {
            return null;
        }

        return new ReceiptFile
        {
            Receipt = receipt,
            AbsolutePath = paths.ResolveReceiptPath(receipt.StoragePath)
        };
    }

    private async Task<Result> ValidateAsync(ExpenseEditModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Description))
        {
            return Result.Fail("Bitte eine Beschreibung angeben.");
        }

        if (model.AmountCents <= 0)
        {
            return Result.Fail("Der Betrag muss größer als 0 sein.");
        }

        var groupExists = await db.Groups.AnyAsync(
            x => x.Id == model.GroupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (!groupExists)
        {
            return Result.Fail("Die Gruppe wurde nicht gefunden.");
        }

        var payerIsMember = await db.GroupMembers.AnyAsync(
            x => x.GroupId == model.GroupId
                 && x.UserId == model.PaidByUserId
                 && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        if (!payerIsMember)
        {
            return Result.Fail("Der Zahler ist kein Mitglied dieser Gruppe.");
        }

        if (model.Shares.Count > 0)
        {
            var memberIds = await db.GroupMembers
                .Where(x => x.GroupId == model.GroupId && x.UpdateState != UpdateState.Deleted)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);

            if (model.Shares.Any(s => !memberIds.Contains(s.UserId)))
            {
                return Result.Fail("Mindestens ein Beteiligter ist kein Mitglied dieser Gruppe.");
            }

            if (model.Shares.Select(s => s.UserId).Distinct().Count() != model.Shares.Count)
            {
                return Result.Fail("Ein Beteiligter darf nur einmal vorkommen.");
            }

            var fixedTotal = model.Shares.Where(s => s.ShareAmountCents.HasValue).Sum(s => s.ShareAmountCents!.Value);
            if (fixedTotal > model.AmountCents)
            {
                return Result.Fail("Die festen Anteile übersteigen den Gesamtbetrag.");
            }

            var hasFactorShare = model.Shares.Any(s => !s.ShareAmountCents.HasValue);
            if (!hasFactorShare && fixedTotal != model.AmountCents)
            {
                return Result.Fail("Die festen Anteile ergeben nicht den Gesamtbetrag.");
            }
        }

        return Result.Ok();
    }

    /// <summary>
    /// Falls back to all group members when no explicit shares were supplied.
    /// </summary>
    private async Task<List<ExpenseShareInput>> ResolveSharesAsync(ExpenseEditModel model, CancellationToken cancellationToken)
    {
        if (model.Shares.Count > 0)
        {
            return model.Shares
                .Select(s => new ExpenseShareInput
                {
                    UserId = s.UserId,
                    ShareFactor = Math.Clamp(s.ShareFactor, 1, 100),
                    ShareAmountCents = s.ShareAmountCents is > 0 ? s.ShareAmountCents : null
                })
                .ToList();
        }

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(x => x.GroupId == model.GroupId && x.UpdateState != UpdateState.Deleted)
            .Select(x => new { x.UserId, x.ShareFactor })
            .ToListAsync(cancellationToken);

        return members
            .Select(m => new ExpenseShareInput { UserId = m.UserId, ShareFactor = Math.Max(1, m.ShareFactor) })
            .ToList();
    }

    private async Task ApplySharesAsync(Expense expense, List<ExpenseShareInput> shares, bool isInsert, CancellationToken cancellationToken)
    {
        if (isInsert)
        {
            foreach (var share in shares)
            {
                expense.Shares.Add(new ExpenseShare
                {
                    UserId = share.UserId,
                    ShareFactor = share.ShareFactor,
                    ShareAmountCents = share.ShareAmountCents,
                    UpdateState = UpdateState.Created
                });
            }

            return;
        }

        var existingShares = await db.ExpenseShares
            .Where(x => x.ExpenseId == expense.Id)
            .ToListAsync(cancellationToken);

        foreach (var input in shares)
        {
            var existing = existingShares.FirstOrDefault(x => x.UserId == input.UserId);
            if (existing is null)
            {
                db.ExpenseShares.Add(new ExpenseShare
                {
                    ExpenseId = expense.Id,
                    UserId = input.UserId,
                    ShareFactor = input.ShareFactor,
                    ShareAmountCents = input.ShareAmountCents,
                    UpdateState = UpdateState.Created
                });

                continue;
            }

            existing.ShareFactor = input.ShareFactor;
            existing.ShareAmountCents = input.ShareAmountCents;
            existing.UpdateState = UpdateState.Updated;
        }

        var keptUserIds = shares.Select(x => x.UserId).ToHashSet();
        foreach (var orphan in existingShares.Where(x => !keptUserIds.Contains(x.UserId)))
        {
            orphan.UpdateState = UpdateState.Deleted;
        }
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> to disk and throws
    /// <see cref="InvalidDataException"/> when the stream is longer.
    /// </summary>
    private static async Task<long> WriteLimitedAsync(Stream source, string absolutePath, long maxBytes, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920;
        var buffer = new byte[bufferSize];
        long total = 0;

        await using var target = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Upload exceeds the configured maximum size.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await target.FlushAsync(cancellationToken);
        return total;
    }

    private void TryDeleteFile(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete receipt file {Path}", absolutePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "No permission to delete receipt file {Path}", absolutePath);
        }
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "beleg.jpg";
        }

        var name = Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length > 200 ? name[^200..] : name;
    }

    private static string NormalizeContentType(string? contentType, string extension)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var value = contentType.Split(';')[0].Trim().ToLowerInvariant();
            if (AllowedReceiptContentTypes.Contains(value))
            {
                return value;
            }
        }

        return extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            _ => "image/jpeg"
        };
    }

    private static string FormatAmount(long cents, string currency)
        => string.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), $"{cents / 100m:N2} {currency}");
}
