namespace MatSplit.Web.Data.Entities;

/// <summary>
/// A person that can log in (local user) or that was created through an invite
/// link (anonymous user). Anonymous users can later be merged into real users.
/// </summary>
public class User : AuditableEntity
{
    public string Token { get; set; } = Guid.NewGuid().ToString();

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    public string? PayPalAddress { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsAnonymous { get; set; }

    /// <summary>Set when this user was merged into another user (soft deleted source).</summary>
    public long? MergedIntoUserId { get; set; }

    public ThemeMode ThemePreference { get; set; } = ThemeMode.System;

    public User? MergedIntoUser { get; set; }

    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();

    public ICollection<GroupMember> Memberships { get; set; } = new List<GroupMember>();
}
