namespace MatSplit.Web.Data;

/// <summary>
/// Global role of a user. Group specific rights are modelled by
/// <see cref="Entities.GroupMember.IsGroupAdmin"/>.
/// </summary>
public enum UserRole
{
    Anonymous = 0,
    User = 1,
    Admin = 2
}
