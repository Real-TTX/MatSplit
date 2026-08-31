# Changelog

Alle nennenswerten Aenderungen an MatSplit werden hier dokumentiert.
Format orientiert an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionierung nach dem in der [README](README.md#versionierungsschema) beschriebenen Schema
(`<major>.<minor>.<run_number>-<yyyyMMdd>`).

## [Unreleased]

### Geplant

- Bestehende Benutzer per E-Mail in Gruppen einladen (aktuell: Einladungslink).
- Export der Gruppenhistorie (CSV/PDF).
- EF-Core-Migrations als Ersatz fuer `EnsureCreated()`.

## [0.5.0] - 2026-08-31

### Geaendert

- **Nur-Lese-Ansicht** (`/View`) neu gestaltet: Zusammenfassung mit Kennzahlen, Pill-Tabs
  „Transaktionen"/„Mitglieder" und Typ-Filter (Alle/Ausgaben/Zahlungen) — Ausgaben im Fokus.

### Behoben

- **Betraege mit Komma**: Geld-Eingabefelder akzeptieren jetzt Komma **und** Punkt als
  Dezimaltrenner (Text-Feld mit `inputmode=decimal`, Normalisierung auf den invarianten Punkt).
- **Auto-Zoom auf dem Handy**: Formularfelder sind auf kleinen Screens mindestens 16px gross,
  dazu `touch-action: manipulation` — kein Zoom mehr beim Antippen eines Feldes oder per Doppeltipp.
- Service-Worker-Cache-Version erhoeht (v7), damit installierte PWAs CSS/JS nach einem Update
  frisch laden.

## [0.4.0] - 2026-08-30

### Hinzugefuegt

- **Verwaltete Mitglieder ohne eigenen Zugang**: Ein Gruppen-Admin kann beim „Mitglied
  hinzufuegen" eine Person direkt per **Name** anlegen (kein Login, kein Konto, nur in
  dieser Gruppe) und alle Ausgaben/Anteile auf ihren Namen buchen. Zusammen mit dem
  Nur-Lese-Link laesst sich eine Gruppe so zentral verwalten und transparent teilen.

## [0.3.0] - 2026-08-27

### Hinzugefuegt

- **Nur-Lese-Link** je Gruppe: eine oeffentliche, schreibgeschuetzte Ansicht
  (`/View?token=...`) mit Kontostand, Ausgleich, Transaktionen und Mitgliedern — ohne
  Login, ohne Beitritt, ohne Bearbeiten. Eigener Token, standardmaessig aus, jederzeit
  deaktivier- und neu erzeugbar; Verwaltung auf `/Groups/Share`.

### Hinweise

- Schema-Erweiterung an `Group` (`ReadOnlyToken`, `ReadOnlyEnabled`). Da das Schema per
  `EnsureCreated()` entsteht, muss eine **bestehende** Datenbank neu angelegt oder die
  Spalte manuell nachgezogen werden.

## [0.2.0] - 2026-08-26

Grosses UI-Redesign nach Mockup: hub-zentrierte Navigation, mobil-first, dazu
Beleg-Komprimierung und geschaerfte Gaeste-Rechte.

### Hinzugefuegt

- **Gruppen-Hub** (`/Groups/Details`) als zentraler Einstieg: grosser Kontostand-Hero,
  Pill-Tabs "Transaktionen" und "Mitglieder", kombinierte Transaktionsliste (Ausgaben +
  Zahlungen chronologisch) und Icon-Zugaenge zu Historie, Teilen und Verwaltung.
- **Gruppe teilen** (`/Groups/Share`): eigener Bildschirm mit Einladungslink, Kopieren,
  WhatsApp-/Telegram-Verknuepfungen und der Link-Verwaltung (aktivieren, neu erzeugen).
- **Mitglieder-Filter** im Hub: Alle / Admins / Benutzer / Anonyme.
- **Ausgaben-Editor**: Live-Vorschau des Anteils je Person im Modus "Individuelle Anteile".
- **Beleg-Komprimierung**: Fotos werden im Browser vor dem Upload verkleinert; per
  Einstellung schaltbar mit Zielgroesse in KB (Standard 500 KB).
- Neue Controls: `<ms-avatar>` (deterministische Initialen), Icon-Sprite u. a. mit
  `ausgleich` (Zahlung) und `cog` (Verwaltung), sowie ein `TransactionService`.

### Geaendert

- **Startseite** ist die Gruppen-Uebersicht ("Meine Gruppen"); das Seitenmenue zeigt die
  Gruppen flach ohne Untermenue, jede verlinkt direkt auf ihren Hub.
- Separate Seite `/Groups/Members` entfernt — Mitgliederverwaltung laeuft ueber den
  Mitglieder-Tab des Hubs und die Unterseite `/Groups/MemberEdit`.
- Kontostand und Historie im neuen Listen-/Tabellenstil, Breadcrumbs verschlankt.
- **"Anteilsfaktor" heisst jetzt "Personen"** (Anzahl Personen) in der gesamten Oberflaeche.
- Mobiles + Desktop-Redesign (Orange/Sommer-Optik, weiche Karten, PWA); Tab- und
  Button-Beschriftungen werden auf schmalen Screens auf Icons reduziert.
- Admin-Bereich breiter, Uebersicht auf maximal 3 Kacheln; interne Pfad-/Volume-Infos aus
  der Oberflaeche entfernt.
- Historie mit Sortierung (Zeitpunkt, Aktion, Person).

### Sicherheit

- **Gaeste (anonyme Link-Mitglieder)** koennen keine Gruppen erstellen und keinen
  Einladungslink weitergeben — im UI ausgeblendet und serverseitig abgesichert.

### Behoben

- `BalanceService`: liegen die festen Anteile einer Ausgabe ueber dem Gesamtbetrag,
  stimmte die Summe der Anteile nicht mehr mit dem Ausgabenbetrag ueberein.
- `ExpenseService` / `PaymentService`: beim Speichern wird geprueft, dass der
  bearbeitete Datensatz zur uebergebenen Gruppe gehoert (Schutz vor Zugriff auf
  fremde Gruppen ueber eine untergeschobene Id).
- `MatSplitPaths.ResolveReceiptPath`: Prefix-Pruefung inklusive Verzeichnistrenner,
  damit ein Nachbarverzeichnis wie `/data/receipts-x` nicht als gueltig gilt.
- Pflichtfeld-Meldungen der Client-Validierung sind auch bei nicht-nullbaren
  Werttypen deutsch.

## [0.1.0] - 2026-08-20

Erste Version: vollstaendiger Funktionsumfang fuer Urlaubs- und WG-Abrechnung,
lauffaehig als einzelner Docker-Container.

### Hinzugefuegt

- **Gruppen**: anlegen, bearbeiten, soft-delete, Waehrung, Beschreibung, Mitgliederverwaltung.
- **Einladungen**: Einladungslink pro Gruppe (aktivierbar/deaktivierbar, Token neu erzeugbar),
  Beitritt als anonymer Benutzer ueber `/Join`.
- **Ausgaben**: erfassen, bearbeiten, filtern (Suche, Zahler, Zeitraum), Sortierung,
  Pagination; Anteile je Mitglied per Faktor oder festem Betrag.
- **Belege**: Foto-Upload zu einer Ausgabe inklusive Kamera-Zugriff auf dem Handy,
  Ablage unter `/data/receipts`.
- **Zahlungen**: dokumentieren, wer wem wie viel gegeben hat.
- **Kontostand & Ausgleich**: Salden je Mitglied und minimale Ausgleichszahlungen,
  optional mit `paypal.me`-Link zum Empfaenger.
- **Anteils-Faktoren**: pro Gruppenmitglied (z. B. Familie = Faktor 3), pro Ausgabe
  uebersteuerbar.
- **Benutzer zusammenfuehren**: anonyme Doppel-Eintraege (Horst/Horsti) zu einem Benutzer
  verschmelzen, inklusive Umhaengen von Ausgaben, Anteilen und Zahlungen.
- **Historie** je Gruppe mit Suche und Filter nach Aktion.
- **Auth**: Cookie-Authentication mit eigener Session-Tabelle, Rollen Admin / User /
  Anonymous, pro Gruppe zusaetzlich `IsGroupAdmin`.
- **Administration**: Benutzer-, Gruppen- und Einstellungsverwaltung
  (`/data/config/appconfig.json`).
- **UI**: Menue links (Gruppen mit Untereintraegen), Inhalt rechts mit Breadcrumb,
  Listen mit Toolbar oben und Aktionen unten, TagHelper-Controlbibliothek (`ms-*`),
  Orange/Sommer-Farbwelt, Dark/Light/System-Theme, mobiler Drawer unter 900px.
- **PWA**: Manifest, Service Worker mit Offline-Faehigkeit und Sync, eigenes Logo/Icons,
  iOS- und Android-Besonderheiten (Safe-Area, Standalone, Apple-Touch-Icon).

### Infrastruktur

- Multi-Stage `Dockerfile` (`sdk:10.0` -> `aspnet:10.0`), non-root User `app`,
  `HEALTHCHECK` auf `/health`, Restore-Layer vor dem Source-Copy fuer Layer-Caching.
- `docker-compose.yml` (Release): Service `msbi`, Port `4774:8080`, Volume
  `matsplit-data:/data`, `restart: unless-stopped`, Healthcheck.
- `docker-compose.dev.yml` (Dev): Service `msbi` aus lokalem Build plus SQLite-Browser
  (`coleifer/sqlite-web`) auf Port `4775` am selben Volume.
- Persistente Daten unter `/data` (`db`, `config`, `receipts`, `keys`, `logs`); die
  DataProtection-Keys liegen in `/data/keys`, damit Sessions Container-Restarts ueberleben.
- Build-Skripte `scripts/build.ps1` und `scripts/build.sh` (Image `local-<yyyyMMdd>` +
  Redeploy des Dev-Stacks).
- GitHub-Actions-Workflow `docker-build.yml`: Job `build-test` (dotnet build/publish) und
  Job `docker` (Buildx, GHCR-Push mit optionalem Login, Tags fuer `main`/`dev`).
- `README.md` mit Quickstart, Ports, Volume-Layout, Versionierungs- und Branch-Schema sowie
  dem Abschnitt "Abweichungen von der Spezifikation".

### Bekannte Einschraenkungen

- Schema wird via `Database.EnsureCreated()` erzeugt; es gibt noch keine Migrations, ein
  Schema-Upgrade erfordert manuelles Eingreifen.
- Kein Multi-Arch-Image: der CI-Build erzeugt nur `linux/amd64`.
- Kein integriertes TLS — HTTPS bitte ueber einen Reverse Proxy davor.

[Unreleased]: https://github.com/Real-TTX/MatSplit/compare/v0.5.0...dev
[0.5.0]: https://github.com/Real-TTX/MatSplit/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Real-TTX/MatSplit/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/Real-TTX/MatSplit/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Real-TTX/MatSplit/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Real-TTX/MatSplit/releases/tag/v0.1.0
