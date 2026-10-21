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
/// The session keeps the <c>sid</c> of MatIdentity: a back-channel logout (<see cref="IMatIdentitySessions"/>) ends it through
/// <see cref="SessionService.EndIdentitySessionsAsync"/>.
/// </summary>
public sealed class SplitIdentityUsers(UserService users, CurrentUserService currentUser, SessionService sessions, HistoryService history)
    : IMatIdentityUsers, IMatIdentitySessions
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
        await currentUser.SignInAsync(user, true, ct, person.IdToken, person.SessionId);

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

    public async Task EndSessionsAsync(string? subject, string? sessionId, CancellationToken ct)
    {
        var ended = await sessions.EndIdentitySessionsAsync(subject, sessionId, ct);

        foreach (var (user, count) in ended)
        {
            await history.LogAsync(
                null,
                user.Id,
                HistoryService.EntityTypes.User,
                user.Id,
                HistoryService.Actions.SignedOut,
                count == 1
                    ? $"\"{user.DisplayName}\" wurde über MatIdentity abgemeldet."
                    : $"\"{user.DisplayName}\" wurde über MatIdentity abgemeldet ({count} Sitzungen).",
                cancellationToken: ct);
        }
    }
}
