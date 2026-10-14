# MatSplit UI (MatUi + ms-*)

Verbindliche Referenz fuer alle Seiten. MatSplit nutzt das Design aller Mat-Apps: **MatUi**, das Toolkit
aus dem Repository *MatDesign* (Regelwerk, Tokens, CSS, ein Skript, Icon-Sprite). Eine Seite wird aus den
`ms-*`-Tag-Helpern und - wo es keinen gibt - aus `mat-*`-Klassen des Toolkits gebaut. Die Tag-Helper
(`src/MatSplit.Web/Ui/`) rendern MatUi-Markup; sie halten Reihenfolge, Ids, Validierungsmeldungen und
Antiforgery-Token an einer Stelle zusammen.

## 0. Wie das Design in die App kommt

| Was | Wo |
| --- | --- |
| Toolkit (Kopie, nicht verlinkt: die App muss offline laufen) | `wwwroot/matui/matui.css`, `matui.js`, `matui.boot.js`, `matui-icons.svg` |
| Akzentfarbe der App | `[data-app="matsplit"]` im Toolkit (`#bf5610`, Registry von MatDesign); `data-app` steht am `<html>` |
| Was nicht im Toolkit ist (Offline-Balken, Installationshinweis, Kamera, Blaecke der Gruppenseiten, iOS/Android-Feinheiten) | `wwwroot/css/app.css`, nur Tokens (`--mat-*`), keine Farbwerte, flach |
| Skript der App (Farbe der Systemleisten, Tastatur, Teilen, Kamera, Komma bei Betraegen) | `wwwroot/js/site.js` |
| Icons, die nur MatSplit braucht (Kamera, Waage, Merge, PayPal, WhatsApp, Telegram, Euro, Ausgabe, Logo) | `Pages/Shared/_AppIcons.cshtml` (`#ms-i-*`); alles andere aus dem Toolkit-Sprite (`#mat-i-*`) |

**Toolkit aktualisieren:** Dateien aus einer Version von MatDesign neu nach `wwwroot/matui/` kopieren
(Download-Seite von MatDesign oder `/releases/<version>/`), `CHANGELOG.md` des Toolkits lesen, `sw.js`
`CACHE_VERSION` hochzaehlen. Die Dateien nicht von Hand aendern - was fehlt, gehoert nach `app.css`.

Regeln, die MatDesign vorgibt und die hier gelten (Auszug): kein Inline-Style, kein Inline-Skript fuer Verhalten
des Toolkits, keine Farbwerte ausserhalb der Tokens, keine Verlaeufe/Schatten/Blur, Auswahl ist eine Toenung
(kein Balken an einer Seite), Icons nur aus dem Sprite (kein Emoji), Buttons unter dem Inhalt von positiv nach
negativ, Rueckfragen als Dialog (nie `window.confirm`), auf dem Handy sind Dialoge Vollbild.

---

## 1. Grundregeln

1. **CRUD immer Liste + separate Unterseite.** Keine Inline-Edits, keine Modals fuer Bearbeiten/Anlegen.
2. **Reihenfolge/Abstaende macht das Control.** In `ms-list`: Toolbar, Tabelle, Leerzustand, Pagination, Aktionen.
3. **Buttons immer positiv -> negativ:** Speichern, Zurueck, Abstand, Loeschen (`ms-button-row`).
4. **Geld immer als `long` in Cent.** Anzeige `<ms-money cents="...">`, Eingabe `<ms-field type="money">`.
5. **Jedes Control bekommt eine eindeutige `id`** (praefixt die inneren Ids; `name` wird nie praefixt).
6. **Deutsche Texte in der UI**, englische Namen im Code. `ErrorMessage` der DataAnnotations immer deutsch.
7. **Tabellen-Zellen brauchen `data-label`** - auf dem Handy werden die Zeilen zu Karten.
8. **Keine HTML-Entities in `ms-*`-Attributen** (`label`, `title`, `text`, `hint`, `confirm`): die Tag-Helper
   encodieren den Wert, aus `Zur&#252;ck` wuerde sichtbar `Zur&#252;ck`. Echte UTF-8-Umlaute verwenden.
9. **Mobil zuerst:** jede Seite bei 390 px ansehen; sekundaere Tabellenspalten mit `mat-hide-sm`.

---

## 2. Layout-Vertrag (Titel, Breadcrumb, Menue, Kopfleiste, Flash)

`Pages/Shared/_Layout.cshtml` liest nur ViewData/TempData. Helfer in `MatSplit.Web.Ui.PageLayoutExtensions`:

```csharp
public async Task<IActionResult> OnGetAsync()
{
    Groups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId());

    this.SetTitle("Ausgaben", "Sommerurlaub 2026", "expense");   // h1, Zeile darunter, Titel des Dokuments
    this.SetBreadcrumb(
        new BreadcrumbItem("Gruppen", "/Groups"),
        new BreadcrumbItem("Sommerurlaub 2026", $"/Groups/Details?groupId={GroupId}"),
        new BreadcrumbItem("Ausgaben"));                          // letzter Eintrag ohne Url
    this.SetMenuGroups(
        Groups.Select(g => new MenuGroupEntry(g.Id, g.Name)),      // Gruppenliste im Menue
        activeGroupId: GroupId);                                   // diese Gruppe ist markiert
    this.SetHeaderActions(                                         // Icons rechts in der Kopfleiste
        new HeaderAction("/Groups/History?groupId=1", "history", "Historie"),
        new HeaderAction("/Groups/Expenses/Edit?groupId=1", "plus", "Neue Ausgabe", Primary: true));
    return Page();
}
```

| Key / Helfer | Typ | Wirkung |
| --- | --- | --- |
| `SetTitle(title, subtitle, icon)` | string | `h1` der Seite, Zeile darunter, Dokumenttitel (das Icon wird nicht mehr gezeigt) |
| `SetBreadcrumb(params BreadcrumbItem[])` | `BreadcrumbItem(Text, Url?, Icon?)` | Brotkrumen in der Kopfleiste; auf dem Handy die letzten zwei - der Weg zurueck auch in der installierten App ohne Browser-Zurueck |
| `SetMenuGroups(groups, activeGroupId)` | `MenuGroupEntry(Id, Name, IsGroupAdmin)` | Gruppen im Menue (Ueberschrift "Gruppen" mit Plus fuer eine neue Gruppe) |
| `SetHeaderActions(params HeaderAction[])` | `HeaderAction(Url, Icon, Label, Primary)` | Icons rechts in der Kopfleiste, hoechstens vier; die Hauptaktion (`Primary`) steht zuletzt, in der Akzentfarbe. Mehrere Nebenaktionen sind ab 900 px Icons, darunter ein Menue hinter "Weitere Aktionen" |
| `this.Flash("Gespeichert.")` | string (TempData) | Toast (ueber einem offenen Dialog sichtbar), ueberlebt den Redirect |
| `this.FlashError("Fehlgeschlagen.")` | string (TempData) | roter Hinweis oben im Inhalt, mit X |
| `ViewData[LayoutKeys.HideMenu] = true` | bool | Seite ohne Menue (Anmeldung, Einladung, Fehler): Logo, `h1`, Karte in einer Spalte |
| `ViewData[LayoutKeys.PublicPage] = true` | bool | zusammen mit `HideMenu`: breite oeffentliche Ansicht (Nur-Lese-Link) |
| `ViewData[LayoutKeys.IsAdmin] = true` | bool | Admin-Bereich erzwingen (sonst aus `User.IsInRole("Admin")`) |
| `ViewData[LayoutKeys.CurrentUserName]` | string | Anzeigename im Menue (sonst `User.Identity.Name`) |
| `ViewData[LayoutKeys.AppName]` | string | App-Name (sonst aus `appconfig.json`) |
| `ViewData["ThemeSaveUrl"]` | string | Adresse, an die der Theme-Button `theme=system|light|dark` postet (Standard: das Konto) |

**Nach jedem POST mit Redirect: `this.Flash(...)` + `RedirectToPage(...)`.**

### URL-Vertrag (wird vom Menue und vom Breadcrumb erwartet)

| Seite | URL |
| --- | --- |
| Gruppenliste | `/Groups` |
| Gruppe anlegen/bearbeiten | `/Groups/Edit` bzw. `/Groups/Edit?id={groupId}` |
| Gruppen-Uebersicht | `/Groups/Details?groupId={id}` (Tabs ueber `&tab=`) |
| Mitglied hinzufuegen/bearbeiten | `/Groups/MemberEdit?groupId={id}` bzw. `...&userId={userId}` |
| Ausgabe bearbeiten | `/Groups/Expenses/Edit?groupId={id}&id={expenseId}` |
| Zahlung bearbeiten | `/Groups/Payments/Edit?groupId={id}&id={paymentId}` |
| Kontostand / Historie / Teilen | `/Groups/Balance`, `/Groups/History`, `/Groups/Share` (`?groupId={id}`) |
| Admin | `/Admin`, `/Admin/Users`, `/Admin/Users/Edit?id=`, `/Admin/Users/Merge`, `/Admin/Groups`, `/Admin/Settings` |
| Konto | `/Account/Login`, `/Account/Logout`, `/Account/Profile`, `/Join?token=`, `/View?token=` |

Gruppenbezogene Seiten binden **`groupId`** (SupportsGet), die bearbeitete Entitaet heisst **`id`**.

---

## 3. Listen-Beispiel

Die Werkzeugleiste (LIST-02/03): die Suche steht als Feld in der Leiste, Filter und Sortierung sind Buttons,
die einen Dialog oeffnen (auf dem Handy Vollbild). Die Felder schreibst du einfach in `ms-toolbar`; er verteilt sie:
das Feld vom Typ `search` wird das Suchfeld, das Feld mit dem Namen `sort` kommt in den Sortier-Dialog, alle anderen
in den Filter-Dialog. Alle gehoeren zu **einem** GET-Formular, jeder Dialog schickt also den ganzen Zustand.
Was gerade gilt, zeigt der Button (Zaehler, aktueller Wert) und ein Chip mit X (`LIST-04`).

```cshtml
@page
@model MatSplit.Web.Pages.Groups.Expenses.IndexModel

<ms-list id="expenses">

    <ms-toolbar id="expenses-tools"
                action="/Groups/Expenses"
                reset-url="@($"/Groups/Expenses?groupId={Model.GroupId}")"
                preserve="groupId">
        <ms-field id="expenses-search" name="search" type="search" label="Suche"
                  value="@Model.Search" placeholder="Beschreibung oder Kategorie"></ms-field>
        <ms-field id="expenses-payer" name="payerUserId" type="select" label="Bezahlt von"
                  items="Model.MemberOptions" option-label="Alle"></ms-field>
        <ms-field id="expenses-from" name="fromDate" type="date" label="Von"
                  value="@Model.FromDate?.ToString("yyyy-MM-dd")"></ms-field>
        <ms-field id="expenses-sort" name="sort" type="select" label="Sortierung"
                  items="Model.SortOptions"></ms-field>
    </ms-toolbar>

    <ms-table id="expenses-table" caption="Ausgaben der Gruppe">
        <thead>
            <tr>
                <th scope="col">Datum</th>
                <th scope="col">Beschreibung</th>
                <th scope="col" class="mat-num">Betrag</th>
                <th scope="col"><span class="mat-sr-only">Aktionen</span></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var expense in Model.Result.Items)
            {
                <tr>
                    <td data-label="Datum">@expense.ExpenseDate.ToString("dd.MM.yyyy")</td>
                    <td data-label="Beschreibung">@expense.Description</td>
                    <td data-label="Betrag" class="mat-num">
                        <ms-money cents="@expense.AmountCents" currency="@expense.Currency"></ms-money>
                    </td>
                    <td data-label="Aktionen">
                        <div class="mat-table__actions">
                            <ms-button id="@($"expense-{expense.Id}-edit")" kind="ghost" size="sm"
                                       icon="edit" icon-only="true" label="Bearbeiten"
                                       href="@($"/Groups/Expenses/Edit?groupId={Model.GroupId}&id={expense.Id}")"></ms-button>
                        </div>
                    </td>
                </tr>
            }
        </tbody>
    </ms-table>

    @if (Model.Result.TotalCount == 0)
    {
        <ms-empty-state id="expenses-empty" title="Noch keine Ausgaben"
                        text="Trage die erste Ausgabe ein, damit die Abrechnung starten kann."
                        icon="expense"
                        action-url="@($"/Groups/Expenses/Edit?groupId={Model.GroupId}")"
                        action-label="Ausgabe erfassen"></ms-empty-state>
    }

    <ms-pagination id="expenses-pager"
                   page="@Model.Result.Page"
                   page-size="@Model.Result.PageSize"
                   total-count="@Model.Result.TotalCount"
                   page-url="@($"/Groups/Expenses?groupId={Model.GroupId}&search={Model.Search}&page={{0}}")"></ms-pagination>

    <ms-actions id="expenses-actions">
        <ms-button id="expenses-new" kind="primary" icon="plus" label="Neue Ausgabe"
                   href="@($"/Groups/Expenses/Edit?groupId={Model.GroupId}")"></ms-button>
    </ms-actions>
</ms-list>
```

* **Keine eigene Ueberschrift in der Liste**: der `h1` der Seite steht schon im Layout (`SetTitle`).
* Dinge, die man einzeln ansieht, oeffnet oder abhakt (Transaktionen, Mitglieder, Salden), sind eine **Zeilenliste**
  (`ul.mat-itemlist` aus `li.mat-item`, `mat-item__main` = ein Ziel, davor `__lead`, dann `__body`, hinten `__end`),
  keine Tabelle; eine Tabelle ist fuer Daten, die man sucht und sortiert (`LIST-02`, `LIST-15`).
* `page-url` enthaelt den Platzhalter `{0}`; in einem interpolierten String `{{0}}`. `ms-pagination` rendert nichts bei einer Seite.
* **Die Seitennummer NIE per `[BindProperty(Name = "page")]` binden** - in Razor Pages ist `page` ein reservierter
  Route-Value. Richtig: `public int PageNumber => MsPaging.ReadPageNumber(Request);`.
* Loeschen passiert auf der Edit-Seite, nicht in der Liste (Ausnahme: Admin-Listen mit `kind="danger" confirm="..."`).

---

## 4. Formular-Beispiel

```cshtml
<ms-card id="expense-card" title="@(Model.Id.HasValue ? "Ausgabe bearbeiten" : "Neue Ausgabe")" icon="expense">
    <ms-form id="expense-form" columns="2" has-files="true">
        <ms-field id="ef-desc" for="Input.Description" full-width="true"
                  placeholder="z. B. Pizza am Strand"></ms-field>
        <ms-field id="ef-amount" for="Input.Amount" type="money" currency="EUR"></ms-field>
        <ms-field id="ef-date" for="Input.ExpenseDate" type="date"></ms-field>
        <ms-field id="ef-payer" for="Input.PaidByUserId" type="select"
                  items="Model.MemberOptions" option-label="Bitte waehlen"></ms-field>

        <ms-field id="ef-custom" for="Input.UseCustomShares" type="checkbox"></ms-field>
        <ms-field id="ef-share-mode" for="Input.ShareMode" type="select" items="Model.ShareModes"
                  depends-on="Input.UseCustomShares" depends-value="true"></ms-field>

        <ms-field id="ef-receipt" name="Receipt" type="file" label="Belegfoto"
                  accept="image/*" capture="environment" camera="true" full-width="true"></ms-field>

        <ms-button-row id="ef-buttons">
            <ms-button id="ef-save" kind="primary" icon="save" label="Speichern"></ms-button>
            <ms-button id="ef-back" kind="secondary" icon="back" label="Zurueck"
                       type="button" href="@($"/Groups/Expenses?groupId={Model.GroupId}")"></ms-button>
            @if (Model.Id.HasValue)
            {
                <ms-button id="ef-delete" kind="danger" icon="trash" label="Loeschen"
                           handler="Delete" confirm="Ausgabe wirklich loeschen?"></ms-button>
            }
        </ms-button-row>
    </ms-form>
</ms-card>
```

* `for="Input.X"` liefert Label (`[Display]`), Wert, Format und die **Pruefung im Browser**: `[Required]`,
  `[StringLength]`, `[Range]`, `[RegularExpression]`, `[EmailAddress]` werden zu `required`, `maxlength`/`minlength`,
  `min`/`max`, `pattern`, `type` und den deutschen Meldungen (`data-msg-*`), die MatUi beim Absenden an das Feld schreibt.
  Ohne `for` immer `name` + `label` setzen.
* Eine Zusammenfassung ueber dem Formular listet, was nicht stimmt (Serverfehler aus dem ModelState, im Browser von MatUi).
* `optional="true"`: das Feld ist nicht Pflicht, obwohl die Eigenschaft ein nicht-nullbarer Werttyp ist (der Server fragt
  nur in manchen Faellen danach).
* `handler="Delete"` erzeugt `formaction=...?handler=Delete`; ein solcher Button (und jeder `danger`) sendet das
  Formular **ohne** Pruefung der Felder (`formnovalidate`). `confirm="..."` fragt vorher in einem Dialog; bei `danger`
  traegt der rote Button den Namen der Aktion.
* `depends-on`/`depends-value` -> `data-show-if`/`data-show-value`: das Feld erscheint nur, wenn das Master-Feld den Wert hat
  (`*` = beliebig, `true` bei Checkboxen, Komma-Liste). **Ein verstecktes Feld ist `disabled`** - es wird nicht gesendet
  und nicht geprueft. Auch beliebiges Markup kann das: `<div data-show-if="Input.ShareMode" data-show-value="Factors,Amounts" hidden>`.
* Der Antiforgery-Token wird von `ms-form` gerendert.

---

## 5. Control-Referenz

Alle Controls akzeptieren `class="..."` (an die Wurzel) und `id="..."`.

### ms-list
Wrapper (`mat-list`), sammelt die Slots Toolbar, Tabelle, Leerzustand, Pagination, Aktionen. `title`, `subtitle`, `icon`,
`count` rendern eine Ueberschrift - nur fuer eine Liste mit eigenem Abschnitt; auf einer Listenseite nicht noetig.
Freier Inhalt (eine Kartenliste, ein Hinweis) landet zwischen Toolbar und Tabelle.

### ms-toolbar
Siehe Abschnitt 3. `action`, `reset-url` ("Zuruecksetzen" in den Dialogen), `preserve` (Query-Keys als Hidden-Felder,
z. B. `groupId`), `reset-page`/`page-param` (Seite 1 beim Filtern), `submit-label` ("Anwenden"). `auto-submit*` und
`hide-submit` werden nur noch angenommen.

### ms-table
`caption` (Screenreader), `show-caption`, `dense` (kompakt), `zebra` (nur fuer lange Referenztabellen), `sticky-head`.
**Pflicht:** `data-label` an jeder `td`. Zahlenspalten `class="mat-num"`. Zeilenaktionen in
`<td class="mat-table__cell-actions"><div class="mat-table__actions">`... Unter 720 px werden die Zeilen zu Karten.

### ms-actions / ms-button-row
Aktionen unter der Liste (`mat-actions`, linksbuendig) bzw. unter dem Formular (`mat-btn-row`: primary, secondary, Abstand,
danger; die Reihenfolge im Markup ist egal). `ms-button-row`: `divider` (Linie darueber), `sticky` (klebt auf dem Handy unten).

### ms-pagination
`page`, `page-size`, `total-count`, `page-url`, `page-param`, `window`, `show-info` ("51 bis 75 von 190").

### ms-empty-state
`title`, `text`, `icon`, `action-url`, `action-label`, `action-icon`.

### ms-form
`action`, `method`, `handler`, `has-files`, `enctype`, `antiforgery`, `validation-summary` (`all`, `modelonly`, `none`),
`columns` (2 = Feldraster, einspaltig unter 700 px), `no-validate`, `autocomplete`. `ms-button-row`, `full-width`-Felder,
Hinweise, `h2/h3` und `hr` mit `mat-field--full` ueber die ganze Breite.

### ms-field
`for`, `name`, `type` (`text`, `textarea`, `select`, `checkbox`, `file`, `money`, `number`, `date`, `datetime`, `time`,
`email`, `password`, `tel`, `url`, `search`, `color`, `month`, `hidden`, `static`), `label`, `label-hidden`, `value`, `hint`,
`placeholder`, `required`, `optional`, `readonly`, `disabled`, `min`/`max`/`step`, `max-length`, `input-mode`, `pattern`,
`rows`, `items`, `option-label`, `multiple`, `accept`, `capture`, `camera` (Button "Foto aufnehmen"), `checked`, `currency`
(Suffix bei `money`), `prefix`/`suffix`, `full-width`, `depends-on`/`depends-value`, `no-validation`, `autofocus`.
`type="static"` zeigt den Wert (oder den Kindinhalt) als nicht editierbaren Block. Geld ist ein Textfeld mit
`inputmode="decimal"`: Komma und Punkt gehen, `site.js` haelt den Wert mit Punkt (Modell-Binding).

### ms-button
`kind` (`primary`, `secondary`, `ghost`, `danger`), `label`, `icon`, `icon-only` (das Label wird `aria-label` und Tooltip),
`href` (dann `<a>`), `type`, `name`/`value`, `handler`, `form-action`, `form`, `confirm`, `size` (`sm`), `full-width`,
`target`, `title`, `disabled`.

### ms-card
`title`, `subtitle`, `icon`, `tone` (`accent` = Linie in der Akzentfarbe, hoechstens eine pro Seite), `href` (ganze Karte
ist ein Link), `flush` (kein Innenabstand, z. B. mit einer Zeilenliste oder Tabelle), `level`, `header-action-url`/`-icon`/`-label`.

### ms-alert
`tone` (`info`, `success`, `warning`, `error`), `title`, `text`, `icon`, `dismissible`. Kindinhalt erlaubt. Jeder Hinweis
hat ein Icon und ein Wort - die Farbe ist nie das einzige Signal.

### ms-money
`cents` (Pflicht), `currency`, `show-sign` (Plus und gruen bei positiven Salden), `strong`. Negative Betraege sind rot.

### ms-avatar / ms-icon
`ms-avatar`: Initialen in der Akzenttoenung (`name`, `size` = `sm|md|lg`, `you`). Es wird keine Farbe pro Person
erfunden. `ms-icon`: `name` (Pflicht), `label` (macht das Icon fuer Screenreader sichtbar); die Groesse kommt von der
Schrift um das Icon.

---

## 6. Icon-Namen

Aus dem Toolkit-Sprite (Auswahl): `home`, `users`, `user`, `plus`, `edit`, `trash`, `save`, `back`, `search`, `filter`,
`sliders`, `sort`, `menu`, `sun`, `moon`, `monitor`, `logout`, `link`, `settings`, `info`, `check`, `x`, `copy`, `calendar`,
`download`, `upload`, `activity`, `clock`, `shield`, `alert`, `image`, `refresh`, ... Die alten MatSplit-Namen werden
abgebildet (`group` -> `users`, `history` -> `clock`, `admin` -> `shield`, `warning` -> `alert`, `close` -> `x`,
`forward` -> `arrow-right`, `receipt` -> `file-text`, `empty` -> `box`). Nur in `_AppIcons.cshtml`: `camera`, `balance`,
`merge`, `paypal`, `share`, `whatsapp`, `telegram`, `euro`, `ausgleich`, `expense`, `logo`. Ein Icon mit einer Bedeutung in der
Oberflaeche (speichern, loeschen ...) gehoert ins Toolkit (`ICON-02`), nicht nur in die App.

---

## 7. Klassen des Toolkits, die Seiten nutzen duerfen

| Klasse | Zweck |
| --- | --- |
| `mat-stack` | senkrechte Liste mit Standardabstand (`mat-stack--sm`, `--lg`) |
| `mat-row` | waagerechte, umbrechende Zeile |
| `mat-grid` `mat-grid--2` / `--3` | Kachelraster |
| `mat-kv` | Fakten `<dl><dt>/<dd>` |
| `mat-num` | rechtsbuendige Zahl |
| `mat-muted`, `mat-sr-only`, `mat-mono` | sekundaerer Text, nur fuer Screenreader, Festbreite |
| `mat-pill` (`--ok`, `--warn`, `--error`, `--info`, `--accent`, `--plain`) | Zustand mit Text |
| `mat-tag`, `mat-badge`, `mat-chip` | Kategorie, Zahl, aktives Werkzeug |
| `mat-tabs` (`mat-tabs--pills`) + `mat-tab` | Reiter und Pill-Filter als Links (`aria-current` am aktuellen) |
| `mat-itemlist`, `mat-item` | Zeilenliste (`mat-item__lead/__body/__title/__text/__end/__aside`) |
| `mat-stat` | Kennzahl in einer Karte |
| `mat-meter` + `data-value` | Balken (die Breite setzt das Skript) |
| `d-*` Hilfen `mat-d-none mat-d-md-inline-flex` | Anzeige nach Breite (`sm` 600, `md` 900, `lg` 1200 px) |

Eigene Klassen der App tragen das Praefix `ms-` (`ms-grouplist`, `ms-balance-hero`, `ms-share`, `ef-*` fuer die
Aufteilung der Ausgabe) und stehen mit ihren Stilen in `app.css`.

---

## 8. JavaScript-Hooks

MatUi (`matui.js`) liefert: Theme-Button (`data-theme-toggle`, `data-theme-save-url`), Drawer (`data-nav-toggle`),
Dialoge (`data-dialog-open`, `data-dialog-close`, `data-confirm`), Menues (`data-menu-toggle`), Pruefung und
Zusammenfassung, abhaengige Felder (`data-show-if`), Kopieren (`data-copy="text|#id"`), Meldungen schliessen
(`data-dismiss`), Toasts (`MatUi.toast`), Tastenkuerzel (`/` = Suche). `site.js` ergaenzt nur:

| Attribut | Wirkung |
| --- | --- |
| `data-ms-share` + `data-ms-share-url` / `-title` / `-text` | Web-Share-Dialog des Geraets, sonst WhatsApp |
| `data-ms-camera="fileInputId"` | Kamera-Aufnahme mit `getUserMedia`, Fallback Datei-Dialog |
| `data-ms-money` | Komma im Geldfeld wird live zum Punkt |
| `data-ms-receipt` (`receipt-capture.js`) | Beleg: Groessenpruefung, Verkleinern, Vorschau, Kamera |

Am `<html>`: `data-standalone="true"` (laeuft vom Home-Bildschirm), `data-platform="ios|android"`, `--ms-keyboard-inset`
(Hoehe der Bildschirmtastatur), `html.ms-keyboard-open`.

---

## 9. Anti-Patterns

* Kein `<table>` ohne `ms-table`, kein `<form>` ohne `ms-form` (Pruefung, Token, Raster).
* Keine Inline-Styles, keine Farbwerte, keine Verlaeufe/Schatten - nur Tokens (`var(--mat-...)`) in `app.css`.
* Kein `window.confirm`/`alert`/`prompt` - `confirm="..."` am Button, im Skript `MatUi.confirm`.
* Keine externen Fonts, Icons oder Skripte (Offline-/PWA-Faehigkeit); das Toolkit wird kopiert, nicht verlinkt.
* Keine `double`/`decimal`-Felder fuer gespeicherte Betraege - `long` Cent.
* Kein `asp-page`/`asp-route` in den Shared-Partials: Menue und Breadcrumb nutzen feste Pfade.
* Keine eigene Ueberschrift, die den `h1` der Seite wiederholt.

---

## 10. Validierung im Detail

* `ms-form` setzt immer `novalidate`: die Blasen des Browsers (englisch, nicht stylebar) sind aus. MatUi prueft beim
  Absenden die nativen Regeln der Felder und schreibt die Meldung in die reservierte Zeile unter dem Feld (`mat-error`),
  setzt `is-invalid` und fuellt die Zusammenfassung mit Links auf die Felder.
* Die Meldungen kommen aus `ErrorMessage` der DataAnnotation - **immer deutsch setzen**:

  ```csharp
  [Required(ErrorMessage = "Bitte eine Beschreibung angeben.")]
  [StringLength(120, ErrorMessage = "Maximal 120 Zeichen.")]
  [Display(Name = "Beschreibung")]
  public string Description { get; set; } = string.Empty;
  ```
* Texte, die das Toolkit selbst schreibt (Dialog-Knoepfe, "Kopiert", Standardmeldungen), stehen als `data-*` am `<html>`
  (`MsUi.ToolkitTexts`, Deutsch).
* Checkboxen sind nie Pflicht (ein leeres Kaestchen postet `false`).
* Serverseitig gilt weiter `ModelState.IsValid`; die Fehler landen im Feld (`is-invalid` + Text) und in der Zusammenfassung.

---

## 11. Was das Layout mitbringt (nicht selbst bauen)

* Menue links: Logo, Gruppen (mit Plus), Administration (nur Admins), unten Benutzer (Konto, Abmelden), Theme-Button
  (System, Hell, Dunkel; wird im Konto gespeichert), Version. Auf dem Handy ein Drawer, dessen Logo und X sichtbar bleiben.
* Kopfleiste mit Burger, Brotkrumen und den Aktionen der Seite; Seitenkopf mit `h1` und einer Zeile; Fussleiste.
* Icon-Sprites, PWA-Metatags, Manifest, Service-Worker-Registrierung, Offline-Balken, Installationshinweis.
* Flash-Meldungen aus TempData.
* Sichere Bereiche (Notch, Home-Indikator, Aussparung im Querformat) und die Anpassung an die Bildschirmtastatur -
  siehe [pwa.md](pwa.md).

Seiten fuellen also nur `ViewData`/`TempData` (Abschnitt 2) und rendern ihren Inhalt mit den Controls - kein eigenes
`<html>`, `<head>`, Menue oder Header.
