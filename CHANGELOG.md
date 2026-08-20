# Changelog

Alle nennenswerten Aenderungen an MatSplit werden hier dokumentiert.
Format orientiert an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionierung nach dem in der [README](README.md#versionierungsschema) beschriebenen Schema
(`<major>.<minor>.<run_number>-<yyyyMMdd>`).

## [Unreleased]

### Geaendert

- **Mitglieder ohne Inline-Edits**: `/Groups/Members` ist jetzt eine reine Liste,
  Anlegen/Bearbeiten/Entfernen einer Mitgliedschaft laeuft ueber die neue Unterseite
  `/Groups/MemberEdit` (UI-Guideline "CRUD immer Liste + separate Unterseite").
- **Historie** hat eine Sortierung in der Toolbar (Zeitpunkt, Aktion, Person),
  umgesetzt in `HistoryService.ListHistoryAsync`.
- Pflichtfeld-Meldungen der Client-Validierung sind auch bei nicht-nullbaren
  Werttypen deutsch (Rolle, Sitzungsdauer, Beleggroesse, Anteilsfaktor).

### Behoben

- `BalanceService`: liegen die festen Anteile einer Ausgabe ueber dem Gesamtbetrag,
  stimmte die Summe der Anteile nicht mehr mit dem Ausgabenbetrag ueberein.
- `ExpenseService` / `PaymentService`: beim Speichern wird geprueft, dass der
  bearbeitete Datensatz zur uebergebenen Gruppe gehoert (Schutz vor Zugriff auf
  fremde Gruppen ueber eine untergeschobene Id).
- `MatSplitPaths.ResolveReceiptPath`: Prefix-Pruefung inklusive Verzeichnistrenner,
  damit ein Nachbarverzeichnis wie `/data/receipts-x` nicht als gueltig gilt.

### Geplant

- Bestehende Benutzer per E-Mail in Gruppen einladen (aktuell: Einladungslink).
- Export der Gruppenhistorie (CSV/PDF).
- EF-Core-Migrations als Ersatz fuer `EnsureCreated()`.

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

[Unreleased]: https://github.com/Real-TTX/MatSplit/compare/main...dev
[0.1.0]: https://github.com/Real-TTX/MatSplit/releases/tag/v0.1.0
