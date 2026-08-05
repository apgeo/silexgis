# SilexGIS

Web application for storage, viewing and editing of cave / karst topographic, geographic
and other associated data. Built for caving clubs, researchers and individuals to self-host.

**v3 (2026):** full rebuild on a modern stack —
ASP.NET Core minimal API (.NET, LTS) · PostgreSQL/PostGIS · EF Core + NetTopologySuite ·
React + TypeScript · Ant Design · OpenLayers · Docker-first deployment.

Previous versions:
- **v1** (PHP/MySQL/OpenLayers 3) — live at [speosilex.ro/silexgis](https://speosilex.ro/silexgis/en/index.php)
- **v2** (2022, React/Laravel, partial) — archived on the [`v2-archive`](../../tree/v2-archive) branch

## Features

- **Cave registry** — caves, entrances with precise coordinates, taxonomies, morphometry,
  discovery data, and protection of sensitive locations.
- **Map workspace** — OpenLayers map with base layers, cave/feature/geofile overlays,
  geometry editing with snapping and undo, measurement, and search (internal + Nominatim).
- **Surface features, files & media** — typed symbols and properties, photo galleries,
  documents, audio and video, and georeferenced raster (COG) map overlays. A document has a
  kind (survey report, permit, trip report, …) and each kind decides which details its
  documents are asked for, so an archive can be organized the way its club actually files
  things. Uploads are accepted up to 512 MB by default, and the limit is a setting. The text
  of an uploaded document is read in the background — PDF, Word, Excel, PowerPoint, LibreOffice,
  Rich Text, plain text, Markdown and CSV, including the pre-2007 Word and PowerPoint files a club
  archive is full of and files written in the older Central European code pages Romanian archives
  are full of. A password-protected document is reported as locked rather than read into nonsense.
  Nothing recognises text in a photograph, so scanned
  pages and image-only PDFs have no text to read, and the document panel says exactly that
  instead of leaving you waiting for words that are never coming.
- **Search inside documents** — the same search box that finds caves and trips also finds
  documents by what is written in them, quoting the sentence that matched. Searching is
  accent-insensitive in both directions: "pestera" finds "peșteră", and the result is quoted back
  spelled the way its author wrote it. Words are stemmed in the language a document is written in,
  Romanian and English out of the box and any language PostgreSQL has a stemmer for by adding one
  row. The language is worked out from the document's own text when it is read — and left unset
  rather than guessed when the text does not say clearly — and anyone who may edit the document
  can correct it, which re-indexes it on the spot.
  You only ever find documents you are allowed to read, and replaced versions of a document
  are searchable only by the people who could replace it — a paragraph removed in a new version
  does not stay findable in the old one.
- **Read a document without downloading it** — a document has its own page, saying what it is,
  which version is current, where it is filed and how far reading its text has got, and showing
  the document itself: PDFs a page at a time as pictures drawn by the server, photos, plain text
  and Markdown, and audio and video played in the browser. A search hit opens that page at the
  page the phrase was found on. Nothing is transcoded and no scanned page is invented: where a
  format cannot be shown, the codec is not one your browser plays, or the file simply holds no
  text, the page says which of those it is and offers the download — you are never left looking
  at a blank panel wondering. It works on a phone, and someone allowed to read a protected cave's
  report can read it there without ever being able to download the file.
- **Cabinets** — a filing tree documents live in ("Club archive / Bulletins / 1987"), and the
  unit permissions are granted on: one rule can hand a committee the whole archive instead of
  one rule per document. A document can sit on several shelves at once, so there is no
  move-versus-copy question and nothing is orphaned by belonging in two places. Filing a
  document moves who can read it, and the app says so where the control is.
- **One model for everything on the map** — caves, entrances, centerlines and surface
  features share a single feature model, so any of them can contain another (a karst area
  holding caves, a cave holding its entrances) and be linked to another by a named
  relation. Containment drives breadcrumbs and inherits location protection downwards.
- **Vector import/export** — GPX, KML, Shapefile, GeoJSON, WKT/CSV.
- **3D survey models** — Therion `.lox` / Survex `.3d` via CaveView.js, plus cave
  centerlines projected on the map.
- **Trips, tags, saved & shareable map views**, and **multi-window** pop-out panels.
- **Share links** — hand out a revocable link to one feature and what it contains, either
  public or sign-in-only. A share never reveals a protected location.
- **Works on a phone** — the map workspace adapts to touch, including full geometry editing by
  finger, and the app installs to a home screen.
- **Accounts & permissions** — editable permission groups instead of fixed roles: named
  rulesets of allow/deny rules per resource and scope (everything, own content, a caving
  group's content, a feature subtree, a named feature set, a cabinet and everything filed
  below it, one object), per-object grants
  with the same reach, and an explainer that answers "why can this person see that?".
  Two-factor sign-in (authenticator app, emailed code or texted code), optional external
  login (Google/GitHub/OIDC), and location protection for sensitive caves — which keeps a
  protected cave's documents readable while withholding the fact that they point at *that*
  cave (an administrator can switch that disclosure on), and never hands out a photo whose
  own capture point would place the cave.
- **Email and SMS that an operator controls** — any SMTP server, any SMS gateway that speaks
  HTTP, and the wording of every message editable per language from the admin pages. Configure
  nothing and the app still runs: links and codes go to the server log instead.
- **English and Romanian** throughout.

## Quick start

```bash
git clone https://github.com/apgeo/silexgis.git
cd silexgis/deploy
cp .env.example .env          # set the DB password, admin account and public URL
docker compose up -d          # → http://localhost:8080
```

See **[docs/INSTALL.md](docs/INSTALL.md)** for HTTPS, backups, upgrades, external login
providers, and the non-Docker (Linux/Windows) install path.

## Repository layout

```
server/   ASP.NET Core API (.NET solution: Api / Domain / Infrastructure + tests)
client/   React + TypeScript SPA (Vite, Ant Design, OpenLayers)
deploy/   Docker Compose, TLS overlay, reverse-proxy configs, backup/restore scripts
docs/     Installation and operations documentation
```

## Development

```bash
docker compose -f deploy/docker-compose.dev.yml up -d db   # PostGIS only
dotnet run --project server/src/SilexGis.Api                # API on :5080
cd client && npm ci && npm run dev                          # Vite dev server
```

## License

AGPL-3.0-or-later — see [LICENSE](LICENSE). Third-party bundled components are listed in
[NOTICE](NOTICE).
