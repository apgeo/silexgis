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
- **Talk about a document where it lives** — every document page carries a discussion: who
  recognises the cave in an unlabelled 1987 photograph, which survey superseded which. Replies go
  one level deep, so a thread stays readable; the person who wrote a remark can correct it, and
  they or an administrator can take it down. Whoever may read the document may join in — and
  nobody else can even tell the conversation is there, because a remark is only ever reachable
  through the document it sits on. Remarks are plain words, shown as the words that were typed.
- **Cabinets** — a filing tree documents live in ("Club archive / Bulletins / 1987"), and the
  unit permissions are granted on: one rule can hand a committee the whole archive instead of
  one rule per document. A document can sit on several shelves at once, so there is no
  move-versus-copy question and nothing is orphaned by belonging in two places. Filing a
  document moves who can read it, and the app says so where the control is.
- **One model for everything on the map** — caves, entrances, centerlines and surface
  features share a single feature model, so any of them can contain another (a karst area
  holding caves, a cave holding its entrances) and be linked to another by a named
  relation. Containment drives breadcrumbs and inherits location protection downwards.
- **Links between anything, not just between features** — a cave and the report that describes
  it, a trip and the photographs it produced, two records that turned out to be the same hole:
  a link names two or more things of any kind — features, documents, trips, cavers, clubs,
  cabinets, 3D models, saved views — and says how they are related. The wording comes from a
  list ("Contains", "Documented by", "Duplicate of", …) that an administrator can extend with
  whatever this archive actually says. A relation that reads one way reads correctly from both
  ends, so a single link says "Contains" on one page and "Contained in" on the other. A link
  can point at a part rather than the whole — a page or a run of pages of a document, a moment
  or a stretch of a recording; other kinds of part (a survey station, a passage of text) are
  recorded and shown, but choosing one needs a viewer that can select it, and those arrive with
  their viewers. A link can also name a spot on the map that had no record yet, marking the
  point as the link is written. Every link has a short address of its own to paste into a
  message or a report, and it shows each reader only the ends they are allowed to see: a link
  is never the thing that discloses a protected cave.
- **Vector import/export** — GPX, KML, KMZ, Shapefile, GeoJSON, WKT, and a spreadsheet of
  positions in CSV. A file drawn on the map is only half of it: a review screen turns the
  waypoints in it into caves, entrances and surface features, proposing what each one is from
  the club's own naming habits — a waypoint called `P. Ursilor`, `Aven`, `Izbuc` or `Doline` is
  recognised, and the term is taken back out of the name. Nothing is created until you confirm,
  the review survives closing the tab, each candidate says whether there is already something
  within fifty metres, and the whole confirmation comes back out again in one press if the
  mapping turns out to have been wrong. The rules are yours to edit, and to send to another club
  as a file.
- **Photographs become the places they show** — drop a trip's worth of pictures and the ones taken
  at the same hole arrive as one candidate with a gallery, not as forty points to reject one at a
  time. Each place says how it was placed and how much that is worth: the fix the camera recorded,
  a position worked out by matching the picture's clock against a track you walked (with a
  camera-clock offset, because a camera left on the wrong zone is out by hours while looking
  perfectly plausible), or one you gave it by dragging it onto the map. It offers what is already
  in the registry nearby, so filing a picture on the cave it belongs to is one press. Nothing is
  created until you confirm, and the whole confirmation — including the pictures it hung on caves
  that were already there — comes back out again in one press. A picture that recorded which way
  the camera was facing draws that on the map, which is what turns "somewhere on this slope" into
  a hole you can walk back to. Nothing is ever written back into the file itself.
- **3D survey models** — Therion `.lox` / Survex `.3d` via CaveView.js, plus cave
  centerlines projected on the map.
- **3D view** — the configured base layers draped on a globe, on a page that is downloaded
  only when it is opened. Needs WebGL 2; a browser without it gets an explanation rather
  than a dead canvas. No vendor terrain, imagery or geocoding service is contacted and the
  3D engine is served by your own installation; the basemap is whichever base layers you
  configured, so an air-gapped install needs one it can reach.
- **Real relief, if you want it** — that globe is a smooth sphere out of the box, needing no
  elevation server and nothing downloaded. An operator who wants the caves under actual
  hillsides bakes free elevation data into a tile pyramid in one documented step and serves it
  as static files from the same installation. Everyone who does not is unaffected.
- **Trips** — a trip is logged over the days it actually ran, with who was there, what came of it
  and how long was spent underground, and it can be sketched on a map: a point, a line or an area
  for where it happened. That sketch is shown exactly to everyone who may read the trip, including
  on the trip map, so the editor says so beside it — a sketch dropped on a protected entrance
  discloses that entrance, whatever protection the caves the trip names carry. A trip starts as a
  **draft**: you can write it up over several sittings without anybody being told, and publishing it
  is what announces it to the people who were there — and only to those of them who may actually
  open it. A draft is not a hidden trip, though: whoever the trip's visibility admits can read it
  from the moment it exists. A trip's page also records **what it did to what it names** — the
  areas worked in, the passages surveyed, dug, photographed or discovered, the leads left for next
  time — each as a labelled field you fill in as the trip earns it, rather than a form of empty
  boxes to stare at. A work area shows what contains it, so two passages of the same name are told
  apart. What one person records, everybody who may maintain the thing it is about can correct.
- **Tags, saved & shareable map views**, and **multi-window** pop-out panels.
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
deploy/   Docker Compose, TLS and terrain overlays, reverse-proxy configs, backup/restore
          and terrain pre-bake scripts
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
