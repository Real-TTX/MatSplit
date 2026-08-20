# MatSplit Mobile Design-System (Mockup-Refresh)

Verbindlicher Vertrag fuer alle Seiten-Agenten. Diese Datei dokumentiert **alle**
neuen mobilen `.ms-*`-Komponenten, den `<ms-avatar>`-TagHelper und die neuen
Sprite-Icons, die das Fundament bereitstellt. Jede Seite wird **ausschliesslich**
aus diesen Klassen/Controls und den bereits in `docs/ui-controls.md` beschriebenen
Controls gebaut.

Leitplanken (Wiederholung):

- Es bleibt **MatSplit** (eigenes Branding). Niemals "Splid" schreiben.
- Farbwelt Orange, weisse Karten mit weichen Ecken (`--ms-radius-xl` = 22px),
  weiche Schatten, viel Weissraum. Alles folgt automatisch dem Dark-Mode
  (`[data-theme="dark"]`), weil ausschliesslich Tokens aus `theme.css` benutzt werden.
- Geld immer aus `long` Cent via `<ms-money cents="…">` (de-DE, Salden farbig).
- Die neuen Komponenten gelten fuer Mobile **und** Desktop. Nur `.ms-tabbar` ist
  mobil-only (blendet sich ab 901px aus) und wird bereits vom Layout automatisch
  eingebunden (`_MobileTabBar`) — **nicht** selbst einbauen.

Neue Design-Tokens: `--ms-radius-xl` (22px), `--ms-tabbar-height` (64px).

---

## `<ms-avatar>` — runder Initialen-Avatar

Runder Avatar aus 1-2 Initialen mit **deterministischer** Hintergrundfarbe aus dem
Namen (stabiler FNV-Hash, kein Zufall/Datum). Rein dekorativ (`aria-hidden`), der
Name steht daneben im Markup; `title` traegt den vollen Namen.

Signatur:

```
<ms-avatar name="Luca Bauer" size="sm|md|lg" you="true|false" id="opt"></ms-avatar>
```

| Attribut | Typ    | Default | Bedeutung                                             |
|----------|--------|---------|-------------------------------------------------------|
| `name`   | string | ""      | Basis fuer Initialen **und** Farbe. "(du)" wird ignoriert. |
| `size`   | string | `md`    | `sm` = 30px, `md` = 40px, `lg` = 56px.                |
| `you`    | bool   | false   | Markiert den aktuellen Nutzer mit oranger Ring-Umrandung. |
| `id`     | string | (auto)  | Optional.                                             |

```html
<ms-avatar name="Luca Bauer" size="md"></ms-avatar>
<ms-avatar name="Sarah" size="sm" you="true"></ms-avatar>
```

---

## `.ms-screen-title` / `.ms-screen-head` — Bildschirm-Ueberschrift

Grosse, luftige Ueberschrift (wie "Meine Gruppen"). `.ms-screen-head` ist die
Kopfzeile mit optionalem runden Aktions-Button rechts (z.B. das orange `+`).

```html
<div class="ms-screen-head">
    <h1 class="ms-screen-title">Meine Gruppen</h1>
    <a class="ms-screen-head__action" href="/Groups/Edit" aria-label="Neue Gruppe">
        <ms-icon name="plus" size="22"></ms-icon>
    </a>
</div>
```

---

## `.ms-balance-hero` — orange Gradient-Kontostand-Karte

Grosse Karte mit orangem Verlauf, weissem Text, optionalem Mini-Chart (Sparkline)
oben rechts. `<ms-money>` wird darin automatisch weiss dargestellt (nicht gruen/rot).

Slots: `__label`, `__amount`, `__share` (mit `<strong>`), `__chart` (Inline-SVG).

```html
<div class="ms-balance-hero">
    <span class="ms-balance-hero__label">Kontostand</span>
    <span class="ms-balance-hero__amount"><ms-money cents="4520" show-sign="true"></ms-money></span>
    <span class="ms-balance-hero__share">
        Dein Anteil <strong><ms-money cents="23540"></ms-money></strong>
        von <ms-money cents="127000"></ms-money>
    </span>
    <span class="ms-balance-hero__chart">
        <svg viewBox="0 0 108 46" aria-hidden="true" focusable="false">
            <path d="M2 34 20 28 38 32 56 18 74 22 92 8 106 12" />
        </svg>
    </span>
</div>
```

Den `d`-Pfad der Sparkline aus echten Datenpunkten erzeugen (Platzhalter oben).

---

## `.ms-grouplist` — Gruppen-Karten (Meine Gruppen)

Liste von Karten: Name (+Emoji) links, Label "Saldo" + farbiger Betrag rechts,
"X Mitglieder", oranger Fortschrittsbalken, "… € ausgegeben". Die Karte darf ein
`<a>` sein (Hover-Lift) oder ein `<div>`.

Slots: `__top`, `__name`, `__balance` (+ `__balance-label`), `__meta`, `__bar`
(Fuellung als inneres `<span>` mit `style="width:…%"`), `__spent`.

```html
<div class="ms-grouplist">
    <a class="ms-grouplist__item" href="/Groups/Details?groupId=1">
        <div class="ms-grouplist__top">
            <span class="ms-grouplist__name">Ibiza Trip 🏝️</span>
            <span class="ms-grouplist__balance">
                <span class="ms-grouplist__balance-label">Saldo</span>
                <ms-money cents="4520" show-sign="true"></ms-money>
            </span>
        </div>
        <span class="ms-grouplist__meta">7 Mitglieder</span>
        <span class="ms-grouplist__bar"><span style="width:62%"></span></span>
        <span class="ms-grouplist__spent"><ms-money cents="123480"></ms-money> ausgegeben</span>
    </a>
</div>
```

---

## `.ms-pills` / `.ms-pill` — Filter- und Tab-Pills

Waagerecht scrollbare Pillen. Aktive Pille orange (`.is-active`). Als `<a>`
(Filter/Navigation) oder `<button>` (JS-Tabs) verwendbar.

```html
<div class="ms-pills" role="tablist">
    <a class="ms-pill is-active" href="?filter=all">Alle</a>
    <a class="ms-pill" href="?filter=expenses">Ausgaben</a>
    <a class="ms-pill" href="?filter=payments">Zahlungen</a>
    <a class="ms-pill" href="?filter=joins">Beitritte</a>
</div>
```

---

## `.ms-feed` — Aktivitaeten-Feed

Avatar + Text (Satz mit fettem Namen, optional Titelzeile + Zeit) + optionaler
Betrag rechts. Slots: `__item`, `__avatar`, `__text` (mit `<strong>`, `__title`,
`__time`), `__amount`.

`__avatar` ist ein runder Container — entweder ein `<ms-avatar>` **oder** ein
Icon (z.B. Ausgleich/Beitritt):

```html
<div class="ms-feed">
    <!-- Ausgabe -->
    <div class="ms-feed__item">
        <span class="ms-feed__avatar"><ms-avatar name="Jonas" size="md"></ms-avatar></span>
        <span class="ms-feed__text">
            <strong>Jonas</strong> hat eine Ausgabe hinzugefügt
            <span class="ms-feed__title">Beach Club</span>
            <span class="ms-feed__time">Heute, 14:30</span>
        </span>
        <span class="ms-feed__amount"><ms-money cents="12000"></ms-money></span>
    </div>
    <!-- Ausgleich (Icon statt Avatar) -->
    <div class="ms-feed__item">
        <span class="ms-feed__avatar"><ms-icon name="balance" size="22"></ms-icon></span>
        <span class="ms-feed__text">
            <strong>Sarah</strong> hat den Saldo ausgeglichen
            <span class="ms-feed__time">Heute, 10:20</span>
        </span>
        <span class="ms-feed__amount"><ms-money cents="5000" show-sign="true"></ms-money></span>
    </div>
    <!-- Beitritt -->
    <div class="ms-feed__item">
        <span class="ms-feed__avatar"><ms-icon name="users" size="22"></ms-icon></span>
        <span class="ms-feed__text">
            <strong>Tom</strong> ist der Gruppe beigetreten
            <span class="ms-feed__time">17. Mai 2024</span>
        </span>
    </div>
</div>
```

---

## `.ms-listrow` — Icon-Zeilen (Letzte Ausgaben, Mitglieder)

Runder Icon-/Avatar-Kreis + Haupttext (+Untertitel) + rechter Betragsblock
(darf einen Untertitel wie die Uhrzeit tragen). Slots: `__icon`, `__main`
(mit `<strong>`/`__title` und `__sub`), `__amount` (rechtsbuendige Spalte,
kann `__sub` enthalten).

```html
<!-- Letzte Ausgaben -->
<div class="ms-listrow">
    <span class="ms-listrow__icon"><ms-icon name="receipt" size="20"></ms-icon></span>
    <span class="ms-listrow__main">
        <strong>Beach Club</strong>
        <span class="ms-listrow__sub">Jonas</span>
    </span>
    <span class="ms-listrow__amount">
        <ms-money cents="12000"></ms-money>
        <span class="ms-listrow__sub">Heute</span>
    </span>
</div>

<!-- Mitglieder -->
<div class="ms-listrow">
    <span class="ms-listrow__icon"><ms-avatar name="Luca" size="md" you="true"></ms-avatar></span>
    <span class="ms-listrow__main"><strong>Luca (du)</strong></span>
    <span class="ms-listrow__amount"><ms-money cents="4520" show-sign="true"></ms-money></span>
</div>
```

---

## `.ms-social` — runde Teilen-Buttons

Runde farbige Buttons mit Label darunter: WhatsApp (`--whatsapp`, #25D366),
Telegram (`--telegram`, #229ED9), Mehr (`--more`, neutral). Das Icon **im**
`.ms-social__btn` wird automatisch zum farbigen Kreis; Label folgt darunter.

```html
<div class="ms-social">
    <a class="ms-social__btn ms-social__btn--whatsapp" href="https://wa.me/?text=…">
        <ms-icon name="whatsapp" size="24"></ms-icon>
        <span>WhatsApp</span>
    </a>
    <a class="ms-social__btn ms-social__btn--telegram" href="https://t.me/share/url?url=…">
        <ms-icon name="telegram" size="24"></ms-icon>
        <span>Telegram</span>
    </a>
    <button type="button" class="ms-social__btn ms-social__btn--more" data-ms-share>
        <ms-icon name="dots" size="24"></ms-icon>
        <span>Mehr</span>
    </button>
</div>
```

---

## `.ms-hero-icon` — grosser runder Icon-Kreis

Fuer Teilen/Beitreten. Standard: oranger Gradient-Kreis, weisses Icon.
Variante `--soft`: weicher oranger Hintergrund, oranges Icon.

```html
<div class="ms-hero-icon"><ms-icon name="link" size="44"></ms-icon></div>
<div class="ms-hero-icon ms-hero-icon--soft"><ms-icon name="users" size="44"></ms-icon></div>
```

---

## `.ms-tabbar` — mobile Bottom-Navigation (automatisch)

Wird vom Layout via `_MobileTabBar` gerendert (Gruppen / Aktivitaeten / Profil),
ist fixiert am unteren Rand, respektiert `env(safe-area-inset-bottom)` und ist nur
`<= 900px` sichtbar. **Seiten muessen hier nichts tun** — nur wissen, dass unten
`--ms-tabbar-height` Platz reserviert ist (Layout kuemmert sich darum). Fuer den
aktiven Tab liest der Partial den Request-Pfad; "Aktivitaeten" springt in die
Historie der aktiven Gruppe, sofern `ViewData` eine aktive GroupId kennt
(`this.SetMenuGroups(..., activeGroupId: GroupId)`).

---

## Neue/relevante Sprite-Icons

Alle stroke-basiert (`fill:none; stroke:currentColor`), via `<ms-icon name="…">`.

Neu ergaenzt: `telegram`, `dots` (Mehr), `chart` (Sparkline/Trend), `activity`
(Aktivitaeten), `profile`, `arrow-left` (Zurueck).

Bereits vorhanden und hier relevant: `settings` (Zahnrad), `plus`, `share`,
`whatsapp`, `link`, `copy`, `users`, `user`, `receipt`, `expense`, `balance`,
`calendar`, `euro`, `back`, `history`, `check`, `close`, `chevron-down`,
`chevron-right`.

> Hinweis: Fuer den "Zurueck"-Pfeil gibt es sowohl `arrow-left` als auch das
> bestehende `back` (identische Optik) — beide sind nutzbar.
