# MatSplit

**Gemeinsame Finanzen fuer Urlaub und WG — selfhosted, in einem Docker-Container.**

MatSplit ist eine selbst gehostete Alternative zu Splid: Gruppen anlegen, Ausgaben erfassen,
Belege per Handy-Kamera fotografieren, Zahlungen dokumentieren und am Ende sehen, wer wem
wie viel schuldet — inklusive PayPal-Link fuer den Ausgleich.

---

## Inhalt

- [Features](#features)
- [Technik](#technik)
- [Quickstart (Release)](#quickstart-release)
- [Entwicklung (Dev-Stack)](#entwicklung-dev-stack)
- [Erste Anmeldung](#erste-anmeldung)
- [Ports](#ports)
- [Volume-Layout](#volume-layout)
- [Konfiguration](#konfiguration)
- [Build-Skripte](#build-skripte)
- [Versionierungsschema](#versionierungsschema)
- [Branch-Strategie](#branch-strategie)
- [CI/CD](#cicd)
- [Repo-Struktur](#repo-struktur)
- [Abweichungen von der Spezifikation](#abweichungen-von-der-spezifikation)

---

## Features

- **Gruppen** fuer Urlaub, WG oder Projekt — mit Waehrung, Beschreibung und Historie.
- **Mitglieder** einladen: per Einladungslink (anonyme Teilnehmer, kein Konto notwendig)
  oder als bestehender Benutzer.
- **Ausgaben** mit Betrag, Zahler, Datum, Kategorie und **Belegfotos**
  (Kamera-Zugriff direkt aus der PWA).
- **Anteils-Faktoren**: eine Familie zaehlt z. B. als Faktor 3, Einzelpersonen als 1.
  Faktoren gelten pro Gruppe und sind pro Ausgabe uebersteuerbar.
- **Zahlungen** erfassen (wer hat wem wie viel gegeben).
- **Kontostand und Ausgleich**: minimale Anzahl Ausgleichszahlungen, optional mit
  `paypal.me`-Link zum Empfaenger.
- **Anonyme Benutzer zusammenfuehren** ("Horst" und "Horsti" sind dieselbe Person).
- **PWA**: installierbar, offlinefaehig mit Sync, optimiert fuer iOS und Android
  (Safe-Area/Notch, Standalone-Modus).
- **Dark / Light / System** Theme, Menue links, mobil als Drawer.

## Technik

| Baustein     | Auswahl                                                      |
|--------------|--------------------------------------------------------------|
| Runtime      | .NET 10 (`net10.0`), ASP.NET Core **Razor Pages**            |
| Datenbank    | SQLite via EF Core, Schema per `EnsureCreated()` beim Start   |
| Auth         | Cookie-Authentication + eigene Tabelle `UserSessions`         |
| Geldbetraege | immer `long` in Cent (`...Cents`) — nie Fliesskomma           |
| Container    | Multi-Stage Image, non-root User `app`, Healthcheck `/health` |
| Persistenz   | ein einziges Volume: `/data`                                  |

## Quickstart (Release)

Voraussetzungen: Docker mit Compose-Plugin.

```bash
git clone https://github.com/Real-TTX/MatSplit.git
cd MatSplit
docker compose up -d
```

App aufrufen: **http://localhost:4774**

Das Compose-File verwendet standardmaessig das Image `ghcr.io/real-ttx/matsplit:latest`.
Soll stattdessen lokal aus den Quellen gebaut werden:

```bash
docker compose up -d --build
```

Aktualisieren:

```bash
docker compose pull
docker compose up -d
```

Stoppen (Daten bleiben im Volume `matsplit-data`):

```bash
docker compose down
```

## Entwicklung (Dev-Stack)

```bash
docker compose -f docker-compose.dev.yml up -d --build
```

Damit laufen zwei Container:

| Container                | Zweck                                          | URL                   |
|--------------------------|------------------------------------------------|-----------------------|
| `matsplit-dev`           | die App (`ASPNETCORE_ENVIRONMENT=Development`) | http://localhost:4774 |
| `matsplit-dev-sqliteweb` | SQLite-Browser auf `/data/db/matsplit.db`      | http://localhost:4775 |

Beide Container haengen am selben Volume `matsplit-dev-data`, der Browser sieht also genau
die Datenbank der App.

> Hinweis zum SQLite-Browser: Er laeuft absichtlich mit `user: "1654:1654"` — der UID des
> `app`-Users im Runtime-Image. Ohne das arbeitet der Container als `root` und alles, was er
> auf dem gemeinsamen Volume anlegt, gehoert `root`; die App (UID 1654) stirbt dann beim
> Start mit `SQLite Error 8: attempt to write a readonly database`. Zusaetzlich wartet der
> Browser via `depends_on: condition: service_healthy` und einer Warteschleife auf eine
> nicht-leere Datenbankdatei, denn `sqlite_web` erzeugt die Datei sonst selbst — leer.

Logs:

```bash
docker compose -f docker-compose.dev.yml logs -f msbi
```

Ohne Container entwickeln (Windows-Host):

```powershell
dotnet run --project src/MatSplit.Web/MatSplit.Web.csproj
```

## Erste Anmeldung

Beim ersten Start wird ein Administratorkonto angelegt:

| Feld       | Wert                    |
|------------|-------------------------|
| Anmeldung  | `admin` **oder** `admin@matsplit.local` |
| Passwort   | `admin`                 |

> **Das Passwort direkt nach der ersten Anmeldung unter *Konto* aendern.**
> Die Anmeldung akzeptiert Anzeigename und E-Mail-Adresse, jeweils ohne
> Beachtung der Gross-/Kleinschreibung.

## Ports

| Port (Host) | Port (Container) | Dienst                                 |
|-------------|------------------|----------------------------------------|
| **4774**    | 8080             | MatSplit Web-App (Release **und** Dev) |
| **4775**    | 8080             | SQLite-Browser (nur Dev-Stack)         |

Der Container lauscht intern immer auf `8080` (`ASPNETCORE_URLS=http://+:8080`) und laeuft
als non-root — deshalb kein privilegierter Port.

## Volume-Layout

Alles Veraenderliche liegt unter `/data` (Release: Volume `matsplit-data`,
Dev: `matsplit-dev-data`):

```
/data
  db/         SQLite-Datenbank        -> /data/db/matsplit.db
  config/     App-Konfiguration       -> /data/config/appconfig.json
  receipts/   Belegfotos (Uploads)
  keys/       ASP.NET DataProtection-Keys (Cookies/Sessions ueberleben Restarts)
  logs/       Logdateien
```

Die Verzeichnisse werden im Image angelegt und dem User `app` (uid/gid `1654`) uebergeben;
ein neu erzeugtes Docker-Volume erbt diese Rechte automatisch.

> Wird statt eines Volumes ein **Bind-Mount** verwendet (`-v /srv/matsplit:/data`), muss das
> Hostverzeichnis dem Container-User gehoeren, sonst kann die App nicht schreiben:
> `sudo mkdir -p /srv/matsplit && sudo chown -R 1654:1654 /srv/matsplit`.

**Backup** = Volume sichern, z. B.:

```bash
docker run --rm -v matsplit-data:/data -v "$PWD":/backup alpine \
  tar czf /backup/matsplit-data-$(date +%Y%m%d).tar.gz -C / data
```

**Restore**:

```bash
docker compose down
docker run --rm -v matsplit-data:/data -v "$PWD":/backup alpine \
  sh -c "rm -rf /data/* && tar xzf /backup/matsplit-data-20260820.tar.gz -C /"
docker compose up -d
```

## Konfiguration

Fachliche Einstellungen stehen in `/data/config/appconfig.json` und werden beim ersten Start
mit Defaults erzeugt (`AppName`, `DefaultCurrency`, `AllowAnonymousJoin`,
`SessionLifetimeDays`, `MaxReceiptSizeMb`). Sie sind zusaetzlich unter
*Administration -> Einstellungen* pflegbar.

Umgebungsvariablen des Containers:

| Variable                 | Default         | Bedeutung                                    |
|--------------------------|-----------------|----------------------------------------------|
| `ASPNETCORE_ENVIRONMENT` | `Production`    | `Development` aktiviert Detailfehlerseiten   |
| `ASPNETCORE_URLS`        | `http://+:8080` | interner Listener                            |
| `MATSPLIT_DATA_DIR`      | `/data`         | Wurzel fuer db/config/receipts/keys/logs     |
| `TZ`                     | `Europe/Berlin` | Zeitzone des Containers                      |
| `MATSPLIT_VERSION`       | `local`         | nur Build-Arg fuer Compose (`APP_VERSION`)   |

Der Container-Healthcheck ruft `GET http://127.0.0.1:8080/health` auf und erwartet
HTTP 200 — dieser Endpunkt muss ohne Anmeldung erreichbar sein, sonst gilt der Container
als `unhealthy`.

TLS und Domain uebernimmt ein Reverse Proxy davor (nginx, Traefik, Caddy): Weiterleitung auf
`http://<host>:4774`, fuer korrekte Redirects `X-Forwarded-Proto` und `X-Forwarded-For`
setzen.

## Build-Skripte

Beide Skripte bauen das Image mit Version `local-<yyyyMMdd>` und deployen anschliessend den
Dev-Stack neu (`docker compose -f docker-compose.dev.yml up -d --build`):

```powershell
# Windows / PowerShell
./scripts/build.ps1                    # Debug-Build + Redeploy
./scripts/build.ps1 -NoCache -Follow   # ohne Layer-Cache, danach Logs folgen
./scripts/build.ps1 -BuildOnly         # nur Image bauen, Stack unangetastet
```

```bash
# Linux / macOS / Git Bash
scripts/build.sh                       # Debug-Build + Redeploy
scripts/build.sh --no-cache --follow
scripts/build.sh --build-only --release
```

Ergebnis-Tags: `matsplit:local-<yyyyMMdd>` und `matsplit:local`; im Dev-Stack laeuft das Tag
`matsplit:dev`.

## Versionierungsschema

Basis sind `VersionMajor` und `VersionMinor` aus `Directory.Build.props`.

| Ausloeser                | Image-Tags                                                 |
|--------------------------|------------------------------------------------------------|
| Push auf `main`          | `<major>.<minor>.<run_number>-<yyyyMMdd>` **und** `latest`  |
| Push auf `dev`           | `nightly-<run_number>-<yyyyMMdd>`                          |
| anderer Branch (manuell) | `<branch>-<run_number>-<yyyyMMdd>`                         |
| lokaler Build            | `local-<yyyyMMdd>` (Fallback ohne Datum: `local`)          |

Beispiel: `ghcr.io/real-ttx/matsplit:0.1.42-20260820`.
`run_number` ist die fortlaufende Nummer des GitHub-Actions-Workflows und damit monoton
steigend — es gibt keine manuelle Patch-Pflege.

## Branch-Strategie

```
dev   -> Feature-Arbeit, baut "nightly-*"-Images
main  -> stabiler Stand, baut "<major>.<minor>.<run>-<datum>" + "latest"
```

- Entwicklung passiert auf `dev` bzw. auf Feature-Branches, die per PR nach `dev` gehen.
- Ein Release ist ein Merge/PR `dev -> main`; danach genuegt auf dem Server
  `docker compose pull && docker compose up -d`.
- Pull Requests laufen nur durch den Job `build-test` (Kompilieren, kein Image-Push).

## CI/CD

Workflow: `.github/workflows/docker-build.yml`

1. **`build-test`** — `dotnet restore` / `build` / `publish` (Smoke-Test) auf
   `ubuntu-latest` mit .NET 10. Laeuft bei Push auf `main`/`dev` und bei jedem Pull Request.
2. **`docker`** — Buildx-Build des Images mit GitHub-Actions-Layer-Cache, Tags nach obigem
   Schema, Push nach GHCR. Laeuft nur bei Push oder manuellem Start, nicht bei Pull Requests.

Der Registry-Login ist bewusst optional: existiert das Secret `GHCR_TOKEN`, wird damit
angemeldet; andernfalls wird bei Repositories des Owners `Real-TTX` der `GITHUB_TOKEN`
verwendet. In Forks ohne Zugangsdaten wird das Image nur gebaut und **nicht** gepusht — der
Workflow schlaegt also nicht fehl (kein Hard-Fail).

Optionale Secrets:

| Secret       | Zweck                                                   |
|--------------|---------------------------------------------------------|
| `GHCR_TOKEN` | Personal Access Token (`write:packages`) fuer GHCR-Push |
| `GHCR_USER`  | abweichender Benutzername zum Token (Default: `actor`)  |

## Repo-Struktur

```
MatSplit/
  MatSplit.sln
  Directory.Build.props            Version (VersionMajor / VersionMinor)
  Dockerfile                       Multi-Stage: sdk:10.0 -> aspnet:10.0, non-root
  .dockerignore / .gitignore
  docker-compose.yml               Release-Stack (Port 4774)
  docker-compose.dev.yml           Dev-Stack     (Port 4774 + SQLite-Browser 4775)
  scripts/
    build.ps1                      Build + Redeploy (Windows)
    build.sh                       Build + Redeploy (bash)
  .github/workflows/docker-build.yml
  CHANGELOG.md
  src/
    MatSplit.Web/                  Razor Pages App: Data/, Services/, Ui/, Pages/, wwwroot/
```

## Abweichungen von der Spezifikation

**DB-Browser im Dev-Stack: `coleifer/sqlite-web` statt `mssql-server` / `pgadmin`.**

Urspruenglich waren fuer den Entwicklungs-Stack ein `mssql-server`-Container und `pgadmin`
als Datenbank-Oberflaeche vorgesehen. Das passt nicht zur getroffenen Technik-Entscheidung
"SQLite via EF Core":

- `mssql-server` ist ein **eigener Datenbankserver** (Microsoft SQL Server) — er wuerde eine
  zweite, ungenutzte Datenbank betreiben und die SQLite-Datei nicht anfassen.
- `pgadmin` ist ein Client **ausschliesslich fuer PostgreSQL** und kann sich nicht mit einer
  SQLite-Datei verbinden.

Als funktional aequivalenter Ersatz laeuft daher `coleifer/sqlite-web` auf Port **4775** am
selben Volume wie die App und oeffnet direkt `/data/db/matsplit.db` (Tabellen browsen,
Abfragen ausfuehren, Schema ansehen).

**Weitere Punkte**

- Keine EF-Core-Migrations in v1: Das Schema entsteht beim Start via
  `Database.EnsureCreated()` (so festgelegt) — Schemaaenderungen erfordern damit vorerst ein
  Neuanlegen bzw. manuelles Nachziehen der Datenbank.
- Das Runtime-Image installiert zusaetzlich `curl`, weil `mcr.microsoft.com/dotnet/aspnet`
  kein HTTP-Client-Tool mitbringt, der `HEALTHCHECK` auf `/health` aber eines benoetigt.
- Das Image wird ohne AppHost gebaut (`UseAppHost=false`), Start erfolgt via
  `dotnet MatSplit.Web.dll` — dadurch bleibt der Build architekturneutral und
  buildx-freundlich.
- Der Dev-Stack nutzt ein eigenes Volume (`matsplit-dev-data`), damit Testdaten die
  Release-Daten (`matsplit-data`) nicht ueberschreiben.
- Der Dev-Stack-Container `sqliteweb` laeuft mit `user: "1654:1654"` und wartet auf einen
  gesunden App-Container. Grund: Er wuerde sonst als `root` eine leere, root-eigene
  `matsplit.db` auf dem gemeinsamen Volume anlegen und die App (UID 1654) mit
  `SQLite Error 8: attempt to write a readonly database` in eine Restart-Schleife schicken.
- `DataVolumeGuard` prueft beim Start, ob `/data/db` und die Datenbankdatei beschreibbar
  sind. Eine vorhandene, leere und nicht beschreibbare Datei wird geloescht (Schema wird neu
  erzeugt), bei einer Datei **mit** Inhalt bricht der Start mit einer klaren Meldung inkl.
  `chown`-Hinweis ab statt mit einem SQLite-Stacktrace.
- Das Runtime-Image setzt `HTTP_PORTS=""`. Das Basis-Image `mcr.microsoft.com/dotnet/aspnet`
  setzt `HTTP_PORTS=8080`, wodurch Kestrel bei jedem Start
  `Overriding HTTP_PORTS ... Binding to values defined by URLS` warnt. `ASPNETCORE_URLS`
  bleibt die einzige Quelle der Port-Konfiguration.
- Das PWA-Manifest heisst `wwwroot/manifest.webmanifest` (nicht `manifest.json`),
  weil das die offizielle Dateiendung mit dem korrekten MIME-Typ
  `application/manifest+json` ist.
- Kein QR-Code fuer den Einladungslink: ohne externe Bibliothek/CDN gibt es
  keinen Generator. Die Mitglieder-Seite zeigt den Link stattdessen gross,
  markierbar und mit Kopier-Button.
- Kein jQuery / `jquery-validation-unobtrusive`. Die clientseitige Validierung
  laeuft ueber `wwwroot/js/site.js` (`MatSplit.initValidation()`), damit die App
  ohne externe CDN und offline funktioniert.
- Belegfotos werden offline **nicht** in die Outbox gelegt (ein `File` in
  `FormData` laesst sich nicht zuverlaessig persistieren). Offline gespeicherte
  Ausgaben weisen darauf hin, das Foto spaeter nachzureichen.
- Der Service Worker registriert sich nur unter `https://` oder
  `http://localhost`. Beim Zugriff ueber die LAN-IP (`http://192.168.x.x:4774`)
  gibt es keine Offline-Faehigkeit — dafuer braucht es einen Reverse Proxy mit
  TLS.
- Zusaetzliche Seite `Pages/Groups/MemberEdit.cshtml` (Route
  `/Groups/MemberEdit?groupId=..&userId=..`), die in der Seitenliste der
  Spezifikation nicht aufgefuehrt ist. Sie ist noetig, damit die Regel
  "CRUD immer Liste + separate Unterseite, keine Inline-Edits" auch fuer
  Mitgliedschaften gilt: `/Groups/Members` ist eine reine Liste, Anteilsfaktor,
  Gruppen-Admin-Flag, Hinzufuegen und Entfernen laufen ueber diese Unterseite.
- Es gibt keine automatisierten Tests. Der Build (`dotnet build`, 0 Warnungen)
  und ein manueller Durchlauf sind die Verifikation von v1.
- Kein `LICENSE`-File, deshalb auch kein Lizenz-Label in den OCI-Labels des
  Images.
