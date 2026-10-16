namespace MatSplit.Web.Services.Models;

/// <summary>
/// A person as MatIdentity describes it in the ID token: the claims <c>sub</c>, <c>email</c>, <c>name</c>,
/// <c>preferred_username</c> and, when MatIdentity manages the roles of the app, <c>role</c> (<c>admin</c> or <c>user</c>).
/// </summary>
public sealed record ExternalIdentity(
    string Subject,
    string? Email,
    string? Name,
    string? PreferredUsername,
    string? Role);
