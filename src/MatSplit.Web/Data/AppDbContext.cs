using MatSplit.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatSplit.Web.Data;

/// <summary>
/// Single EF Core context of the application. The schema is created at startup
/// via Database.EnsureCreated(); there are no migrations in v1, so every table
/// and column is configured explicitly with the Fluent API.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUsers(modelBuilder);
        ConfigureUserSessions(modelBuilder);
        ConfigureGroups(modelBuilder);
        ConfigureGroupMembers(modelBuilder);
        ConfigureExpenses(modelBuilder);
        ConfigureExpenseShares(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigureReceipts(modelBuilder);
        ConfigureHistoryEntries(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.ToTable("Users");
        ConfigureAudit(entity, "Users");

        entity.Property(x => x.Token).HasColumnName("Token").HasMaxLength(64).IsRequired();
        entity.Property(x => x.DisplayName).HasColumnName("DisplayName").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Email).HasColumnName("Email").HasMaxLength(320);
        entity.Property(x => x.PasswordHash).HasColumnName("PasswordHash").HasMaxLength(400);
        entity.Property(x => x.PayPalAddress).HasColumnName("PayPalAddress").HasMaxLength(300);
        entity.Property(x => x.Role).HasColumnName("Role").HasConversion<int>().IsRequired();
        entity.Property(x => x.IsAnonymous).HasColumnName("IsAnonymous").IsRequired();
        entity.Property(x => x.MergedIntoUserId).HasColumnName("MergedIntoUserId");
        entity.Property(x => x.ThemePreference).HasColumnName("ThemePreference").HasConversion<int>().IsRequired();

        entity.HasIndex(x => x.Token).IsUnique().HasDatabaseName("IX_Users_Token");
        entity.HasIndex(x => x.Email).HasDatabaseName("IX_Users_Email");
        entity.HasIndex(x => x.MergedIntoUserId).HasDatabaseName("IX_Users_MergedIntoUserId");

        entity.HasOne(x => x.MergedIntoUser)
            .WithMany()
            .HasForeignKey(x => x.MergedIntoUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUserSessions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserSession>();
        entity.ToTable("UserSessions");
        ConfigureAudit(entity, "UserSessions");

        entity.Property(x => x.Token).HasColumnName("Token").HasMaxLength(64).IsRequired();
        entity.Property(x => x.UserId).HasColumnName("UserId").IsRequired();
        entity.Property(x => x.CreatedUtc).HasColumnName("CreatedUtc").IsRequired();
        entity.Property(x => x.ExpiresUtc).HasColumnName("ExpiresUtc").IsRequired();
        entity.Property(x => x.LastSeenUtc).HasColumnName("LastSeenUtc").IsRequired();
        entity.Property(x => x.UserAgent).HasColumnName("UserAgent").HasMaxLength(512);

        entity.HasIndex(x => x.Token).IsUnique().HasDatabaseName("IX_UserSessions_Token");
        entity.HasIndex(x => x.UserId).HasDatabaseName("IX_UserSessions_UserId");
        entity.HasIndex(x => x.ExpiresUtc).HasDatabaseName("IX_UserSessions_ExpiresUtc");

        entity.HasOne(x => x.User)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGroups(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Group>();
        entity.ToTable("Groups");
        ConfigureAudit(entity, "Groups");

        entity.Property(x => x.Token).HasColumnName("Token").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasColumnName("Description").HasMaxLength(2000);
        entity.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).HasDefaultValue("EUR").IsRequired();
        entity.Property(x => x.InviteToken).HasColumnName("InviteToken").HasMaxLength(64).IsRequired();
        entity.Property(x => x.InviteEnabled).HasColumnName("InviteEnabled").IsRequired();

        entity.HasIndex(x => x.Token).IsUnique().HasDatabaseName("IX_Groups_Token");
        entity.HasIndex(x => x.InviteToken).IsUnique().HasDatabaseName("IX_Groups_InviteToken");
    }

    private static void ConfigureGroupMembers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GroupMember>();
        entity.ToTable("GroupMembers");
        ConfigureAudit(entity, "GroupMembers");

        entity.Property(x => x.GroupId).HasColumnName("GroupId").IsRequired();
        entity.Property(x => x.UserId).HasColumnName("UserId").IsRequired();
        entity.Property(x => x.ShareFactor).HasColumnName("ShareFactor").HasDefaultValue(1).IsRequired();
        entity.Property(x => x.IsGroupAdmin).HasColumnName("IsGroupAdmin").IsRequired();

        entity.HasIndex(x => x.GroupId).HasDatabaseName("IX_GroupMembers_GroupId");
        entity.HasIndex(x => x.UserId).HasDatabaseName("IX_GroupMembers_UserId");
        entity.HasIndex(x => new { x.GroupId, x.UserId }).HasDatabaseName("IX_GroupMembers_GroupId_UserId");

        entity.HasOne(x => x.Group)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.User)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureExpenses(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Expense>();
        entity.ToTable("Expenses");
        ConfigureAudit(entity, "Expenses");

        entity.Property(x => x.GroupId).HasColumnName("GroupId").IsRequired();
        entity.Property(x => x.Description).HasColumnName("Description").HasMaxLength(400).IsRequired();
        entity.Property(x => x.AmountCents).HasColumnName("AmountCents").IsRequired();
        entity.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).HasDefaultValue("EUR").IsRequired();
        entity.Property(x => x.PaidByUserId).HasColumnName("PaidByUserId").IsRequired();
        entity.Property(x => x.ExpenseDate).HasColumnName("ExpenseDate").IsRequired();
        entity.Property(x => x.Category).HasColumnName("Category").HasMaxLength(100);

        entity.HasIndex(x => x.GroupId).HasDatabaseName("IX_Expenses_GroupId");
        entity.HasIndex(x => x.PaidByUserId).HasDatabaseName("IX_Expenses_PaidByUserId");
        entity.HasIndex(x => x.ExpenseDate).HasDatabaseName("IX_Expenses_ExpenseDate");

        entity.HasOne(x => x.Group)
            .WithMany(x => x.Expenses)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.PaidByUser)
            .WithMany()
            .HasForeignKey(x => x.PaidByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureExpenseShares(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ExpenseShare>();
        entity.ToTable("ExpenseShares");
        ConfigureAudit(entity, "ExpenseShares");

        entity.Property(x => x.ExpenseId).HasColumnName("ExpenseId").IsRequired();
        entity.Property(x => x.UserId).HasColumnName("UserId").IsRequired();
        entity.Property(x => x.ShareFactor).HasColumnName("ShareFactor").HasDefaultValue(1).IsRequired();
        entity.Property(x => x.ShareAmountCents).HasColumnName("ShareAmountCents");

        entity.HasIndex(x => x.ExpenseId).HasDatabaseName("IX_ExpenseShares_ExpenseId");
        entity.HasIndex(x => x.UserId).HasDatabaseName("IX_ExpenseShares_UserId");

        entity.HasOne(x => x.Expense)
            .WithMany(x => x.Shares)
            .HasForeignKey(x => x.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Payment>();
        entity.ToTable("Payments");
        ConfigureAudit(entity, "Payments");

        entity.Property(x => x.GroupId).HasColumnName("GroupId").IsRequired();
        entity.Property(x => x.FromUserId).HasColumnName("FromUserId").IsRequired();
        entity.Property(x => x.ToUserId).HasColumnName("ToUserId").IsRequired();
        entity.Property(x => x.AmountCents).HasColumnName("AmountCents").IsRequired();
        entity.Property(x => x.PaymentDate).HasColumnName("PaymentDate").IsRequired();
        entity.Property(x => x.Note).HasColumnName("Note").HasMaxLength(1000);

        entity.HasIndex(x => x.GroupId).HasDatabaseName("IX_Payments_GroupId");
        entity.HasIndex(x => x.FromUserId).HasDatabaseName("IX_Payments_FromUserId");
        entity.HasIndex(x => x.ToUserId).HasDatabaseName("IX_Payments_ToUserId");

        entity.HasOne(x => x.Group)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.FromUser)
            .WithMany()
            .HasForeignKey(x => x.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(x => x.ToUser)
            .WithMany()
            .HasForeignKey(x => x.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureReceipts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Receipt>();
        entity.ToTable("Receipts");
        ConfigureAudit(entity, "Receipts");

        entity.Property(x => x.ExpenseId).HasColumnName("ExpenseId").IsRequired();
        entity.Property(x => x.FileName).HasColumnName("FileName").HasMaxLength(300).IsRequired();
        entity.Property(x => x.ContentType).HasColumnName("ContentType").HasMaxLength(150).IsRequired();
        entity.Property(x => x.FileSizeBytes).HasColumnName("FileSizeBytes").IsRequired();
        entity.Property(x => x.StoragePath).HasColumnName("StoragePath").HasMaxLength(500).IsRequired();

        entity.HasIndex(x => x.ExpenseId).HasDatabaseName("IX_Receipts_ExpenseId");

        entity.HasOne(x => x.Expense)
            .WithMany(x => x.Receipts)
            .HasForeignKey(x => x.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureHistoryEntries(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HistoryEntry>();
        entity.ToTable("HistoryEntries");
        ConfigureAudit(entity, "HistoryEntries");

        entity.Property(x => x.GroupId).HasColumnName("GroupId");
        entity.Property(x => x.UserId).HasColumnName("UserId");
        entity.Property(x => x.EntityType).HasColumnName("EntityType").HasMaxLength(100).IsRequired();
        entity.Property(x => x.EntityId).HasColumnName("EntityId");
        entity.Property(x => x.Action).HasColumnName("Action").HasMaxLength(100).IsRequired();
        entity.Property(x => x.Summary).HasColumnName("Summary").HasMaxLength(1000).IsRequired();
        entity.Property(x => x.DetailsJson).HasColumnName("DetailsJson");

        entity.HasIndex(x => x.GroupId).HasDatabaseName("IX_HistoryEntries_GroupId");
        entity.HasIndex(x => x.UserId).HasDatabaseName("IX_HistoryEntries_UserId");
        entity.HasIndex(x => x.CreateDate).HasDatabaseName("IX_HistoryEntries_CreateDate");

        entity.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Applies the shared primary key and audit column configuration.
    /// </summary>
    private static void ConfigureAudit<TEntity>(EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : AuditableEntity
    {
        entity.HasKey(x => x.Id);
        // SQLite stores INTEGER as a 64-bit signed value, which is the required BIGINT.
        // An explicit "BIGINT" column type would break AUTOINCREMENT.
        entity.Property(x => x.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        entity.Property(x => x.CreateDate).HasColumnName("CreateDate").IsRequired();
        entity.Property(x => x.CreateUserId).HasColumnName("CreateUserId");
        entity.Property(x => x.UpdateDate).HasColumnName("UpdateDate").IsRequired();
        entity.Property(x => x.UpdateUserId).HasColumnName("UpdateUserId");
        entity.Property(x => x.UpdateState).HasColumnName("UpdateState").HasConversion<int>().IsRequired();
        entity.Ignore(x => x.IsDeleted);
        entity.HasIndex(x => x.UpdateState).HasDatabaseName("IX_" + tableName + "_UpdateState");
    }
}
