# MatSplit UI-Controls (ms-*)

Verbindliche Referenz fuer alle Seiten. Jede Feature-Seite wird **nur** aus diesen
Controls gebaut - kein eigenes Bootstrap-artiges Markup, keine eigenen CSS-Klassen
ausserhalb der hier dokumentierten Utilities.

Die TagHelper-Bibliothek liegt in `src/MatSplit.Web/Ui/` (Namespace `MatSplit.Web.Ui`)
und ist ueber `Pages/_ViewImports.cshtml` bereits registriert
(`@addTagHelper *, MatSplit.Web`). Es ist **nichts** pro Seite zu importieren.

---

## 1. Grundregeln

1. **CRUD immer Liste + separate Unterseite.** Keine Inline-Edits, keine Modals fuer
   Bearbeiten/Anlegen. Liste = `ms-list`, Bearbeiten = `ms-form` auf eigener Seite.
2. **Reihenfolge/Abstaende macht das Control.** In `ms-list` ist die Reihenfolge fest:
   Toolbar oben, dann Tabelle, direkt darunter die Pagination, darunter die Aktionen.
   Die Markup-Reihenfolge der Slots ist egal.
3. **Buttons immer positiv -> negativ:** Speichern, Zurueck, `<Abstand>`, Loeschen.
   `ms-button-row` erzwingt das, unabhaengig von der Markup-Reihenfolge.
4. **Geld immer als `long` in Cent.** Anzeige mit `<ms-money cents="...">`,
   Eingabe mit `<ms-field type="money">` (bindet auf `decimal`, Umrechnung in Cent
   macht der Service/das EditModel).
5. **Jedes Control bekommt eine eindeutige `id`.** Sie praefixt alle inneren DOM-IDs
   (`id="ef-desc"` -> Wrapper `ef-desc-field`, Fehler `ef-desc-error`). Fehlt die id,
   generiert das Control eine (`ms-field-7`) - besser immer selbst setzen, damit
   Labels/Tests stabil sind. **Die `name`-Attribute werden nicht praefixt**, sonst
   waere Model-Binding kaputt.
6. **Deutsche Texte in der UI**, englische Namen im Code. Bei DataAnnotations immer
   `ErrorMessage` in Deutsch setzen, sonst kommt die englische Standardmeldung.
7. **Tabellen-Zellen brauchen `data-label`** (siehe `ms-table`), sonst fehlen auf dem
   Handy die Spaltenbezeichnungen.

---

## 2. Layout-Vertrag (Titel, Breadcrumb, Menue, Flash)

Das Layout (`Pages/Shared/_Layout.cshtml`) liest ausschliesslich ViewData/TempData.
Dafuer gibt es Helfer in `MatSplit.Web.Ui.PageLayoutExtensions`:

```csharp
public async Task<IActionResult> OnGetAsync()
{
    Groups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId());

    this.SetTitle("Ausgaben", "Sommerurlaub 2026", "expense");   // Titel, Untertitel, Icon
    this.SetBreadcrumb(
        new BreadcrumbItem("Gruppen", "/Groups"),
        new BreadcrumbItem("Sommerurlaub 2026", $"/Groups/Details?groupId={GroupId}"),
        new BreadcrumbItem("Ausgaben"));                          // letzter Eintrag ohne Url
    this.SetMenuGroups(
        Groups.Select(g => new MenuGroupEntry(g.Id, g.Name)),      // Gruppenliste im Menue
        activeGroupId: GroupId);                                   // dessen Untermenue ist offen
    return Page();
}
```

Weitere Schluessel (`MatSplit.Web.Ui.LayoutKeys`):

| Key / Helfer | Typ | Wirkung |
| --- | --- | --- |
| `SetTitle(title, subtitle, icon)` | string | Header, Browser-Titel, Icon links vom Titel |
| `SetBreadcrumb(params BreadcrumbItem[])` | `BreadcrumbItem(Text, Url?, Icon?)` | Breadcrumb unter dem Header; "Start" wird automatisch vorangestellt |
| `SetMenuGroups(groups, activeGroupId)` | `MenuGroupEntry(Id, Name, IsGroupAdmin)` | Gruppen + Untereintraege im linken Menue |
| `this.Flash("Gespeichert.")` | string (TempData) | gruene Meldung, ueberlebt Redirect |
| `this.FlashError("Fehlgeschlagen.")` | string (TempData) | rote Meldung, ueberlebt Redirect |
| `ViewData[LayoutKeys.HideMenu] = true` | bool | Menue ausblenden (Login, Join) |
| `ViewData[LayoutKeys.IsAdmin] = true` | bool | Admin-Bereich erzwingen (sonst aus `User.IsInRole("Admin")`) |
| `ViewData[LayoutKeys.CurrentUserName]` | string | Anzeigename unten links (sonst `User.Identity.Name`) |
| `ViewData[LayoutKeys.AppName]` | string | App-Name (sonst aus `appconfig.json`) |
| `ViewData["ThemeSaveUrl"]` | string | Wenn gesetzt, POSTet der Theme-Umschalter `theme=system|dark|light` dorthin |

**Nach jedem POST mit Redirect: `this.Flash(...)` + `RedirectToPage(...)`** - die Meldung
wird oben im Content automatisch als `ms-alert` gerendert.

### URL-Vertrag (wird vom linken Menue und vom Breadcrumb erwartet)

| Seite | URL |
| --- | --- |
| Gruppenliste | `/Groups` |
| Gruppe anlegen/bearbeiten | `/Groups/Edit` bzw. `/Groups/Edit?id={groupId}` |
| Gruppen-Uebersicht | `/Groups/Details?groupId={id}` |
| Mitglieder (Liste) | `/Groups/Members?groupId={id}` |
| Mitglied hinzufuegen/bearbeiten | `/Groups/MemberEdit?groupId={id}` bzw. `...&userId={userId}` |
| Ausgaben (Liste) | `/Groups/Expenses?groupId={id}` |
| Ausgabe bearbeiten | `/Groups/Expenses/Edit?groupId={id}&id={expenseId}` |
| Zahlungen (Liste) | `/Groups/Payments?groupId={id}` |
| Zahlung bearbeiten | `/Groups/Payments/Edit?groupId={id}&id={paymentId}` |
| Kontostand | `/Groups/Balance?groupId={id}` |
| Historie | `/Groups/History?groupId={id}` |
| Admin | `/Admin`, `/Admin/Users`, `/Admin/Users/Edit?id=`, `/Admin/Users/Merge`, `/Admin/Groups`, `/Admin/Settings` |
| Konto | `/Account/Login`, `/Account/Logout`, `/Account/Profile`, `/Join?token=` |

Gruppenbezogene Seiten binden also **`groupId`** (SupportsGet), die bearbeitete Entitaet
heisst **`id`**.

---

## 3. Vollstaendiges Listen-Beispiel (Copy & Paste)

Toolbar + Tabelle + Pagination + Aktionen. Die Slots duerfen in beliebiger
Reihenfolge im Markup stehen, `ms-list` sortiert sie.

```cshtml
@page
@model MatSplit.Web.Pages.Groups.Expenses.IndexModel

<ms-list id="expenses" title="Ausgaben" subtitle="Alle Ausgaben der Gruppe"
         icon="expense" count="@Model.Result.TotalCount">

    <ms-toolbar id="expenses-filter"
                action="/Groups/Expenses"
                reset-url="@($"/Groups/Expenses?groupId={Model.GroupId}")"
                preserve="groupId"
                auto-submit-text="true">
        <ms-field id="expenses-search" name="search" type="search" label="Suche"
                  value="@Model.Search" placeholder="Beschreibung oder Kategorie"></ms-field>
        <ms-field id="expenses-payer" name="payerUserId" type="select" label="Bezahlt von"
                  items="Model.MemberOptions" option-label="Alle"></ms-field>
        <ms-field id="expenses-from" name="fromDate" type="date" label="Von"
                  value="@Model.FromDate?.ToString("yyyy-MM-dd")"></ms-field>
        <ms-field id="expenses-to" name="toDate" type="date" label="Bis"
                  value="@Model.ToDate?.ToString("yyyy-MM-dd")"></ms-field>
        <ms-field id="expenses-sort" name="sort" type="select" label="Sortierung"
                  items="Model.SortOptions"></ms-field>
    </ms-toolbar>

    <ms-table id="expenses-table" caption="Ausgaben der Gruppe">
        <thead>
            <tr>
                <th scope="col">Datum</th>
                <th scope="col">Beschreibung</th>
                <th scope="col">Bezahlt von</th>
                <th scope="col" class="ms-num">Betrag</th>
                <th scope="col"><span class="ms-visually-hidden">Aktionen</span></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var expense in Model.Result.Items)
            {
                <tr>
                    <td data-label="Datum">@expense.ExpenseDate.ToString("dd.MM.yyyy")</td>
                    <td data-label="Beschreibung">@expense.Description</td>
                    <td data-label="Bezahlt von">@expense.PaidByName</td>
                    <td data-label="Betrag" class="ms-num">
                        <ms-money cents="@expense.AmountCents" currency="@expense.Currency"></ms-money>
                    </td>
                    <td data-label="Aktionen">
                        <div class="ms-table__rowactions">
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
        <ms-button id="expenses-balance" kind="secondary" icon="balance" label="Kontostand"
                   href="@($"/Groups/Balance?groupId={Model.GroupId}")"></ms-button>
    </ms-actions>
</ms-list>
```

Hinweise:

* `page-url` enthaelt den Platzhalter `{0}` fuer die Seitennummer. In einem
  interpolierten String muss er `{{0}}` geschrieben werden. Ohne Platzhalter haengt
  das Control `?page=N` bzw. `&page=N` an.
* `ms-pagination` rendert **nichts**, wenn es nur eine Seite gibt.
* **Die Seitennummer NIEMALS per `[BindProperty(Name = "page")]` binden.** In
  Razor Pages ist `page` ein reservierter Route-Value (er traegt den
  Seitenpfad). Der Binder gewinnt mit dem Route-Wert, setzt einen
  ModelState-Fehler ("The value '/Groups/Members' is not valid for
  PageNumber") und blockiert damit jedes Formular der Seite; `?page=N` wirkt
  nicht mehr. Richtig ist:

  ```csharp
  public int PageNumber => MsPaging.ReadPageNumber(Request);
  ```

  Ebenso darf `page` nicht als Route-Value an `RedirectToPage(...)`
  uebergeben werden (wirft `InvalidOperationException`); fuer PRG-Redirects
  einen eigenen Query-String bauen und `LocalRedirect`/`Redirect` nutzen.
* Loeschen passiert **nicht** aus der Liste, sondern auf der Edit-Seite.
  Wo eine Zeile trotzdem einen Delete-Button traegt (Admin-Listen), steht er
  als letzte Aktion mit `kind="danger" confirm="..."` in einem POST-Formular.
* Auch reine Zuordnungslisten (z. B. Mitglieder) bearbeiten **nicht** inline:
  `/Groups/Members` ist read-only, Anlegen/Bearbeiten/Entfernen laeuft ueber
  `/Groups/MemberEdit?groupId=..&userId=..`.
* **Keine HTML-Entities in `ms-*`-Attributen** (`title`, `label`, `text`,
  `hint`, `subtitle`, `confirm`, ...). Die TagHelper encodieren den Wert
  erneut, aus `label="Zur&#252;ck"` wird sichtbar `Zur&#252;ck`. Echte
  UTF-8-Umlaute verwenden; Entities nur in reinem HTML-Text.

---

## 4. Vollstaendiges Formular-Beispiel (Copy & Paste)

```cshtml
@page
@model MatSplit.Web.Pages.Groups.Expenses.EditModel

<ms-card id="expense-card" title="@(Model.Id.HasValue ? "Ausgabe bearbeiten" : "Neue Ausgabe")" icon="expense">
    <ms-form id="expense-form" columns="2" has-files="true">
        <ms-field id="ef-desc" for="Input.Description" full-width="true"
                  placeholder="z. B. Pizza am Strand"></ms-field>
        <ms-field id="ef-amount" for="Input.Amount" type="money" currency="EUR"></ms-field>
        <ms-field id="ef-date" for="Input.ExpenseDate" type="date"></ms-field>
        <ms-field id="ef-payer" for="Input.PaidByUserId" type="select"
                  items="Model.MemberOptions" option-label="Bitte waehlen"></ms-field>
        <ms-field id="ef-category" for="Input.Category" hint="Optional, z. B. Essen"></ms-field>

        <ms-field id="ef-custom" for="Input.UseCustomShares" type="checkbox"></ms-field>
        <ms-field id="ef-share-mode" for="Input.ShareMode" type="select" items="Model.ShareModes"
                  depends-on="Input.UseCustomShares" depends-value="true"></ms-field>

        <ms-field id="ef-note" for="Input.Note" type="textarea" rows="3" full-width="true"></ms-field>
        <ms-field id="ef-receipt" name="Receipt" type="file" label="Belegfoto"
                  accept="image/*" capture="environment" camera="true" full-width="true"
                  hint="Foto direkt mit der Handy-Kamera aufnehmen."></ms-field>

        <ms-field id="ef-id" for="Input.Id" type="hidden"></ms-field>

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

* `for="Input.X"` liefert Label (aus `[Display]`), Wert, Format und die
  Client-Validierung (`data-val-*`). Ohne `for` immer `name` + `label` setzen.
* `handler="Delete"` erzeugt `formaction=...?handler=Delete` -> `OnPostDeleteAsync()`.
* Der Antiforgery-Token wird von `ms-form` automatisch gerendert.
* `depends-on`/`depends-value`: Feld ist initial versteckt und wird von `site.js`
  eingeblendet, sobald das Master-Feld den Wert hat (`*` = beliebiger Wert,
  `true` fuer Checkboxen, Komma-Liste fuer mehrere Werte). Versteckte Pflichtfelder
  verlieren automatisch ihr `required`.

---

## 5. Control-Referenz

Alle Controls akzeptieren zusaetzlich `class="..."` (wird an die Wurzel angehaengt)
und `id="..."`.

### ms-list

Wrapper einer Liste. Sammelt die Slots und erzwingt die Reihenfolge
**Toolbar -> Tabelle -> Empty-State -> Pagination -> Aktionen**.

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `title` | string | - | Ueberschrift (nur wenn gesetzt, wird ein Kopf gerendert) |
| `subtitle` | string | - | Text unter der Ueberschrift |
| `icon` | string | - | Icon-Name aus dem Sprite |
| `count` | int? | - | Zahl als Badge neben der Ueberschrift |

Freier Inhalt (z. B. ein `ms-alert`) direkt in `ms-list` landet zwischen Toolbar und
Tabelle.

### ms-toolbar

Filterleiste **ueber** der Liste. Rendert ein `<form method="get">`.

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `action` | string | aktuelle Url | Ziel des GET |
| `method` | string | `get` | Http-Methode |
| `submit-label` | string | `Filtern` | Text des Submit-Buttons |
| `hide-submit` | bool | `false` | Submit-Button ausblenden (nur mit auto-submit sinnvoll) |
| `reset-url` | string | - | Wenn gesetzt: Link "Zuruecksetzen" |
| `reset-label` | string | `Zurücksetzen` | Text des Reset-Links |
| `auto-submit` | bool | `true` | Selects/Dates/Checkboxen senden bei Aenderung |
| `auto-submit-text` | bool | `false` | Textfelder senden 500 ms nach dem Tippen (Fokus + Cursor bleiben) |
| `reset-page` | bool | `true` | Verstecktes `page=1`, damit Filter auf Seite 1 springen |
| `page-param` | string | `page` | Name des Seiten-Parameters |
| `preserve` | string | - | Komma-Liste von Query-Keys, die als Hidden-Felder mitgehen (z. B. `groupId`) |

### ms-table

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `caption` | string | - | Tabellenbeschriftung (Screenreader) |
| `show-caption` | bool | `false` | Caption sichtbar rendern |
| `dense` | bool | `false` | Kompakte Zeilenhoehe |
| `zebra` | bool | `true` | Abwechselnde Zeilenfarbe |
| `responsive` | bool | `true` | Unter 720 px werden Zeilen zu Karten |
| `sticky-head` | bool | `true` | Kopfzeile bleibt beim Scrollen stehen |

**Pflicht:** jede `<td>` braucht `data-label="Spaltenname"`; im Karten-Layout wird
daraus die Beschriftung. Zahlen-/Geldspalten bekommen `class="ms-num"`.
Zeilenaktionen in `<div class="ms-table__rowactions">` legen.

### ms-actions

Aktionsleiste **unter** der Liste, linksbuendig. Kinder sind `ms-button`.
Buttons mit `kind="danger"` werden automatisch abgesetzt und nach rechts geschoben.

### ms-pagination

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `page` | int | `1` | Aktuelle Seite (1-basiert) |
| `page-size` | int | `20` | Datensaetze pro Seite |
| `total-count` | int | `0` | Gesamtzahl |
| `page-url` | string | aktueller Pfad | Template mit `{0}` oder Basis-Url |
| `page-param` | string | `page` | Query-Parameter ohne Platzhalter |
| `window` | int | `2` | Anzahl Seitenlinks links/rechts der aktuellen Seite |
| `show-info` | bool | `true` | "Seite 2 von 8 · 142 Eintraege" |

Rendert nichts bei <= 1 Seite.

### ms-empty-state

| Attribut | Typ | Default |
| --- | --- | --- |
| `title` | string | `Noch keine Einträge` |
| `text` | string | - |
| `icon` | string | `empty` |
| `action-url` | string | - |
| `action-label` | string | `Neu anlegen` |
| `action-icon` | string | `plus` |

### ms-form

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `action` | string | aktuelle Url | Ziel |
| `method` | string | `post` | Http-Methode |
| `handler` | string | - | Razor-Page-Handler (`?handler=...`) |
| `has-files` | bool | `false` | setzt `enctype=multipart/form-data` |
| `enctype` | string | - | expliziter enctype |
| `antiforgery` | bool | `true` | Token rendern (nur bei POST) |
| `validation-summary` | string | `all` | `all`, `modelonly` oder `none` |
| `columns` | int | `1` | `2` = zweispaltiges Feldgitter (responsiv) |
| `no-validate` | bool | `false` | Client-Validierung komplett aus |
| `autocomplete` | string | - | z. B. `off` |

Kinder werden in Markup-Reihenfolge gerendert. `ms-button-row`, `ms-field full-width`,
`ms-alert`, `.ms-form__row`, `h2`, `h3`, `p`, `hr` spannen immer alle Spalten.

### ms-field

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `for` | ModelExpression | - | Model-Binding: `for="Input.Description"` |
| `name` | string | id | Feldname ohne `for` |
| `type` | string | `text` | `text`, `textarea`, `select`, `checkbox`, `file`, `money`, `number`, `date`, `datetime`, `time`, `email`, `password`, `tel`, `url`, `search`, `color`, `month`, `hidden`, `static` |
| `label` | string | Display-Name | Beschriftung |
| `label-hidden` | bool | `false` | Label nur fuer Screenreader |
| `value` | string | Modelwert | Expliziter Wert |
| `hint` | string | - | Hilfetext unter dem Feld |
| `placeholder` | string | - | Platzhalter |
| `required` | bool | `false` | `required` + Sternchen |
| `readonly` / `disabled` | bool | `false` | - |
| `min` / `max` / `step` | string | - | Zahlen-/Datumsgrenzen |
| `max-length` | int? | - | `maxlength` |
| `input-mode` | string | - | z. B. `decimal` |
| `pattern` | string | - | Regex fuer HTML5 |
| `rows` | int | `4` | Nur `textarea` |
| `items` | `IEnumerable<SelectListItem>` | - | Optionen fuer `select` |
| `option-label` | string | - | Leere Option ("Bitte waehlen") |
| `multiple` | bool | `false` | Mehrfachauswahl / Mehrfach-Upload |
| `accept` | string | - | Dateifilter, z. B. `image/*` |
| `capture` | string | - | `environment` (Rueckkamera) oder `user` |
| `camera` | bool | `false` | Button "Foto aufnehmen" (getUserMedia mit Fallback) |
| `checked` | bool | `false` | Checkbox ohne `for` |
| `currency` | string | `EUR` | Waehrungssymbol als Suffix bei `money` |
| `prefix` / `suffix` | string | - | Text vor/hinter dem Feld |
| `full-width` | bool | `false` | Feld spannt alle Spalten |
| `depends-on` | string | - | Feldname des Master-Feldes |
| `depends-value` | string | `*` | Erwarteter Wert (`*`, `true`, `a,b,c`) |
| `no-validation` | bool | `false` | Kein Fehlertext-Element |
| `autofocus` | bool | `false` | - |

`type="static"` rendert den Wert (oder den Kindinhalt) als nicht editierbaren Block -
praktisch fuer Detailangaben in einem Formular.

### ms-button-row

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `divider` | bool | `true` | Trennlinie oberhalb |
| `sticky` | bool | `false` | Bleibt am unteren Rand kleben (lange Formulare) |

Reihenfolge wird erzwungen: `primary` -> `secondary`/`ghost` -> freier Inhalt ->
Abstand -> `danger`.

### ms-button

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `kind` | string | `secondary` | `primary`, `secondary`, `ghost`, `danger` |
| `label` | string | Kindinhalt | Text |
| `icon` | string | - | Icon-Name |
| `icon-only` | bool | `false` | Nur Icon, `label` wird `aria-label`/`title` |
| `href` | string | - | Wenn gesetzt: `<a>` statt `<button>` |
| `type` | string | `submit` | `submit`, `button`, `reset` |
| `name` / `value` | string | - | Submit-Wert |
| `handler` | string | - | Razor-Handler via `formaction` |
| `form-action` | string | - | Explizite `formaction` |
| `form` | string | - | Id des Formulars (Button ausserhalb des Formulars) |
| `confirm` | string | - | Rueckfrage vor dem Klick |
| `size` | string | `md` | `sm` fuer Zeilen-/Toolbar-Aktionen |
| `full-width` | bool | `false` | Button ueber die ganze Breite |
| `target` / `title` | string | - | Fuer Links |
| `disabled` | bool | `false` | - |

### ms-tabs / ms-tab

`ms-tabs`: `active` (Key des aktiven Tabs), `remember` (merkt den Tab in
sessionStorage), `label` (aria-label der Tab-Leiste).

`ms-tab`: `key` (stabiler Schluessel, sonst aus dem Label), `label`, `icon`,
`badge`, `active`, `disabled`, `href`. Mit `href` wird der Tab ein Navigationslink
(fuer Unterseiten einer Gruppe), ohne `href` wird der Kindinhalt zum Panel und die
Umschaltung passiert ohne Reload.

### ms-card

| Attribut | Typ | Default | Bedeutung |
| --- | --- | --- | --- |
| `title` / `subtitle` | string | - | Kopfbereich |
| `icon` | string | - | Icon im Titel |
| `tone` | string | `default` | `accent`, `success`, `warning`, `danger`, `muted` |
| `href` | string | - | Ganze Karte ist ein Link (Dashboard-Tiles) |
| `flush` | bool | `false` | Kein Innenabstand (Karte enthaelt eine Tabelle) |
| `level` | int | `2` | Ueberschriftenebene |
| `header-action-url` | string | - | Kompakte Schnell-Aktion (Icon-Link) rechts im Kartenkopf, z. B. ein Plus zum direkten Hinzufuegen |
| `header-action-icon` | string | `plus` | Icon der Kopf-Aktion |
| `header-action-label` | string | `Hinzufuegen` | Beschriftung/Tooltip der Kopf-Aktion (aria-label + title) |

### ms-icon

`name` (Pflicht), `size` (Default 20), `label` (macht das Icon fuer Screenreader
sichtbar; ohne Label ist es dekorativ).

### ms-alert

`tone` (`info`, `success`, `warning`, `error`), `title`, `text`, `icon`,
`dismissible`. Kindinhalt ist erlaubt (z. B. ein Link).

### ms-money

`cents` (long, Pflicht), `currency` (Default `EUR`), `show-sign` (Plus-Zeichen und
gruene Farbe bei positiven Werten - fuer Salden), `strong` (groesser/fetter).
Negative Betraege sind immer rot, normale Betraege bleiben in der Textfarbe.

---

## 6. Icon-Namen

`home`, `group`, `users`, `user`, `expense`, `receipt`, `camera`, `balance`,
`history`, `admin`, `plus`, `edit`, `trash`, `save`, `back`, `forward`, `search`,
`filter`, `sort`, `menu`, `sun`, `moon`, `monitor`, `logout`, `link`, `merge`,
`paypal`, `settings`, `info`, `warning`, `check`, `close`, `chevron-down`,
`chevron-right`, `chevron-up`, `empty`, `copy`, `calendar`, `euro`, `download`,
`upload`, `logo`

Das Sprite steht in `Pages/Shared/_IconSprite.cshtml` und wird vom Layout einmal pro
Seite gerendert. Neue Icons dort als `<symbol id="ms-i-name" viewBox="0 0 24 24">`
ergaenzen (nur Pfade, Stroke/Fill kommen aus dem CSS).

---

## 7. CSS-Utilities (nur diese verwenden)

| Klasse | Zweck |
| --- | --- |
| `ms-stack` | Vertikale Liste mit Standardabstand |
| `ms-row` | Horizontale Zeile, umbrechend, zentriert |
| `ms-grid ms-grid--2` / `--3` | Responsives Kachelgitter (Dashboard) |
| `ms-kv` | Definitionsliste `<dl><dt>/<dd>` fuer Detailangaben |
| `ms-form__row` | Mehrere Felder in eine Zeile innerhalb eines `ms-form` |
| `ms-num` | Rechtsbuendige Zahlenspalte in Tabellen |
| `ms-table__rowactions` | Container fuer Aktionen in einer Tabellenzeile |
| `ms-chip` | Kleines Label (z. B. Faktor 3, Anonym) |
| `ms-badge` | Zahl-Badge |
| `ms-muted` | Sekundaerer Text |
| `ms-visually-hidden` | Nur fuer Screenreader |

Beispiel Detailseite:

```cshtml
<div class="ms-grid ms-grid--2">
    <ms-card id="group-info" title="Gruppe" icon="group">
        <dl class="ms-kv">
            <dt>Name</dt><dd>@Model.Group.Name</dd>
            <dt>Waehrung</dt><dd>@Model.Group.Currency</dd>
            <dt>Mitglieder</dt><dd>@Model.MemberCount</dd>
        </dl>
    </ms-card>
    <ms-card id="group-invite" title="Einladung" icon="link" tone="accent">
        <ms-field id="invite-url" type="static" label="Link" value="@Model.InviteUrl"></ms-field>
        <div class="ms-row">
            <ms-button id="invite-copy" kind="secondary" icon="copy" label="Kopieren"
                       type="button" class="ms-copy" data-ms-copy="#invite-url"></ms-button>
        </div>
    </ms-card>
</div>
```

`data-ms-copy="#elementId"` kopiert Wert/Text des Elements in die Zwischenablage
(oder direkt den Attributwert, wenn er nicht mit `#` beginnt).

---

## 8. JavaScript-Hooks (site.js)

| Attribut | Wirkung |
| --- | --- |
| `data-ms-confirm="Frage?"` | Rueckfrage vor Klick (setzt `ms-button confirm` automatisch) |
| `data-ms-copy="text\|#id"` | In die Zwischenablage kopieren |
| `data-ms-scroll` | Container mit App-Scrollbalken (Sichtbarkeit beim Scrollen) |
| `data-ms-dismiss="alertId"` | Meldung schliessen |
| `data-ms-camera="fileInputId"` | Kamera-Aufnahme mit getUserMedia, Fallback auf Datei-Dialog |
| `data-ms-drawer-toggle` / `-close` | Menue-Drawer auf Mobilgeraeten |
| `data-ms-theme="system\|dark\|light"` | Theme umschalten |

`window.MatSplit` bietet `init()`, `initValidation(scope)`, `initConditionals(scope)`,
`setTheme(mode)` und `validateForm(form)` - nach dynamisch nachgeladenem Markup
`MatSplit.initValidation(container)` und `MatSplit.initConditionals(container)` aufrufen.

Client-Validierung ist dependency-frei (kein jQuery, kein CDN) und liest die
`data-val-*`-Attribute, die `ms-field for="..."` erzeugt. `_ValidationScriptsPartial`
existiert nur aus Kompatibilitaetsgruenden und ruft `MatSplit.initValidation()`.

---

## 9. Anti-Patterns

* Kein `<table>` ohne `ms-table`, kein `<form>` ohne `ms-form`, kein `<button>` ohne
  `ms-button` (Ausnahme: reine Icon-Buttons in eigenen JS-Widgets).
* Keine Inline-Styles, keine neuen Farbwerte - nur die Tokens aus `theme.css`
  (`var(--ms-primary)` etc.).
* Keine externen Fonts, Icons oder Skripte (Offline-/PWA-Faehigkeit).
* Keine `double`/`decimal`-Felder fuer gespeicherte Betraege - `long` Cent.
* Kein `asp-page`/`asp-route` in den Shared-Partials: Menue und Breadcrumb nutzen
  feste Pfade, damit das Layout auch ohne noch fehlende Seiten rendert. Auf den
  Feature-Seiten selbst darf `asp-page` verwendet werden.

---

## 10. Validierung im Detail

* `ms-form` setzt immer `novalidate`. Die nativen Browser-Bubbles (englisch,
  nicht stylebar) sind damit aus; validiert wird ausschliesslich in `site.js` -
  auf `blur` pro Feld und beim Absenden fuer das ganze Formular.
* Geprueft werden die `data-val-*`-Regeln, die `ms-field for="..."` aus den
  DataAnnotations erzeugt: `required`, `length`/`maxlength`/`minlength`, `range`,
  `regex`, `email`, `number`, `equalto`. Felder ohne `for` werden anhand ihres
  `required`-Attributs und `type="email"` geprueft.
* Fehlermeldungen kommen aus `ErrorMessage` der DataAnnotation - **immer deutsch
  setzen**, sonst erscheint die englische Standardmeldung:

  ```csharp
  [Required(ErrorMessage = "Bitte eine Beschreibung angeben.")]
  [StringLength(120, ErrorMessage = "Maximal 120 Zeichen.")]
  [Display(Name = "Beschreibung")]
  public string Description { get; set; } = string.Empty;

  [Range(0.01, 1_000_000, ErrorMessage = "Bitte einen Betrag groesser als 0 angeben.")]
  [Display(Name = "Betrag")]
  public decimal Amount { get; set; }
  ```
* Checkboxen werden nie als Pflichtfeld behandelt (ein leeres Kontrollkaestchen
  postet `false` und ist ein gueltiger Wert) - deshalb bekommen sie auch kein
  Sternchen, selbst wenn das Model ein `bool` ohne `?` ist.
* Der Platz fuer die Fehlermeldung ist im Formular immer reserviert. Bitte keine
  eigenen Fehlerelemente einbauen, sonst springt das Layout beim Fokuswechsel und
  frisst den Klick auf "Speichern".
* Serverseitig gilt weiterhin `ModelState.IsValid`; die Fehler landen automatisch
  in der Zusammenfassung oben im Formular und unter den Feldern.

---

## 11. Was das Layout mitbringt (nicht selbst bauen)

* Menue links inklusive Gruppen-Untereintraegen, Administration (nur Admins),
  Konto/Abmelden und Theme-Umschalter (System, Dunkel, Hell).
* Header mit Titel, Untertitel, Burger-Button und Breadcrumb.
* Icon-Sprite, PWA-Metatags, Manifest, Service-Worker-Registrierung.
* Flash-Meldungen aus TempData.
* Off-Canvas-Drawer unter 900 px, Safe-Area-Insets fuer iPhone-Notch,
  Scrollbalken im App-Design.

Seiten fuellen also nur `ViewData`/`TempData` (Abschnitt 2) und rendern ihren
Inhalt mit den Controls - kein eigenes `<html>`, `<head>`, Menue oder Header.
