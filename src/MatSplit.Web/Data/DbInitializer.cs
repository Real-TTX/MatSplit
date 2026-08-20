using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Data;

/// <summary>
/// Creates the initial content of a fresh database: one administrator
/// (admin / admin) and a small demo group so the UI is not empty on first run.
/// Idempotent - safe to call on every startup.
/// </summary>
public static class DbInitializer
{
    public const string DefaultAdminLogin = "admin";

    public const string DefaultAdminPassword = "admin";

    public const string DefaultAdminEmail = "admin@matsplit.local";

    public const string DemoGroupName = "Demo: Urlaub Mallorca";

    /// <summary>Synchronous wrapper around <see cref="SeedAsync"/>.</summary>
    public static void Seed(AppDbContext db, ILogger logger)
        => SeedAsync(db, logger).GetAwaiter().GetResult();

    /// <summary>
    /// Ensures an administrator exists and adds the demo group once.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var admin = await EnsureAdminAsync(db, logger, cancellationToken);
        await EnsureDemoGroupAsync(db, logger, admin, cancellationToken);
    }

    private static async Task<User> EnsureAdminAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var existing = await db.Users.FirstOrDefaultAsync(
            x => x.Role == UserRole.Admin && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var admin = new User
        {
            Token = Guid.NewGuid().ToString(),
            DisplayName = DefaultAdminLogin,
            Email = DefaultAdminEmail,
            PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
            Role = UserRole.Admin,
            IsAnonymous = false,
            ThemePreference = ThemeMode.System,
            UpdateState = UpdateState.Created
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Created default administrator {Login} with the default password. Please change it after the first login.",
            DefaultAdminLogin);

        return admin;
    }

    private static async Task EnsureDemoGroupAsync(AppDbContext db, ILogger logger, User admin, CancellationToken cancellationToken)
    {
        var anyGroup = await db.Groups.AnyAsync(cancellationToken);
        if (anyGroup)
        {
            return;
        }

        var group = new Group
        {
            Token = Guid.NewGuid().ToString(),
            Name = DemoGroupName,
            Description = "Beispielgruppe mit Ausgaben, Anteils-Faktoren und einer Zahlung.",
            Currency = "EUR",
            InviteToken = Guid.NewGuid().ToString(),
            InviteEnabled = true,
            UpdateState = UpdateState.Created
        };

        db.Groups.Add(group);

        var anna = CreateDemoUser("Anna", "anna@matsplit.local");
        var horst = CreateDemoUser("Horst", null);
        var familyMeyer = CreateDemoUser("Familie Meyer", null);

        anna.PayPalAddress = "https://paypal.me/annademo";
        horst.PayPalAddress = "horstdemo";

        db.Users.AddRange(anna, horst, familyMeyer);
        await db.SaveChangesAsync(cancellationToken);

        db.GroupMembers.AddRange(
            NewMember(group.Id, admin.Id, 1, true),
            NewMember(group.Id, anna.Id, 1, true),
            NewMember(group.Id, horst.Id, 1, false),
            NewMember(group.Id, familyMeyer.Id, 3, false));

        await db.SaveChangesAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        // Groceries: split across everybody by their group factor (6 shares).
        var groceries = NewExpense(group.Id, "Grosseinkauf Supermarkt", 12_450, anna.Id, today.AddDays(-6), "Lebensmittel");
        AddShare(groceries, admin.Id, 1);
        AddShare(groceries, anna.Id, 1);
        AddShare(groceries, horst.Id, 1);
        AddShare(groceries, familyMeyer.Id, 3);

        // Rental car: only the two adults who drive.
        var rentalCar = NewExpense(group.Id, "Mietwagen 3 Tage", 21_900, admin.Id, today.AddDays(-5), "Transport");
        AddShare(rentalCar, admin.Id, 1);
        AddShare(rentalCar, anna.Id, 1);

        // Dinner with one fixed share (Horst had the expensive lobster).
        var dinner = NewExpense(group.Id, "Abendessen Strandbar", 9_600, horst.Id, today.AddDays(-3), "Restaurant");
        AddShare(dinner, horst.Id, 1, 4_000);
        AddShare(dinner, admin.Id, 1);
        AddShare(dinner, anna.Id, 1);
        AddShare(dinner, familyMeyer.Id, 3);

        db.Expenses.AddRange(groceries, rentalCar, dinner);
        await db.SaveChangesAsync(cancellationToken);

        db.Payments.Add(new Payment
        {
            GroupId = group.Id,
            FromUserId = familyMeyer.Id,
            ToUserId = anna.Id,
            AmountCents = 5_000,
            PaymentDate = today.AddDays(-1),
            Note = "Anzahlung für den Einkauf",
            UpdateState = UpdateState.Created
        });

        db.HistoryEntries.Add(new HistoryEntry
        {
            GroupId = group.Id,
            UserId = admin.Id,
            EntityType = "Group",
            EntityId = group.Id,
            Action = "Created",
            Summary = $"Demo-Gruppe \"{group.Name}\" wurde beim ersten Start angelegt.",
            UpdateState = UpdateState.Created
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded demo group {GroupName} with {MemberCount} members", group.Name, 4);
    }

    private static User CreateDemoUser(string displayName, string? email) => new()
    {
        Token = Guid.NewGuid().ToString(),
        DisplayName = displayName,
        Email = email,
        Role = email is null ? UserRole.Anonymous : UserRole.User,
        IsAnonymous = email is null,
        ThemePreference = ThemeMode.System,
        UpdateState = UpdateState.Created
    };

    private static GroupMember NewMember(long groupId, long userId, int shareFactor, bool isGroupAdmin) => new()
    {
        GroupId = groupId,
        UserId = userId,
        ShareFactor = shareFactor,
        IsGroupAdmin = isGroupAdmin,
        UpdateState = UpdateState.Created
    };

    private static Expense NewExpense(
        long groupId,
        string description,
        long amountCents,
        long paidByUserId,
        DateTime expenseDate,
        string category) => new()
        {
            GroupId = groupId,
            Description = description,
            AmountCents = amountCents,
            Currency = "EUR",
            PaidByUserId = paidByUserId,
            ExpenseDate = expenseDate,
            Category = category,
            UpdateState = UpdateState.Created
        };

    private static void AddShare(Expense expense, long userId, int shareFactor, long? shareAmountCents = null)
    {
        expense.Shares.Add(new ExpenseShare
        {
            UserId = userId,
            ShareFactor = shareFactor,
            ShareAmountCents = shareAmountCents,
            UpdateState = UpdateState.Created
        });
    }
}
