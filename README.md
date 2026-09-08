# SilexGIS

Web application for storage, viewing and editing of cave / karst topographic, geographic
and other associated data. Built for caving clubs, researchers and individuals to self-host.

**v2 (2026):** full rebuild on a modern stack —
ASP.NET Core minimal API (.NET, LTS) · PostgreSQL/PostGIS · EF Core + NetTopologySuite ·
React + TypeScript · Ant Design · OpenLayers · Docker-first deployment.

Previous versions:
- **v1** (PHP/MySQL/OpenLayers 3) — live at [speosilex.ro/silexgis](https://speosilex.ro/silexgis/en/index.php)
- **v1.2** (2022, React/Laravel, partial) — archived on the [`v2-archive`](../../tree/v2-archive) branch

## Try it

A public test installation runs at **[silexgistest.speotopo.ro](https://silexgistest.speotopo.ro)**.
Sign in with any of the demo accounts below — the login page lists them too, with a fill-in
button and a note on what each may do:

| Account | Password | What it may do |
|---|---|---|
| `admin@test.local` | `test-login-pass-1` | full administrator — everything, accounts and settings included |
| `editor@test.local` | `test-login-pass-1` | creates and edits content; no user or permission administration, and exact locations of protected caves stay hidden |
| `viewer@test.local` | `test-login-pass-1` | the signed-in baseline — browses what is visible to all accounts |

Everything there is public and disposable — do not put anything real in it. The data returns
to a curated baseline on a daily schedule, so feel free to create, edit and delete while
exploring. Running a test installation of your own is described in
[docs/INSTALL.md](docs/INSTALL.md#running-a-test-installation-public-demo-logins).

**Using it?** The handbook for cavers, archivists and whoever runs the installation is
**[docs/user-guide/](docs/user-guide/README.md)** — what the application is, the workflows,
and a page per feature. In English and **[română](docs/user-guide/ro/README.md)**, page for page.
The list below is the summary; **[docs/FEATURES.md](docs/FEATURES.md)** is the same list in full.

## Features

- **Cave registry** — caves, entrances with precise coordinates, taxonomies, morphometry and
  discovery data, with protection for sensitive locations.
- **Map workspace** — OpenLayers map with base layers and overlays, geometry editing with
  snapping and undo, measurement, and search. Works on a phone, editing included.
- **3D** — a globe with the surveys drawn in place: Therion `.lox` / Survex `.3d` models, cave
  walls from `.stl`, centerlines, and optional real relief baked from free elevation data.
- **One model for everything on the map** — caves, entrances, centerlines and surface features
  share a single feature model, so anything can contain or be linked to anything else.
- **Files, media and documents** — photo galleries, audio, video, georeferenced raster overlays,
  and documents filed in cabinets, read in the browser without downloading and discussed where
  they live.
- **Full-text search inside documents** — accent-insensitive, stemmed per language, quoting the
  sentence that matched, and never showing you a document you may not read.
- **Import and export** — GPX, KML/KMZ, Shapefile, GeoJSON, WKT and CSV, through a review screen
  that proposes what each waypoint is; photographs become the places they show, positioned from
  the camera fix or by matching against a track.
- **Trips, plans and expeditions** — logged over the days they ran, with who was there and what
  they did, invitations and waiting lists, checklists, a callout alarm, statistics per person,
  cave and club, and a report that writes itself up as a Word document.
- **Club events and one calendar** — meetings, training, deadlines and everything else dated,
  answered the way trips are, in one window that never names a cave.
- **Notifications you control** — a grid of what happens against how to reach you, an in-app
  inbox, per-club announcements, and a delivery page showing whether the mail is actually going
  out.
- **Accounts and permissions** — editable permission groups rather than fixed roles, per-object
  grants, two-factor sign-in, optional external login, and an explainer answering "why can this
  person see that?".
- **Share links** — revocable, public or sign-in-only, and never revealing a protected location.
- **Operator-controlled email and SMS** — any SMTP server, any HTTP SMS gateway, every message
  editable per language; configure nothing and the app still runs.
- **English and Romanian** throughout, screens, dates and sent messages alike.

Each of these is a paragraph or several in **[docs/FEATURES.md](docs/FEATURES.md)**, and a page
of its own in the [user guide](docs/user-guide/README.md).

## Quick start

```bash
git clone --recurse-submodules https://github.com/apgeo/silexgis.git
cd silexgis/deploy
cp .env.example .env          # set the DB password, admin account and public URL
docker compose up -d          # → http://localhost:8080
```

See **[docs/INSTALL.md](docs/INSTALL.md)** for HTTPS, encryption at rest, backups, upgrades,
external login providers, and the non-Docker (Linux/Windows) install path.

Word, Excel and OpenDocument files are stored, searched and downloaded out of the box, but
have no pages to show in the browser — those formats have none until something lays them out.
An optional converter service gives them real pages; see
[Showing Word and Excel files](docs/INSTALL.md#showing-word-and-excel-files).

SilexGIS stores uploaded files and database rows unencrypted, and expects the operator to
encrypt the volume or disk beneath them. What that protects against, what it does not, and how
to set it up is in [Encryption at rest](docs/INSTALL.md#encryption-at-rest).

## Repository layout

```
server/   ASP.NET Core API (.NET solution: Api / Domain / Infrastructure + tests)
client/   React + TypeScript SPA (Vite, Ant Design, OpenLayers)
deploy/   Docker Compose, TLS and terrain overlays (serving, and the optional tile-making
          worker), reverse-proxy configs, backup/restore, terrain pre-bake and the
          mobile-sync development server scripts
docs/     Installation and operations documentation, and the mobile-sync integration
          contract in docs/speleoloc-sync/
contract/ Recorded HTTP exchanges that are the mobile-sync contract, asserted byte for
          byte by the API test suite
```

### The mobile-sync integration

SilexGIS serves a row-level sync API for the SpeleoLoc cave-navigation application: a phone signs in
as an installed application, names the caves it carries, reads them a page at a time and writes its
own edits back, with the server arbitrating each row. A code printed on a cave label also resolves
to a public landing address.

Whoever writes a client for it starts at [docs/speleoloc-sync/README.md](docs/speleoloc-sync/README.md).
The recorded exchanges under `contract/speleoloc-sync/` are the specification of the wire; they are
compared byte for byte by the test suite, so the way anyone finds out the contract moved is that the
server's own tests fail.

For a server to write that client against — its own database container, its own ports, seeded with
the group, the second account and the protected cave that make the rules observable:

```bash
node deploy/speleoloc-dev.mjs up
```

It prints the accounts, the client id and the whole sign-in sequence when it is up.
[docs/speleoloc-sync/07-dev-server.md](docs/speleoloc-sync/07-dev-server.md) has the rest.

## Development

```bash
docker compose -f deploy/docker-compose.dev.yml up -d db   # PostGIS only
dotnet run --project server/src/SilexGis.Api                # API on :5080
cd client && npm ci && npm run dev                          # Vite dev server
```

### Tests

The fast checks — server build, Domain and Architecture tests, the client gate, the script
tests — finish in about a minute; `.github/workflows/ci.yml` lists the exact commands. The API
integration suite (Testcontainers + PostGIS) is measured in hours: `scripts/gate-affected.mjs`
names the test classes that answer for a change so those can run first, and
`scripts/gate-lock.mjs` makes integration runs on one machine take turns instead of colliding.

## License

AGPL-3.0-or-later — see [LICENSE](LICENSE). Third-party bundled components are listed in
[NOTICE](NOTICE).
