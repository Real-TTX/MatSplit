# MatSplit als PWA - Offline, Sync, iOS/Android

Diese Datei beschreibt die PWA-Schicht: Service Worker, Offline-Outbox,
Sync-API, Installations-Hinweis und die Eigenheiten von iOS und Android.

## 1. Beteiligte Dateien

| Datei | Aufgabe |
| --- | --- |
| `src/MatSplit.Web/wwwroot/sw.js` | Service Worker: Precache, Caching-Strategien, Background-Sync |
| `src/MatSplit.Web/wwwroot/offline.html` | Fallback-Seite fuer Navigationen ohne Netz |
| `src/MatSplit.Web/wwwroot/js/offline-sync.js` | Outbox (IndexedDB), Formular-Abfang, Replay, Banner-Steuerung |
| `src/MatSplit.Web/wwwroot/js/pwa-install.js` | `beforeinstallprompt` (Android) + iOS-Hinweis, Standalone-Erkennung |
| `src/MatSplit.Web/Pages/Shared/_OfflineBanner.cshtml` | Balken oben (Markup + Styles) und Einbindung der beiden Skripte |
| `src/MatSplit.Web/Api/SyncApi.cs` | `/api/sync/ping`, `/status`, `POST /expenses`, `POST /payments` |
| `src/MatSplit.Web/wwwroot/manifest.webmanifest` | Manifest (gehoert dem UI-Agenten) |

Registriert wird der Service Worker vom Layout (`navigator.serviceWorker.register('/sw.js')`).

### Einbindung im Layout (erledigt)

`Pages/Shared/_Layout.cshtml` rendert das Partial als **erstes Element in
`<body>`** - vor `<a class="ms-skip">` bzw. `<div class="ms-shell">`:

```cshtml
<body class="ms-app">
    <partial name="_OfflineBanner" />
    <a class="ms-skip" href="#ms-main">Zum Inhalt springen</a>
```

Das Partial bringt seine Styles selbst mit und laedt `js/offline-sync.js` und
`js/pwa-install.js` (beide `defer`). Es sind keine weiteren Layout-Aenderungen
noetig. Wer die Styles lieber in `wwwroot/css/` haben will, kann den
`<style>`-Block 1:1 nach `controls.css` verschieben - die Klassennamen sind
`.ms-offline-banner*` und `.ms-pwa-install*`.

## 2. Was offline funktioniert - und was nicht

| Funktion | Offline | Bemerkung |
| --- | --- | --- |
| App-Shell (CSS, JS, Logo, Icons) | ja | Precache beim `install`, `CACHE_VERSION` = `v2` |
| Bereits besuchte Seiten | nein | HTML ist benutzerspezifisch, wird nie gecacht |
| Navigation ohne Netz | Fallback | `offline.html` mit Status und Warteschlange |
| Neue **Ausgabe** erfassen | ja | landet in IndexedDB `pendingExpenses` |
| Neue **Zahlung** erfassen | ja | landet in IndexedDB `pendingPayments` |
| Bestehende Eintraege bearbeiten/loeschen | nein | Konfliktrisiko, wird mit Hinweis abgelehnt |
| Belegfoto hochladen | nein | Datei-Uploads werden nicht gequeued (Hinweis im Banner) |
| Bereits angezeigte Belegbilder | ja | `/receipts/*` liegen in einem eigenen Runtime-Cache (max. 60) |
| Kontostand, Historie, Listen | nein | brauchen den Server |
| Anmelden / Beitreten | nein | Cookie-Login braucht den Server |

Die Warteschlange ueberlebt Neustart und App-Schliessen (IndexedDB) und wird
automatisch geleert bei: `online`-Event, Seitenaufruf mit Netz, Rueckkehr in den
Vordergrund (`visibilitychange`), Background-Sync-Event und Klick auf
"Jetzt synchronisieren" im Balken.

## 3. Caching-Strategien im Service Worker

| Anfrage | Strategie |
| --- | --- |
| Navigation (`mode === 'navigate'`, HTML) | network-first, bei Fehler `offline.html` |
| `/css/*`, `/js/*`, `/img/*`, `/lib/*`, `manifest.webmanifest` | stale-while-revalidate (Cache-Treffer sofort, Aktualisierung im Hintergrund) |
| `/receipts/*` | network-first, Kopie im Media-Cache (auf 60 Eintraege begrenzt) |
| `/api/*`, `/health` | network-only, nie Cache |
| Alles ausser GET | wird durchgelassen (kein Cache) |

Cache-Namen sind versioniert (`matsplit-shell-v1`, `matsplit-assets-v1`,
`matsplit-media-v1`). Beim `activate` werden alle `matsplit-*`-Caches gelöscht,
die nicht zur aktuellen Version gehoeren; `skipWaiting()` und `clients.claim()`
sorgen dafuer, dass eine neue Version sofort greift. Bei einer Aenderung an
`sw.js` oder an der Precache-Liste muss `CACHE_VERSION` hochgezaehlt werden
(`v1` -> `v2`).

Weil CSS/JS ueber `asp-append-version="true"` mit `?v=...` ausgeliefert werden,
laufen alle Cache-Lookups mit `ignoreSearch: true`.

## 4. Sync-Protokoll

Alle Endpunkte liegen unter `/api/sync` und verlangen die Policy
`AuthenticatedUser` (Cookie wird per `credentials: 'same-origin'` mitgesendet).

| Route | Zweck |
| --- | --- |
| `GET /api/sync/ping` | Lebenszeichen: `{ status, utc, userId, displayName }` |
| `GET /api/sync/status` | Rolle, `defaultCurrency`, `maxReceiptSizeMb` und alle Gruppen des Users inkl. Mitglieder (userId, displayName, shareFactor) |
| `POST /api/sync/expenses` | Batch offline erfasster Ausgaben |
| `POST /api/sync/payments` | Batch offline erfasster Zahlungen |

Request `POST /api/sync/expenses` (max. 200 Eintraege pro Anfrage):

```json
[{ "clientId": "11111111-2222-4333-a444-555555555555",
   "groupId": 1, "description": "Eis am Strand", "amountCents": 450,
   "currency": "EUR", "paidByUserId": 0,
   "expenseDate": "2026-08-19T00:00:00Z", "category": "Snacks",
   "shares": [{ "userId": 2, "shareFactor": 1, "shareAmountCents": null }] }]
```

Request `POST /api/sync/payments`:

```json
[{ "clientId": "aaaaaaaa-2222-4333-a444-555555555555",
   "groupId": 1, "fromUserId": 3, "toUserId": 2,
   "amountCents": 2500, "paymentDate": "2026-08-18T00:00:00Z",
   "note": "Bar am Flughafen" }]
```

Antwort (beide Endpunkte, `expenseId` bzw. `paymentId`):

```json
{ "accepted": 1, "rejected": 1,
  "results": [
    { "clientId": "1111...", "expenseId": 4, "success": true,  "error": null },
    { "clientId": "4444...", "expenseId": 0, "success": false, "error": "Bitte eine Beschreibung angeben." }
  ],
  "acceptedClientIds": ["1111..."],
  "rejectedClientIds": ["4444..."] }
```

Regeln:

- `paidByUserId` / `fromUserId` gleich `0` bedeutet "der angemeldete User".
- Leeres `shares` bedeutet "alle Mitglieder nach Gruppen-Faktor".
- Jeder Eintrag wird **einzeln** quittiert. Nur bestaetigte Eintraege loescht der
  Client aus der Outbox; abgelehnte bleiben mit `lastError` und `attempts` liegen
  und werden im Balken als Fehler gemeldet. **Der Server gewinnt immer** - es
  wird nichts erzwungen und nichts stillschweigend ueberschrieben.
- HTTP 400 gibt es nur fuer eine leere oder zu grosse Liste, HTTP 401 fuer eine
  abgelaufene Session.

### Idempotenz (gewaehlte Loesung)

Jeder Outbox-Eintrag traegt eine `clientId` (UUID v4, vom Browser erzeugt).
Nach einem erfolgreichen Schreibvorgang legt `SyncApi` **zusaetzlich** einen
`HistoryEntry` an:

- `Action` = `"Synced"` (Konstante `SyncApi.SyncAction`)
- `EntityType` = `"Expense"` bzw. `"Payment"`, `EntityId` = neue Id
- `Summary` = `"Ausgabe wurde offline erfasst und synchronisiert (Sync-Id <clientId>)."`
- `DetailsJson` = `{"source":"offline-sync","clientId":"...","entityType":"...","entityId":42,"receivedUtc":"..."}`

Vor jedem Insert sucht `SyncApi` ueber `HistoryService.ListHistoryAsync(groupId,
search: clientId, action: "Synced")` nach diesem Marker. Wird er gefunden,
antwortet der Endpunkt mit `success: true` und der **bereits gespeicherten Id**,
ohne einen zweiten Datensatz anzulegen. Ein doppelter Replay (Seite und Service
Worker flushen gleichzeitig, Verbindungsabbruch nach dem Schreiben, App-Neustart)
erzeugt also keine Dublette.

Warum so: `Expenses.Category` ist ein Fachfeld und wird bewusst **nicht** als
Ablage fuer technische Ids missbraucht. Die `clientId` steht sowohl in `Summary`
(nur `Summary`, `EntityType` und `User.DisplayName` sind ueber
`ListHistoryAsync(search:)` durchsuchbar) als auch maschinenlesbar in
`DetailsJson`. Damit bleibt die Loesung ohne Schemaaenderung und ohne direkten
`AppDbContext`-Zugriff auskommen.

Konsequenzen, die man kennen muss:

- Pro synchronisiertem Eintrag stehen **zwei** Zeilen in der Historie: die des
  Services (`Created`) und der Sync-Marker (`Synced`). Der Marker ist in
  `/Groups/History?groupId=…` sichtbar und ueber den Aktionsfilter "Synced"
  filterbar - gewollt, damit nachvollziehbar bleibt, was offline entstanden ist.
- Ohne `clientId` (Feld ist optional) gibt es keine Dublettenerkennung. Der
  mitgelieferte Client setzt immer eine.
- `clientId` wird validiert: maximal 64 Zeichen, nur `A-Z a-z 0-9 - _ . :`.
  Alles andere wird mit "Die ClientId hat ein unerlaubtes Format." abgelehnt
  (verhindert Wildcards in der LIKE-Suche).

## 5. Wie die Offline-Erfassung an den Formularen haengt

`offline-sync.js` faengt `submit` in der Capture-Phase ab, **nur** wenn
`navigator.onLine === false` ist. Online verhaelt sich die App voellig normal
(klassisches POST/Redirect/GET).

Erkannt wird ein Formular ueber den Pfad
(`/Groups/Expenses/Edit`, `/Groups/Payments/Edit`) oder explizit ueber
`data-ms-offline="expense|payment"`. `data-ms-offline="off"` schaltet die
Uebernahme fuer ein Formular ab.

Auf einer Seite mit mehreren Formularen (z. B. Beleg-Upload oder Beleg loeschen
neben dem Ausgabenformular) wird nur das Formular uebernommen, das das
Hauptfeld traegt (`Description` bei Ausgaben, `ToUserId` bei Zahlungen).
Alle anderen Formulare bleiben unangetastet und scheitern offline wie gewohnt
am fehlenden Netz.

Die Feldzuordnung laeuft ueber den letzten Namensteil des Formularfeldes
(`Input.Description` -> `description`), passend zu den bestehenden Seiten:

| Rolle | erkannte Feldnamen |
| --- | --- |
| Beschreibung | `Description`, `Beschreibung`, `Bezeichnung`, `Title` |
| Betrag | `Amount`, `AmountEuro`, `Betrag`, `Summe` (Cent bei `AmountCents`) |
| Waehrung | `Currency`, `Waehrung` |
| Zahler | `PaidByUserId`, `PaidBy`, `Payer` |
| Datum | `ExpenseDate`, `PaymentDate`, `Date`, `Datum` |
| Kategorie | `Category`, `Kategorie` |
| Von / An | `FromUserId`, `ToUserId` |
| Notiz | `Note`, `Notiz`, `Bemerkung` |
| Aufteilung | `ShareMode` + `Shares[i].UserId / IsIncluded / ShareFactor / Amount` |
| Datensatz-Id | `Id` (Wert != 0 => Bearbeiten, wird offline abgelehnt) |

`ShareMode` wird wie in `Pages/Groups/Expenses/Edit.cshtml.cs` interpretiert:
`Equal` sendet eine leere Anteilsliste, `Factors` die Faktoren der ausgewaehlten
Mitglieder, `Amounts` die festen Cent-Betraege (Summe muss dem Gesamtbetrag
entsprechen, sonst wird der Eintrag gar nicht gequeued). Beim Checkbox-Paar
`true`/`false` (ASP.NET rendert Checkbox + Hidden) gewinnt wie beim Model-Binder
der **erste** Wert.

Betraege werden de-DE-tolerant gelesen: `12,50`, `12.50`, `1.234,56`,
`1 234,56 EUR` sind alle in Ordnung. `1.234` wird als Tausendergruppe gelesen
(= 123400 Cent), weil das Geldfeld in de-DE mit Komma arbeitet.

### Wenn eine Seite die Automatik nicht mag

Fuer Seiten mit eigenem Markup gibt es zwei Wege:

```html
<!-- 1) explizite Rollen pro Feld -->
<form method="post" data-ms-offline="expense" data-ms-offline-mode="insert">
    <input data-ms-offline-field="description" name="Foo.Bar" />
    <input data-ms-offline-field="amountCents" name="Foo.Cents" />
</form>
```

```js
// 2) komplett selbst in die Outbox schreiben
await window.MatSplitOffline.queueExpense({
    groupId: 1, description: 'Eis', amountCents: 450,
    currency: 'EUR', paidByUserId: 0, expenseDate: null, shares: []
});
```

`window.MatSplitOffline` bietet ausserdem `state()`, `list()`, `flush()`,
`discard(kind, clientId)`, `requestSync()` und `refresh()`. Bei jeder
Zustandsaenderung feuert `document` das Event `matsplit:offline-state` mit
`{ online, status, pending, failed }`.

Am `<html>`-Element stehen zusaetzlich zwei Attribute fuer CSS und andere
Skripte zur Verfuegung - **absichtlich anders benannt als die Marker-Attribute
im Markup**, damit `document.querySelector('[data-ms-offline-banner]')` nie das
Root-Element trifft (ein `hidden` am `<html>` wuerde die ganze Seite ausblenden):

| Attribut / Variable am `<html>` | Werte |
| --- | --- |
| `data-ms-connection` | `online` / `offline` |
| `data-ms-banner` | `visible` / `hidden` |
| `--ms-offline-banner-height` | gemessene Balkenhoehe, z. B. `40px` |

`window.MatSplitInstall` bietet `isStandalone()`, `canPrompt()`, `prompt()`,
`show()` und `dismiss()` - damit kann z. B. das Profil eine Schaltflaeche
"App installieren" anbieten.

## 6. iOS-Einschraenkungen

- **Kein Background Sync.** `SyncManager` existiert in Safari nicht. Deshalb
  synchronisiert `offline-sync.js` auf iOS ueber das `online`-Event, den
  Seitenaufruf und `visibilitychange`. Praktisch heisst das: die App muss
  einmal kurz geoeffnet werden, damit die Warteschlange rausgeht.
- **Kein Web-Push / kein Periodic Sync** in installierten Web-Apps (ausserhalb
  der neueren Home-Screen-Web-Push-Unterstuetzung, die MatSplit nicht nutzt).
- **Cache-Budget.** Safari begrenzt den Speicher pro Origin (Groessenordnung
  ~50 MB fuer Cache Storage/IndexedDB) und **loescht Daten nach ca. 7 Tagen
  ohne Nutzung** (ITP), bei installierten Web-Apps grosszuegiger. Der Media-Cache
  ist daher auf 60 Belegbilder begrenzt, die Outbox haelt nur Textdaten.
- **Kamera.** `getUserMedia` funktioniert in installierten Web-Apps erst ab
  iOS 14.3 und nur ueber HTTPS. Der zuverlaessige Weg ist
  `<input type="file" accept="image/*" capture="environment">` - genau das
  rendert `ms-field type="file" capture="environment"`. Der getUserMedia-Pfad in
  `site.js` ist nur Komfort.
- **Installation** laeuft ausschliesslich ueber "Teilen -> Zum Home-Bildschirm";
  `beforeinstallprompt` gibt es nicht. `pwa-install.js` zeigt deshalb genau
  diesen Hinweis - nur in Safari (nicht in Chrome/Firefox fuer iOS, die das
  nicht koennen) und nur wenn die App nicht schon standalone laeuft.
- **Notch / Dynamic Island.** `viewport-fit=cover` steht im Layout, der
  Offline-Balken und der Installationshinweis rechnen mit
  `env(safe-area-inset-top/bottom/left/right)`.
- **Statusleiste.** `apple-mobile-web-app-status-bar-style="default"` haelt die
  Statusleiste lesbar; bei `black-translucent` wuerde der Balken darunter
  rutschen.
- Nach dem Hinzufuegen zum Home-Bildschirm startet iOS eine **eigene**
  Cookie-Umgebung: einmal neu anmelden ist normal.

## 7. Android / Chromium

- `beforeinstallprompt` wird abgefangen, der Prompt kommt erst auf Klick
  ("Installieren"), nie doppelt: das gespeicherte Event wird nach dem ersten
  `prompt()` verworfen, `appinstalled` blendet den Hinweis dauerhaft aus.
- "Spaeter"/Schliessen setzt `matsplit.pwa.installDismissed` in `localStorage`
  und haelt den Hinweis 30 Tage fern.
- Background Sync (`registration.sync.register('matsplit-sync')`) laeuft auch
  bei geschlossener App. Ist ein Fenster offen, uebernimmt das Fenster den
  Flush (bessere Fehlermeldungen), sonst arbeitet `sw.js` die Outbox selbst ab.
  Doppelte Zustellung ist wegen der `clientId` unkritisch.

## 8. Testanleitung

**Vorbereitung.** Service Worker brauchen `https://` oder `http://localhost`.
Ueber die LAN-IP (`http://192.168.x.x:4774`) registriert sich `sw.js` nicht -
dort entweder einen Reverse-Proxy mit TLS nutzen oder in Chrome unter
`chrome://flags/#unsafely-treat-insecure-origin-as-secure` die Origin freigeben.

**Desktop (Chrome/Edge DevTools)**

1. `docker compose -f docker-compose.dev.yml up --build`, dann
   `http://localhost:4774` oeffnen und anmelden.
2. DevTools -> Application -> Service Workers: Status "activated", Quelle `/sw.js`.
3. Application -> Cache Storage: `matsplit-shell-v1` enthaelt `offline.html`,
   `site.css`, `offline-sync.js`, Icons.
4. DevTools -> Network -> "Offline" aktivieren. Neu laden: `offline.html`
   erscheint, der obere Balken wird orange ("Offline ...").
5. Weiter offline: `/Groups/Expenses/Edit?groupId=1` war noch nicht geladen -
   dafuer eine bereits offene Ausgabenseite verwenden, Formular ausfuellen,
   speichern. Erwartung: Balken meldet "Ausgabe offline gespeichert ...",
   Application -> IndexedDB -> `matsplit-offline` -> `pendingExpenses` hat einen
   Eintrag mit `clientId`.
6. "Offline" wieder abschalten. Erwartung: Balken wechselt auf
   "wird synchronisiert ...", danach ist der Store leer und die Ausgabe steht in
   `/Groups/Expenses?groupId=1`.
7. Background Sync pruefen: DevTools -> Application -> Background Services ->
   Background Sync -> Record, offline erfassen, Tab schliessen, online gehen.
   Der Tag `matsplit-sync` muss auftauchen.
8. Idempotenz pruefen: im Application-Tab den erledigten Eintrag manuell wieder
   in `pendingExpenses` einfuegen (gleiche `clientId`) und `flush()` in der
   Konsole aufrufen: `MatSplitOffline.flush()`. Erwartung: `accepted: 1`, aber
   **keine** zweite Ausgabe in der Liste.

**API direkt (ohne Browser)**

```bash
# Anmelden und Cookie sichern
TOKEN=$(curl -s -c jar.txt http://localhost:4774/Account/Login \
  | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' \
  | sed 's/.*value="\([^"]*\)".*/\1/' | head -1)
curl -s -b jar.txt -c jar.txt -X POST http://localhost:4774/Account/Login \
  --data-urlencode "Input.EmailOrName=admin" --data-urlencode "Input.Password=admin" \
  --data-urlencode "__RequestVerificationToken=$TOKEN" -o /dev/null

curl -s -b jar.txt http://localhost:4774/api/sync/status
curl -s -b jar.txt -H "Content-Type: application/json" \
  -d '[{"clientId":"test-1","groupId":1,"description":"CLI","amountCents":199,"shares":[]}]' \
  http://localhost:4774/api/sync/expenses
# gleicher Aufruf ein zweites Mal -> gleiche expenseId, keine Dublette
```

**Mobil**

- Android/Chrome: Seite oeffnen, Installationshinweis erscheint nach ~1 s,
  installieren, Flugmodus an, Ausgabe erfassen, Flugmodus aus, App oeffnen.
- iOS/Safari: Hinweis "Teilen -> Zum Home-Bildschirm" pruefen, installieren,
  Flugmodus an, Ausgabe erfassen, Flugmodus aus, App **oeffnen** (ohne
  Oeffnen kein Sync, siehe Abschnitt 6). Notch pruefen: Balken darf nicht unter
  der Dynamic Island verschwinden.
- Lighthouse (Chrome) -> Kategorie "Progressive Web App" bzw. "Installable"
  fuer einen schnellen Gesamtcheck.

**Nach Aenderungen an `sw.js`**: `CACHE_VERSION` erhoehen, in DevTools
"Update on reload" aktivieren oder `Unregister` + Neuladen, sonst haelt der alte
Worker die alten Assets.

## 9. Bekannte Grenzen

- Bearbeiten und Loeschen sind offline gesperrt (bewusst: der Server gewinnt,
  ein Offline-Merge waere fachlich nicht eindeutig).
- Datei-Uploads (Belege) werden nicht gequeued. Der Nutzer bekommt beim
  Offline-Speichern den Hinweis, das Foto spaeter nachzureichen.
- HTML-Seiten werden nie gecacht. Wer offline die App startet, landet auf
  `offline.html` und kann von dort nur bereits gecachte Assets nutzen -
  eine echte Offline-Ansicht der Listen waere eine eigene Client-Datenhaltung
  und ist fuer v1 nicht vorgesehen.
- Ohne JavaScript gibt es weder Offline-Erfassung noch Banner; die App bleibt
  ansonsten voll bedienbar.
