# MatSplit - Architektur & Vertrag fuer Feature-Agenten

Diese Datei ist die **verbindliche Schnittstellenbeschreibung** des Core-Layers
(Datenmodell, Services, Auth). Wer Razor Pages, TagHelper-Controls, CSS oder
Docker/CI baut, richtet sich nach den hier dokumentierten Signaturen.

Assembly / Namespace-Wurzel: `MatSplit.Web` (relevant fuer
`@addTagHelper *, MatSplit.Web`).

---

## 1. Projektstruktur

```
MatSplit.sln
Directory.Build.props            VersionMajor=0 / VersionMinor=1, net10.0, nullable, implicit usings
src/MatSplit.Web/
  Program.cs                     Pipeline, Auth, Policies, /health, /receipts/{id}
  appsettings.json               Data:DataDirectory, Data:DatabasePath
  Api/SyncApi.cs                 MapSyncApi() - PWA-Sync (Stub, erweiterbar)
  Data/
    AppDbContext.cs              alle DbSets, komplette Fluent-API
    AuditableEntity.cs           Basisklasse (Id + Audit-Spalten)
    UpdateState.cs               enum: Deleted=0, Created=1, Updated=2
    UserRole.cs                  enum: Anonymous=0, User=1, Admin=2
    ThemeMode.cs                 enum: System=0, Dark=1, Light=2
    DbInitializer.cs             Seed: Admin admin/admin + Demo-Gruppe
    Entities/*.cs                User, UserSession, Group, GroupMember, Expense,
                                 ExpenseShare, Payment, Receipt, HistoryEntry
  Infrastructure/
    MatSplitPaths.cs             /data-Pfade (Singleton)
    MatSplitClaims.cs            Scheme-/Policy-/Claim-Konstanten + Principal-Factory
    SessionAuthenticationMiddleware.cs   Session-Validierung gegen UserSessions
    AuditInterceptor.cs          setzt Audit-Spalten automatisch
    PasswordHasher.cs            PBKDF2-SHA256
    PayPalLinkBuilder.cs         paypal.me-Links
    Result.cs                    Result / Result<T>
  Services/
    ServiceRegistration.cs       AddMatSplitServices()
    AppConfigService.cs  SessionService.cs  HistoryService.cs  UserService.cs
    GroupService.cs  ExpenseService.cs  PaymentService.cs  BalanceService.cs
    CurrentUserService.cs
    Models/                      PagedResult<T>, Paging, ExpenseEditModel,
                                 ExpenseShareInput, PaymentEditModel, BalanceResult,
                                 MemberBalance, Settlement, AppConfig,
                                 GroupJoinResult, ReceiptFile, Sync*Dto
  Pages/                         << Zustaendigkeit der Feature-Agenten
  Ui/                            << TagHelper-Bibliothek, Zustaendigkeit UI-Agent
  wwwroot/                       << CSS/JS/Icons/PWA, Zustaendigkeit UI-Agent
docs/architecture.md
```

### Namenskonvention
Alle DB-/IO-Methoden sind **async** und tragen das Suffix `Async`
(Microsoft-Guideline). Die Spezifikation nennt sie ohne Suffix - gemeint sind
immer die `...Async`-Varianten aus dieser Datei. Jede Methode hat als letzten
Parameter ein optionales `CancellationToken cancellationToken = default`.

---

## 2. Datenmodell

Jede Entity erbt von `AuditableEntity`:

| Feld | Typ | Bemerkung |
|---|---|---|
| `Id` | `long` | PK, autoincrement (SQLite INTEGER = 64 Bit) |
| `CreateDate` | `DateTime` | UTC, vom `AuditInterceptor` gesetzt |
| `CreateUserId` | `long?` | aus den Claims des Requests |
| `UpdateDate` | `DateTime` | UTC, vom `AuditInterceptor` gesetzt |
| `UpdateUserId` | `long?` | aus den Claims des Requests |
| `UpdateState` | `UpdateState` | 0 = Deleted (Soft-Delete), 1 = Created, 2 = Updated |
| `IsDeleted` | `bool` (read-only) | `UpdateState == Deleted`, **nicht** gemappt |

**Wichtig:** Audit-Spalten NIE manuell setzen. Der Interceptor macht das.
Beim Aendern eines Datensatzes `UpdateState = UpdateState.Updated` setzen,
beim Loeschen `UpdateState = UpdateState.Deleted` (Soft-Delete).

### Entities (Felder ausser Audit)

**Users** - `Token` (uuid), `DisplayName`, `Email?`, `PasswordHash?`,
`PayPalAddress?`, `Role` (`UserRole`), `IsAnonymous` (bool),
`MergedIntoUserId?` (long), `ThemePreference` (`ThemeMode`).
Navigation: `MergedIntoUser`, `Sessions`, `Memberships`.

**UserSessions** - `Token` (64 Zeichen Random), `UserId`, `CreatedUtc`,
`ExpiresUtc`, `LastSeenUtc`, `UserAgent?`. Navigation: `User`.

**Groups** - `Token` (uuid), `Name`, `Description?`, `Currency` (Default "EUR"),
`InviteToken` (uuid), `InviteEnabled` (bool).
Navigation: `Members`, `Expenses`, `Payments`.

**GroupMembers** - `GroupId`, `UserId`, `ShareFactor` (int, Default 1),
`IsGroupAdmin` (bool). Navigation: `Group`, `User`.

**Expenses** - `GroupId`, `Description`, `AmountCents` (long), `Currency`,
`PaidByUserId`, `ExpenseDate` (DateTime, Datumsanteil UTC), `Category?`.
Navigation: `Group`, `PaidByUser`, `Shares`, `Receipts`.

**ExpenseShares** - `ExpenseId`, `UserId`, `ShareFactor` (int),
`ShareAmountCents?` (long; `null` = anteilig nach Faktor).
Navigation: `Expense`, `User`.

**Payments** - `GroupId`, `FromUserId`, `ToUserId`, `AmountCents` (long),
`PaymentDate`, `Note?`. Navigation: `Group`, `FromUser`, `ToUser`.

**Receipts** - `ExpenseId`, `FileName`, `ContentType`, `FileSizeBytes` (long),
`StoragePath` (relativ zu `/data/receipts`, Form `"<groupId>/<expenseId>/<guid>.<ext>"`).
Navigation: `Expense`.

**HistoryEntries** - `GroupId?`, `UserId?`, `EntityType`, `EntityId?`, `Action`,
`Summary`, `DetailsJson?`. Navigation: `Group`, `User`.

### Regeln
- **Geld immer `long` in Cent**, Feldname endet auf `...Cents`. Kein double/float.
- **Kein Global Query Filter.** Soft-Delete-Filterung passiert in den Services.
  Razor Pages greifen **nicht** direkt auf `AppDbContext` zu, sondern nur auf Services.
- FK-Verhalten: Kind-Tabellen einer Gruppe/Ausgabe = `Cascade`, Verweise auf
  `Users` = `Restrict`.

---

## 3. Auth, Session, Autorisierung

### Konstanten (`MatSplit.Web.Infrastructure.MatSplitClaims`)
```csharp
MatSplitClaims.AuthenticationScheme      // "MatSplit"
MatSplitClaims.AdminOnlyPolicy           // "AdminOnly"
MatSplitClaims.AuthenticatedUserPolicy   // "AuthenticatedUser"
MatSplitClaims.SessionTokenClaim         // "matsplit:session"
MatSplitClaims.UserTokenClaim            // "matsplit:usertoken"
MatSplitClaims.ThemeClaim                // "matsplit:theme"
MatSplitClaims.IsAnonymousClaim          // "matsplit:anonymous"
```

### Policies (in Program.cs registriert)
- `"AdminOnly"` - authentifiziert **und** Rolle `Admin`.
- `"AuthenticatedUser"` - authentifiziert und Rolle `Admin`, `User` oder `Anonymous`.

### Razor-Pages-Conventions (schon gesetzt, nicht duplizieren)
```csharp
Conventions.AuthorizeFolder("/", "AuthenticatedUser");
Conventions.AuthorizeFolder("/Admin", "AdminOnly");
Conventions.AllowAnonymousToPage("/Account/Login");
Conventions.AllowAnonymousToPage("/Account/Logout");
Conventions.AllowAnonymousToPage("/Join");
Conventions.AllowAnonymousToPage("/Error");
```
=> Jede neue Seite ist **automatisch geschuetzt**. Kein `[Authorize]` noetig.
Alles unter `Pages/Admin/**` ist automatisch Admin-only.
Wer eine weitere anonyme Seite braucht, meldet das im Report; Program.cs gehoert
dem Core-Agenten.

### Ablauf
1. `/Account/Login` prueft `UserService.ValidatePasswordAsync(...)`.
2. Bei Erfolg `CurrentUserService.SignInAsync(user)` -> legt eine Zeile in
   `UserSessions` an und setzt das Cookie `MatSplit.Auth`.
3. `SessionAuthenticationMiddleware` prueft bei **jedem** Request den
   Session-Token gegen die DB, baut das Principal aus dem aktuellen
   DB-Datensatz neu (Rollen-/Namensaenderung wirkt sofort) und legt den
   `User` in `HttpContext.Items` ab. Abgelaufene/geloeschte Sessions werden
   sofort ausgeloggt.
4. `/Account/Logout` ruft `CurrentUserService.SignOutAsync()`.

### Zugriff aus einer Razor Page
```csharp
public class ExpensesModel(
    CurrentUserService currentUser,
    GroupService groups,
    ExpenseService expenses) : PageModel
{
    public async Task<IActionResult> OnGetAsync(long groupId)
    {
        if (!await currentUser.CanViewGroupAsync(groupId))
        {
            return Forbid();
        }

        var userId = currentUser.RequireUserId();
        // ...
        return Page();
    }
}
```

Im `.cshtml` bzw. `_Layout.cshtml`:
```csharp
@inject MatSplit.Web.Services.CurrentUserService CurrentUser
@inject MatSplit.Web.Services.AppConfigService AppConfig

@if (CurrentUser.IsAdmin) { /* Administration im Menue zeigen */ }
<html data-theme="@MatSplit.Web.Services.CurrentUserService.ToThemeAttribute(CurrentUser.Theme)">
```

### CurrentUserService (Scoped)
| Member | Typ | Bemerkung |
|---|---|---|
| `Principal` | `ClaimsPrincipal?` | |
| `IsAuthenticated` | `bool` | |
| `UserId` | `long?` | |
| `CurrentUser` | `User?` | aus `HttpContext.Items`, kein DB-Zugriff |
| `DisplayName` | `string` | Fallback `"Gast"` |
| `Role` | `UserRole` | |
| `IsAdmin` | `bool` | |
| `IsAnonymousUser` | `bool` | Link-Gast ohne Passwort |
| `Theme` | `ThemeMode` | |
| `SessionToken` | `string?` | |
| `RequireUserId()` | `long` | wirft, wenn nicht eingeloggt |
| `IsMemberAsync(groupId)` | `Task<bool>` | |
| `IsGroupAdminAsync(groupId)` | `Task<bool>` | globaler Admin = true |
| `CanViewGroupAsync(groupId)` | `Task<bool>` | Mitglied oder Admin |
| `CanManageGroupAsync(groupId)` | `Task<bool>` | Gruppen-Admin oder Admin |
| `SignInAsync(user, isPersistent = true)` | `Task` | Login **und** Join |
| `SignOutAsync()` | `Task` | |
| `RefreshSignInAsync()` | `Task` | nach Profil-/Theme-Aenderung |
| `static ToThemeAttribute(ThemeMode)` | `string` | `"system"` / `"dark"` / `"light"` |

---

## 4. Services

Alle im DI registriert via `builder.Services.AddMatSplitServices()`.
Scoped: `SessionService`, `HistoryService`, `UserService`, `GroupService`,
`ExpenseService`, `PaymentService`, `BalanceService`, `CurrentUserService`.
Singleton: `AppConfigService`, `MatSplitPaths`, `AuditInterceptor`.

### Result-Typen
```csharp
Result           { bool IsSuccess; bool IsFailure; string? Error; }
Result<T> : Result { T Value;  T? ValueOrDefault; }
```
`Error` ist eine **deutsche** Meldung, direkt fuer
`ModelState.AddModelError(string.Empty, result.Error!)` geeignet.

### PagedResult<T> / Paging
```csharp
PagedResult<T> { IReadOnlyList<T> Items; int Page; int PageSize; int TotalCount;
                 int TotalPages; bool HasPreviousPage; bool HasNextPage; bool IsEmpty; }
Paging.DefaultPageSize = 20;  Paging.MaxPageSize = 200;
```
Direkt an `ms-pagination` (page / page-size / total-count / page-url) uebergeben.

### UserService
```csharp
Task<User?>              GetByIdAsync(long id)
Task<User?>              GetByTokenAsync(string? token)
Task<PagedResult<User>>  ListUsersAsync(string? search = null, UserRole? role = null,
                                        int page = 1, int pageSize = 20,
                                        string? sort = null, bool includeDeleted = false)
Task<IReadOnlyList<User>> ListAllActiveUsersAsync()
Task<Result<User>>       CreateLocalUserAsync(string displayName, string? email,
                                              string? password, UserRole role)
Task<User>               CreateAnonymousUserAsync(string displayName)
Task<User?>              ValidatePasswordAsync(string? emailOrName, string? password)
Task<Result>             UpdateProfileAsync(long userId, string displayName, string? email,
                                            string? payPalAddress, string? newPassword = null)
Task<Result>             SetRoleAsync(long userId, UserRole role)
Task<Result>             SetThemeAsync(long userId, ThemeMode theme)
Task<Result>             SoftDeleteUserAsync(long userId)
Task<Result>             MergeUsersAsync(long sourceUserId, long targetUserId)
Task<User?>              ResolveEffectiveUserAsync(long userId)
```
`sort`-Schluessel: `name` (Default), `name_desc`, `email`, `email_desc`,
`role`, `role_desc`, `created`, `created_desc`.
`ValidatePasswordAsync` akzeptiert E-Mail **oder** Anzeigename.
`MergeUsersAsync` verschiebt GroupMembers, `Expenses.PaidByUserId`,
`ExpenseShares.UserId`, `Payments.From/ToUserId`, setzt `MergedIntoUserId`,
soft-loescht die Quelle und schreibt einen HistoryEntry. Der letzte Admin kann
nicht geloescht/herabgestuft/gemergt werden (Result mit Fehlertext).

### GroupService
```csharp
Task<IReadOnlyList<Group>>      ListGroupsForUserAsync(long userId, bool includeAll = false)
Task<PagedResult<Group>>        ListGroupsAsync(string? search = null, int page = 1,
                                                int pageSize = 20, string? sort = null,
                                                bool includeDeleted = false)
Task<Group?>                    GetGroupAsync(long groupId)
Task<Group?>                    GetGroupByTokenAsync(string? token)
Task<Group?>                    GetGroupByInviteTokenAsync(string? inviteToken)
Task<IReadOnlyList<GroupMember>> ListMembersAsync(long groupId)        // inkl. .User
Task<GroupMember?>              GetMemberAsync(long groupId, long userId)
Task<bool>                      IsMemberAsync(long groupId, long userId)
Task<bool>                      IsGroupAdminAsync(long groupId, long userId)
Task<Result<Group>>             CreateGroupAsync(string name, string? description,
                                                 string? currency, long ownerUserId)
Task<Result<Group>>             UpdateGroupAsync(long groupId, string name,
                                                 string? description, string? currency)
Task<Result>                    SoftDeleteGroupAsync(long groupId)
Task<Result<GroupMember>>       AddMemberAsync(long groupId, long userId,
                                               int shareFactor = 1, bool isGroupAdmin = false)
Task<Result>                    RemoveMemberAsync(long groupId, long userId)
Task<Result>                    SetShareFactorAsync(long groupId, long userId, int shareFactor)
Task<Result>                    SetGroupAdminAsync(long groupId, long userId, bool isGroupAdmin)
Task<Result<string>>            RegenerateInviteTokenAsync(long groupId)   // Value = neuer Token
Task<Result>                    SetInviteEnabledAsync(long groupId, bool enabled)
Task<Result<GroupJoinResult>>   JoinByInviteTokenAsync(string? inviteToken, string displayName)
Task<Result<GroupJoinResult>>   JoinExistingUserByInviteTokenAsync(string? inviteToken, long userId)
```
`sort`: `name` (Default), `name_desc`, `created`, `created_desc`.
`CreateGroupAsync` macht `ownerUserId` automatisch zum Gruppen-Admin.
`ShareFactor` wird auf 1..100 geklemmt.
`RemoveMemberAsync` verweigert, wenn das Mitglied noch in Ausgaben, Anteilen
oder Zahlungen vorkommt (dann `MergeUsersAsync` benutzen).
`GetGroupByInviteTokenAsync` liefert `null`, wenn `AppConfig.AllowAnonymousJoin`
false oder `InviteEnabled` false ist.

`GroupJoinResult { Group Group; User User; bool CreatedNewMembership; }`
-> auf `/Join`: `await currentUser.SignInAsync(result.Value.User);`

Invite-Link-Form: `/Join?token={group.InviteToken}`.

### ExpenseService
```csharp
Task<PagedResult<Expense>>  ListExpensesAsync(long groupId, string? search = null,
                                              long? payerUserId = null, DateTime? fromDate = null,
                                              DateTime? toDate = null, int page = 1,
                                              int pageSize = 20, string? sort = null)
Task<Expense?>              GetExpenseAsync(long expenseId)   // inkl. Shares(+User), Receipts, PaidByUser
Task<long>                  GetTotalCentsAsync(long groupId)
Task<IReadOnlyList<string>> ListCategoriesAsync(long groupId)
Task<Result<Expense>>       SaveExpenseAsync(ExpenseEditModel model)   // Id = 0 -> Insert
Task<Result>                SoftDeleteExpenseAsync(long expenseId)
Task<IReadOnlyList<Receipt>> ListReceiptsAsync(long expenseId)
Task<Result<Receipt>>       SaveReceiptAsync(long expenseId, Stream stream,
                                             string fileName, string? contentType)
Task<Result>                DeleteReceiptAsync(long receiptId)
Task<ReceiptFile?>          GetReceiptPathAsync(long receiptId)
```
`sort`: `date_desc` (Default), `date`, `amount`, `amount_desc`,
`description`, `description_desc`, `payer`, `payer_desc`.

```csharp
ExpenseEditModel {
    long Id; long GroupId; string Description; long AmountCents; string Currency;
    long PaidByUserId; DateTime ExpenseDate; string? Category;
    List<ExpenseShareInput> Shares;      // leer = alle Mitglieder nach Gruppen-Faktor
}
ExpenseShareInput { long UserId; int ShareFactor = 1; long? ShareAmountCents; }
ReceiptFile { Receipt Receipt; string AbsolutePath; bool Exists; }
```
Validierung in `SaveExpenseAsync`: Beschreibung nicht leer, `AmountCents > 0`,
Gruppe existiert, Zahler ist Mitglied, Beteiligte sind Mitglieder, keine
Dubletten, Summe fester Anteile <= Gesamtbetrag (und == Gesamtbetrag, wenn es
keinen faktorbasierten Anteil gibt).

Belege: erlaubt sind `.jpg .jpeg .png .webp .heic .heif .gif .pdf`,
Groessenlimit `AppConfig.MaxReceiptSizeMb`. Anzeige im Markup ueber den
Endpoint **`/receipts/{receiptId}`** (autorisiert, prueft Gruppenmitgliedschaft).
Also z. B. `<img src="/receipts/@receipt.Id" alt="@receipt.FileName" />`.

### PaymentService
```csharp
Task<PagedResult<Payment>>  ListPaymentsAsync(long groupId, string? search = null,
                                              long? memberUserId = null, DateTime? fromDate = null,
                                              DateTime? toDate = null, int page = 1,
                                              int pageSize = 20, string? sort = null)
Task<Payment?>              GetPaymentAsync(long paymentId)
Task<Result<Payment>>       SavePaymentAsync(PaymentEditModel model)   // Id = 0 -> Insert
Task<Result>                SoftDeletePaymentAsync(long paymentId)
Task<long>                  GetTotalCentsAsync(long groupId)
```
`sort`: `date_desc` (Default), `date`, `amount`, `amount_desc`,
`from`, `from_desc`, `to`, `to_desc`.
```csharp
PaymentEditModel { long Id; long GroupId; long FromUserId; long ToUserId;
                   long AmountCents; DateTime PaymentDate; string? Note; }
```

### BalanceService
```csharp
Task<BalanceResult> CalculateBalancesAsync(long groupId)
static IReadOnlyList<(long UserId, long AmountCents)> Distribute(long amountCents,
                                                                 IReadOnlyList<ExpenseShareInput> shares)
```
```csharp
BalanceResult {
    long GroupId; string Currency; long TotalExpensesCents; long TotalPaymentsCents;
    IReadOnlyList<MemberBalance> Balances;      // absteigend nach BalanceCents
    IReadOnlyList<Settlement> Settlements;
}
MemberBalance { long UserId; string DisplayName; int ShareFactor;
                long PaidCents; long OwedCents; long BalanceCents;
                long ExpensesPaidCents; long PaymentsSentCents; long PaymentsReceivedCents;
                bool IsCreditor; bool IsDebtor; }
Settlement    { long FromUserId; string FromDisplayName;
                long ToUserId;   string ToDisplayName;
                long AmountCents; string? PayPalUrl; }
```
Rechenregel: Ausgabenanteil nach `ExpenseShares.ShareFactor`, bzw. nach
`GroupMembers.ShareFactor`, wenn eine Ausgabe keine explizit erfassten Anteile
hat. Feste `ShareAmountCents` gehen vorweg, der Rest wird nach Faktor verteilt
(Largest-Remainder, Cent-Summe stimmt exakt). Zahlungen werden gegengerechnet:
`BalanceCents = (Ausgaben bezahlt + Zahlungen geleistet) - (Anteil + Zahlungen erhalten)`.
**Positiv = bekommt Geld zurueck**, negativ = schuldet Geld.
`Settlements` = greedy minimaler Ausgleich (groesster Schuldner -> groesster
Gutschriftinhaber). `PayPalUrl` ist `https://paypal.me/{handle}/{betrag}{CUR}`
oder `null` (kein Handle / E-Mail-Adresse hinterlegt).

### HistoryService
```csharp
Task<HistoryEntry>          LogAsync(long? groupId, long? userId, string entityType,
                                     long? entityId, string action, string summary,
                                     string? detailsJson = null, bool saveChanges = true)
Task<PagedResult<HistoryEntry>> ListHistoryAsync(long? groupId, string? search = null,
                                                 string? action = null, int page = 1,
                                                 int pageSize = 20,
                                                 DateTime? fromUtc = null,
                                                 DateTime? toUtc = null)
Task<IReadOnlyList<string>> ListActionsAsync(long? groupId)
```
`fromUtc` / `toUtc` filtern auf `CreateDate` (utc). Wer nach einem *lokalen* Tag
filtert, rechnet die Tagesgrenzen selbst um (siehe `Pages/Groups/History`).
Konstanten: `HistoryService.Actions.{Created,Updated,Deleted,Joined,Left,Merged,Uploaded,Settled,SignedIn,SignedOut}`,
`HistoryService.EntityTypes.{Group,GroupMember,Expense,Payment,Receipt,User,AppConfig}`.
Die Services schreiben ihre History selbst - Razor Pages muessen das
normalerweise **nicht** nachziehen.

### SessionService
```csharp
Task<string>        CreateSessionAsync(long userId, string? userAgent)   // -> Token
Task<UserSession?>  ResolveSessionAsync(string? token)                   // inkl. .User
Task                TouchAsync(string? token)
Task                EndSessionAsync(string? token)
Task                EndAllSessionsForUserAsync(long userId)
Task<int>           CleanupExpiredAsync()
Task<IReadOnlyList<UserSession>> ListSessionsForUserAsync(long userId)
```
Login/Logout laufen ueber `CurrentUserService`, nicht direkt hierueber.

### AppConfigService (Singleton)
```csharp
AppConfig       Current                  // gecacht, ohne await nutzbar (fuer Views)
Task<AppConfig> GetAsync()
Task<Result>    SaveAsync(AppConfig config)
void            InvalidateCache()
```
```csharp
AppConfig { string AppName = "MatSplit"; string DefaultCurrency = "EUR";
            bool AllowAnonymousJoin = true; int SessionLifetimeDays = 30;
            int MaxReceiptSizeMb = 10; AppConfig Normalized(); }
```
Datei: `/data/config/appconfig.json`, wird beim Start mit Defaults erzeugt.
`SaveAsync` normalisiert (SessionLifetimeDays 1..365, MaxReceiptSizeMb 1..100).

---

## 5. Endpoints ausserhalb der Razor Pages

| Route | Auth | Zweck |
|---|---|---|
| `GET /health` | anonym | Docker-Healthcheck, `{ status, utc, version }` |
| `GET /receipts/{id:long}` | `AuthenticatedUser` + Gruppenmitglied | streamt den Beleg aus `/data/receipts` |
| `GET /api/sync/ping` | `AuthenticatedUser` | `{ status, utc, userId, displayName }` |
| `GET /api/sync/status` | `AuthenticatedUser` | Server-Sicht auf die Outbox (Zaehler, Zeitstempel) |
| `POST /api/sync/expenses` | `AuthenticatedUser` | Liste offline erfasster Ausgaben |
| `POST /api/sync/payments` | `AuthenticatedUser` | Liste offline erfasster Zahlungen |
| `POST /Account/Profile?handler=Theme` | `AuthenticatedUser` | Theme-Umschalter des Layouts (Feld `theme`) |

Alles unter `/api` antwortet **ohne** Login-Redirect: die Cookie-Events
`OnRedirectToLogin` / `OnRedirectToAccessDenied` liefern dort `401` bzw. `403`,
damit der Service Worker eine fehlgeschlagene Synchronisierung nicht mit einer
HTML-Loginseite verwechselt. Fuer alle anderen Pfade bleibt der Redirect.

`POST /api/sync/expenses` Body = `SyncExpenseDto[]`:
```json
[{ "clientId":"c1", "groupId":1, "description":"Eis", "amountCents":450,
   "currency":"EUR", "paidByUserId":0, "expenseDate":"2026-08-20T00:00:00Z",
   "category":"Snacks",
   "shares":[{ "userId":2, "shareFactor":1, "shareAmountCents":null }] }]
```
Antwort `SyncExpenseResponseDto`:
```json
{ "accepted":1, "rejected":0,
  "results":[{ "clientId":"c1", "expenseId":42, "success":true, "error":null }] }
```
`paidByUserId = 0` -> der eingeloggte User. Leere `shares` -> alle Mitglieder
nach Gruppen-Faktor. Jedes Element wird einzeln quittiert, damit der Service
Worker fehlgeschlagene Eintraege in seiner Outbox behalten kann.
`SyncApi.MapSyncApi(this WebApplication app)` ist die stabile Signatur;
der Sync-Agent baut **nur diese Datei** weiter aus.

---

## 6. Daten-Volume & Konfiguration

`MatSplitPaths` (Singleton, injizierbar):
`DataRoot`, `DatabaseFile`, `DatabaseDirectory`, `ConfigDirectory`, `ConfigFile`,
`ReceiptsDirectory`, `KeysDirectory`, `LogsDirectory`, `SqliteConnectionString`,
`EnsureDirectories()`, `ResolveReceiptPath(storagePath)` (mit Traversal-Schutz).

Aufloesung des Roots in dieser Reihenfolge:
1. Env `MATSPLIT_DATA_DIR`
2. Config `Data:DataDirectory`
3. Default `/data` (Container). Ist `/data` nicht anlegbar (Windows-Host,
   lokales `dotnet run`), wird das Volume ins **Repo-Root** gelegt:
   `<repoRoot>/data` (erkannt an `.git` bzw. `*.sln`).
   `<contentRoot>/data` wird bewusst vermieden, weil das auf einem
   case-insensitiven Dateisystem mit dem Quellordner `src/MatSplit.Web/Data`
   kollidieren wuerde.

Die SQLite-Datei kann zusaetzlich per `Data:DatabasePath` gesetzt werden
(Default `<DataRoot>/db/matsplit.db`).

Layout:
```
/data/db/matsplit.db          SQLite (WAL)
/data/config/appconfig.json   AppConfig
/data/receipts/<gid>/<eid>/   Belegbilder
/data/keys/                   DataProtection-Keys (Cookies ueberleben Restart)
/data/logs/                   reserviert
```
Beim Start: Verzeichnisse anlegen -> `EnsureCreatedAsync()` -> `PRAGMA journal_mode=WAL`
-> `DbInitializer.SeedAsync` -> `SessionService.CleanupExpiredAsync()`.

Seed-Daten: Admin `admin` / Passwort `admin` (E-Mail `admin@matsplit.local`) und
die Demo-Gruppe "Demo: Urlaub Mallorca" mit 4 Mitgliedern (Familie Meyer hat
`ShareFactor = 3`), drei Ausgaben (davon eine mit festem Anteil) und einer Zahlung.
Die Demo-Gruppe wird nur erzeugt, wenn noch keine Gruppe existiert.

---

## 7. Konventionen fuer Feature-Agenten

- Kein direkter `AppDbContext`-Zugriff aus Razor Pages. Wenn eine Abfrage fehlt:
  im Report melden, nicht selbst am Core vorbei bauen.
- Betraege im UI als Cent -> `decimal` erst zur Anzeige:
  `(model.AmountCents / 100m).ToString("N2")`, Eingabe wieder in Cent umrechnen.
  Kultur fuer die Anzeige: `de-DE`.
- Fehlermeldungen aus `Result.Error` unverändert in `ModelState` uebernehmen
  (sie sind bereits deutsch).
- CRUD: Liste + separate Edit-Unterseite (`?id=`), `Id = 0`/kein `id` = Neuanlage.
- Nach Profil-, Namens- oder Theme-Aenderung `CurrentUserService.RefreshSignInAsync()`
  aufrufen, damit Menue und `data-theme` sofort passen.
- `/Error` und `Pages/Shared/_Layout.cshtml` liegen beim UI-Agenten; Program.cs
  erwartet im Non-Development-Fall eine Seite `/Error`.

## 8. Bekannte Abweichungen von der Spezifikation

- **PK-Spaltentyp**: SQLite erlaubt `AUTOINCREMENT` nur auf `INTEGER PRIMARY KEY`.
  Ein explizites `BIGINT` bricht `EnsureCreated()`. `INTEGER` ist in SQLite
  ohnehin 64 Bit, die Fachanforderung "BIGINT / long" ist damit erfuellt.
- **Methodennamen** tragen das `Async`-Suffix (siehe Abschnitt 1).
- **`AppConfigService` ist Singleton** (nicht Scoped), weil er die JSON-Datei
  cacht und nur von Singletons abhaengt.
- **PayPal-Links bei E-Mail-Adressen**: `paypal.me` kennt nur Handles. Steht in
  `User.PayPalAddress` eine E-Mail-Adresse, liefert
  `Settlement.PayPalUrl` `null`.
- **`.gitignore`**: Die Regel `data/` matchte unter Windows (case-insensitive)
  auch den Quellordner `src/MatSplit.Web/Data`. Die Regel ist deshalb auf
  `/data/` verankert (fuehrender Slash = nur Repo-Root); das frueher noetige
  lokale `src/MatSplit.Web/.gitignore` ist entfallen.
- **Seitenzahl `?page=`**: In Razor Pages ist `page` ein reservierter
  Route-Value (er traegt den Seitenpfad). `[BindProperty(Name = "page")]`
  erzeugt darum einen ModelState-Fehler, der jedes Formular der Seite
  blockiert, und `RedirectToPage(new { page = 1 })` wirft
  `InvalidOperationException`. Listenseiten lesen die Seitenzahl daher ueber
  `MsPaging.ReadPageNumber(Request)` und leiten mit `LocalRedirect`/`Redirect`
  auf einen selbst gebauten Query-String um.
- **Kultur beim Model Binding**: `app.UseRequestLocalization()` setzt die
  Request-Kultur auf `InvariantCulture`, weil `<input type="number">` immer
  invariant postet (`30.00`). Anzeige-Strings formatieren ausschliesslich mit
  explizit angegebener Kultur (`de-DE` bzw. invariant), sind davon also nicht
  betroffen. Zusaetzlich sind die Model-Binding-Meldungen in Program.cs auf
  Deutsch gesetzt (`MvcOptions.ModelBindingMessageProvider`).
- **PWA-Icons**: `apple-touch-icon.png` (180x180), `icon-192.png` und
  `icon-512.png` sind echte PNGs (iOS akzeptiert fuer `apple-touch-icon` kein
  SVG). `icon-512.svg` bleibt als groessenlose `"sizes": "any"`-Variante im
  Manifest. Das Manifest heisst `manifest.webmanifest` (korrekter MIME-Typ)
  statt `manifest.json`.
