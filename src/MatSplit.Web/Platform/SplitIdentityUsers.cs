using System.Globalization;
using MatPlatform;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Platform;

/// <summary>
/// MatSplit's side of the sign-in through MatIdentity (MatPlatform <see cref="IMatIdentityUsers"/>): the person is mapped to a local user by
/// <see cref="UserService.SignInExternalAsync"/> (by <c>sub</c>, else a new user; the claim <c>role</c> when it is there)
/// and then signed in with MatSplit's own cookie and session, like a local sign-in, keeping the ID token for the sign-out.
/// An account with the same e-mail address is not linked by the address: the answer is <see cref="MatIdentitySignInResult.LinkRequired"/>,
/// and the sign-in page links it after its password (<see cref="MatIdentityPendingLink"/>).
/// </summary>
public sealed class SplitIdentityUsers(UserService users, CurrentUserService currentUser, HistoryService history) : IMatIdentityUsers
{
    public async Task<MatIdentitySignInResult> SignInAsync(MatIdentityPerson person, HttpContext http, CancellationToken ct)
    {
        var identity = new ExternalIdentity(person.Subject, person.Email, person.Name, person.PreferredUserName, person.Role);

        var result = await users.SignInExternalAsync(identity, ct);
        if (result.IsFailure)
        {
            return MatIdentitySignInResult.Refused(result.Error ?? "refused");
        }

        if (result.Value.LinkUserId is { } linkUserId)
        {
            return MatIdentitySignInResult.LinkRequired(linkUserId.ToString(CultureInfo.InvariantCulture));
        }

        var user = result.Value.User!;
        await currentUser.SignInAsync(user, true, ct, person.IdToken);

        await history.LogAsync(
            null,
            user.Id,
            HistoryService.EntityTypes.User,
            user.Id,
            HistoryService.Actions.SignedIn,
            $"\"{user.DisplayName}\" hat sich über MatIdentity angemeldet.",
            cancellationToken: ct);

        return MatIdentitySignInResult.Success();
    }
}
