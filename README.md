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
  documents, and georeferenced raster (COG) map overlays.
- **Vector import/export** — GPX, KML, Shapefile, GeoJSON, WKT/CSV.
- **3D survey models** — Therion `.lox` / Survex `.3d` via CaveView.js, plus cave
  centerlines projected on the map.
- **Trips, tags, saved & shareable map views**, and **multi-window** pop-out panels.
- **Works on a phone** — the map workspace adapts to touch, including full geometry editing by
  finger, and the app installs to a home screen.
- **Accounts & permissions** — per-user/team/object access control, two-factor sign-in
  (authenticator app, emailed code or texted code), optional external login (Google/GitHub/OIDC),
  and location protection for sensitive caves.
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
