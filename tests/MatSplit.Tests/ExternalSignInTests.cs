using MatPlatform;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatSplit.Tests;

/// <summary>How a person signed in by MatIdentity becomes a local user (<see cref="UserService.SignInExternalAsync"/>), on an in-memory database.</summary>
public sealed class ExternalSignInTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private UserService _users = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var accessor = new HttpContextAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditInterceptor(accessor))
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        await SchemaUpgrader.UpgradeAsync(_db, NullLogger.Instance);
        await DbInitializer.SeedAsync(_db, NullLogger.Instance);
        _users = new UserService(_db, new HistoryService(_db, accessor), NullLogger<UserService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_new_person_becomes_a_user_with_the_role_of_the_claim()
    {
        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-1", "Neu@Example.com", "Nora Neu", "nora", "admin"));

        Assert.True(result.IsSuccess, result.Error);
        var user = result.Value.User!;
        Assert.Equal("sub-1", user.ExternalSubject);
        Assert.Equal("Nora Neu", user.DisplayName);
        Assert.Equal("neu@example.com", user.Email);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.IsAnonymous);
        Assert.Null(user.PasswordHash);

        // The second time the same user, found by sub.
        var again = await _users.SignInExternalAsync(new ExternalIdentity("sub-1", "other@example.com", "Other", null, "admin"));
        Assert.Equal(user.Id, again.Value.User!.Id);
    }

    [Fact]
    public async Task Without_a_name_the_preferred_username_is_used_and_without_role_claim_the_user_is_a_user()
    {
        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-2", null, null, "bert", null));
        Assert.Equal("bert", result.Value.User!.DisplayName);
        Assert.Equal(UserRole.User, result.Value.User!.Role);
    }

    [Fact]
    public async Task An_existing_user_with_the_same_email_is_not_linked_but_asks_for_its_password()
    {
        var anna = await WithPasswordAsync("Anna");
        var users = await _db.Users.CountAsync();

        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-anna", "ANNA@matsplit.local", "Anna Example", null, "admin"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value.User);
        Assert.Equal(anna.Id, result.Value.LinkUserId);
        var stored = await _db.Users.AsNoTracking().SingleAsync(x => x.Id == anna.Id);
        Assert.Null(stored.ExternalSubject);
        Assert.Equal(UserRole.User, stored.Role);
        Assert.Equal(users, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task After_its_password_the_account_is_linked_with_the_role_of_the_claim()
    {
        var anna = await WithPasswordAsync("Anna");

        var result = await _users.LinkExternalAsync(anna.Id, new ExternalIdentity("sub-anna", "anna@matsplit.local", "Anna Example", null, "admin"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Anna", result.Value.DisplayName);
        Assert.Equal(UserRole.Admin, result.Value.Role);
        Assert.Equal("sub-anna", (await _db.Users.AsNoTracking().SingleAsync(x => x.Id == anna.Id)).ExternalSubject);
        Assert.Contains(await _db.HistoryEntries.AsNoTracking().Select(x => x.Summary).ToListAsync(), x => x.Contains("linked with MatIdentity", StringComparison.Ordinal));

        // From now on the account is found by sub.
        var again = await _users.SignInExternalAsync(new ExternalIdentity("sub-anna", "anna@matsplit.local", "Anna", null, null));
        Assert.Equal(anna.Id, again.Value.User!.Id);
    }

    [Fact]
    public async Task An_account_that_is_linked_meanwhile_or_a_subject_that_is_taken_is_not_linked()
    {
        var anna = await WithPasswordAsync("Anna");
        var admin = await _db.Users.SingleAsync(x => x.Role == UserRole.Admin);

        Assert.True((await _users.LinkExternalAsync(anna.Id, new ExternalIdentity("sub-1", null, null, null, null))).IsSuccess);
        Assert.True((await _users.LinkExternalAsync(anna.Id, new ExternalIdentity("sub-2", null, null, null, null))).IsFailure);
        Assert.True((await _users.LinkExternalAsync(admin.Id, new ExternalIdentity("sub-1", null, null, null, null))).IsFailure);
        Assert.Null((await _db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Id)).ExternalSubject);
    }

    [Fact]
    public async Task A_deleted_account_is_not_linked()
    {
        var anna = await WithPasswordAsync("Anna");
        Assert.True((await _users.SoftDeleteUserAsync(anna.Id)).IsSuccess);

        Assert.True((await _users.LinkExternalAsync(anna.Id, new ExternalIdentity("sub-anna", null, null, null, null))).IsFailure);
    }

    [Fact]
    public async Task Two_users_with_the_same_email_are_not_linked_but_a_new_one_is_made_without_the_address()
    {
        _db.Users.Add(new User { DisplayName = "Anna 2", Email = "anna@matsplit.local", Role = UserRole.User, UpdateState = UpdateState.Created });
        await _db.SaveChangesAsync();

        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-x", "anna@matsplit.local", "Anna X", null, null));

        Assert.True(result.IsSuccess);
        Assert.Equal("Anna X", result.Value.User!.DisplayName);
        Assert.Null(result.Value.User!.Email);
        Assert.Equal(2, await _db.Users.CountAsync(x => x.Email == "anna@matsplit.local" && x.ExternalSubject == null));
    }

    [Fact]
    public async Task The_role_claim_wins_at_every_sign_in_and_without_it_the_local_role_stays()
    {
        var first = await _users.SignInExternalAsync(new ExternalIdentity("sub-r", "r@example.com", "R", null, "user"));
        Assert.Equal(UserRole.User, first.Value.User!.Role);

        Assert.Equal(UserRole.Admin, (await _users.SignInExternalAsync(new ExternalIdentity("sub-r", null, "R", null, "admin"))).Value.User!.Role);
        Assert.Equal(UserRole.Admin, (await _users.SignInExternalAsync(new ExternalIdentity("sub-r", null, "R", null, null))).Value.User!.Role);
        Assert.Equal(UserRole.Admin, (await _users.SignInExternalAsync(new ExternalIdentity("sub-r", null, "R", null, "something-else"))).Value.User!.Role);
        Assert.Equal(UserRole.User, (await _users.SignInExternalAsync(new ExternalIdentity("sub-r", null, "R", null, "user"))).Value.User!.Role);
    }

    [Fact]
    public async Task The_last_administrator_is_not_demoted_by_a_claim()
    {
        var admin = await _db.Users.SingleAsync(x => x.Role == UserRole.Admin);

        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-admin", DbInitializer.DefaultAdminEmail, "admin", null, "user"));
        Assert.Equal(admin.Id, result.Value.LinkUserId);

        var linked = await _users.LinkExternalAsync(admin.Id, new ExternalIdentity("sub-admin", DbInitializer.DefaultAdminEmail, "admin", null, "user"));
        Assert.Equal(UserRole.Admin, linked.Value.Role);
        Assert.Equal(UserRole.Admin, (await _users.SignInExternalAsync(new ExternalIdentity("sub-admin", null, "admin", null, "user"))).Value.User!.Role);
    }

    [Fact]
    public async Task A_deleted_user_stays_out()
    {
        var created = await _users.SignInExternalAsync(new ExternalIdentity("sub-d", "d@example.com", "D", null, null));
        Assert.True((await _users.SoftDeleteUserAsync(created.Value.User!.Id)).IsSuccess);

        Assert.True((await _users.SignInExternalAsync(new ExternalIdentity("sub-d", "d@example.com", "D", null, "admin"))).IsFailure);
        // Nor through the address of the deleted account with another sub.
        Assert.True((await _users.SignInExternalAsync(new ExternalIdentity("sub-d2", "d@example.com", "D", null, null))).IsFailure);
    }

    [Fact]
    public async Task An_account_without_a_password_cannot_prove_itself_and_a_new_user_is_made_without_the_address()
    {
        // A link guest (or a demo member) has no password: nobody can sign in as it, so it is never linked by its address.
        var guest = new User { DisplayName = "Gast", Email = "gast@example.com", Role = UserRole.Anonymous, IsAnonymous = true, UpdateState = UpdateState.Created };
        _db.Users.Add(guest);
        await _db.SaveChangesAsync();

        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-g", "gast@example.com", "Gast", null, null));

        Assert.NotEqual(guest.Id, result.Value.User!.Id);
        Assert.Equal(UserRole.User, result.Value.User!.Role);
        Assert.False(result.Value.User!.IsAnonymous);
        Assert.Null(result.Value.User!.Email);
        Assert.Null((await _db.Users.AsNoTracking().SingleAsync(x => x.Id == guest.Id)).ExternalSubject);
    }

    [Fact]
    public async Task A_match_linked_to_another_subject_is_left_alone()
    {
        var anna = await WithPasswordAsync("Anna");
        Assert.True((await _users.LinkExternalAsync(anna.Id, new ExternalIdentity("sub-anna", null, null, null, null))).IsSuccess);

        var result = await _users.SignInExternalAsync(new ExternalIdentity("sub-other", "anna@matsplit.local", "Other", null, null));

        Assert.NotEqual(anna.Id, result.Value.User!.Id);
        Assert.Null(result.Value.User!.Email);
        Assert.Equal("sub-anna", (await _db.Users.AsNoTracking().SingleAsync(x => x.Id == anna.Id)).ExternalSubject);
    }

    [Fact]
    public async Task No_subject_is_refused()
        => Assert.True((await _users.SignInExternalAsync(new ExternalIdentity(" ", "x@example.com", "X", null, null))).IsFailure);

    /// <summary>The demo user <paramref name="name"/> with a password, so that it can prove itself.</summary>
    private async Task<User> WithPasswordAsync(string name)
    {
        var user = await _db.Users.SingleAsync(x => x.DisplayName == name);
        user.PasswordHash = PasswordHasher.Hash("anna-password");
        await _db.SaveChangesAsync();
        return user;
    }
}

/// <summary>The schema upgrade adds the column of MatIdentity to a database of an older version, and runs twice without harm.</summary>
public sealed class SchemaUpgraderTests
{
    [Fact]
    public async Task An_old_users_table_gets_the_external_subject()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // As 0.6 made it: no column, no index.
        await db.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Users_ExternalSubject\";");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" DROP COLUMN \"ExternalSubject\";");

        await SchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);
        await SchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);

        db.Users.Add(new User { DisplayName = "X", ExternalSubject = "sub", UpdateState = UpdateState.Created });
        await db.SaveChangesAsync();
        Assert.Equal("sub", (await db.Users.AsNoTracking().SingleAsync()).ExternalSubject);
    }
}

/// <summary>MATIDENTITY_INTERNAL_URL: the server-to-server calls go to the internal address, everything else stays as it is.</summary>
public sealed class BackchannelHandlerTests
{
    [Theory]
    [InlineData("https://id.example.com/.well-known/openid-configuration", "http://matidentity:2300/.well-known/openid-configuration")]
    [InlineData("https://id.example.com/.well-known/jwks", "http://matidentity:2300/.well-known/jwks")]
    [InlineData("https://id.example.com/connect/token", "http://matidentity:2300/connect/token")]
    [InlineData("https://ID.example.com:443/connect/userinfo?x=1", "http://matidentity:2300/connect/userinfo?x=1")]
    public void An_address_of_the_public_authority_is_rewritten(string from, string to)
    {
        var handler = new MatIdentityBackchannelHandler(new Uri("https://id.example.com"), new Uri("http://matidentity:2300"));
        Assert.Equal(new Uri(to), handler.Rewrite(new Uri(from)));
    }

    [Theory]
    [InlineData("https://other.example.com/connect/token")]
    [InlineData("http://id.example.com/connect/token")]
    [InlineData("https://id.example.com:8443/connect/token")]
    public void Other_addresses_stay(string from)
    {
        var handler = new MatIdentityBackchannelHandler(new Uri("https://id.example.com"), new Uri("http://matidentity:2300"));
        Assert.Null(handler.Rewrite(new Uri(from)));
    }

    [Fact]
    public void An_authority_with_a_path_keeps_the_rest_of_the_path()
    {
        var handler = new MatIdentityBackchannelHandler(new Uri("https://example.com/id"), new Uri("http://matidentity:2300"));
        Assert.Equal(new Uri("http://matidentity:2300/connect/token"), handler.Rewrite(new Uri("https://example.com/id/connect/token")));
        Assert.Null(handler.Rewrite(new Uri("https://example.com/identity/connect/token")));
    }

    [Fact]
    public async Task The_request_goes_to_the_internal_address()
    {
        var inner = new RecordingHandler();
        using var handler = new MatIdentityBackchannelHandler(new Uri("https://id.example.com"), new Uri("http://matidentity:2300"), inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://id.example.com/.well-known/openid-configuration");

        Assert.Equal(new Uri("http://matidentity:2300/.well-known/openid-configuration"), inner.Seen);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
