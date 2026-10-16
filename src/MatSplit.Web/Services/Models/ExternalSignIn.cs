using MatSplit.Web.Data.Entities;

namespace MatSplit.Web.Services.Models;

/// <summary>
/// The outcome of <see cref="UserService.SignInExternalAsync"/>: the local user to sign in, or the id of the account that matches by its
/// e-mail address and has to be proven by its password before it is linked (<see cref="UserService.LinkExternalAsync"/>).
/// </summary>
public sealed record ExternalSignIn(User? User, long? LinkUserId)
{
    public static ExternalSignIn SignedIn(User user) => new(user, null);

    public static ExternalSignIn LinkRequired(long userId) => new(null, userId);
}
