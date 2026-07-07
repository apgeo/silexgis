# SilexGIS

Web application for storage, viewing and editing of cave / karst topographic, geographic
and other associated data.

**v3 (2026, in development):** full rebuild on a modern stack —
ASP.NET Core minimal API (.NET, LTS) · PostgreSQL/PostGIS · EF Core + NetTopologySuite ·
React + TypeScript · Ant Design · OpenLayers · Docker-first deployment.

Previous versions:
- **v1** (PHP/MySQL/OpenLayers 3) — live at [speosilex.ro/silexgis](https://speosilex.ro/silexgis/en/index.php)
- **v2** (2022, React/Laravel, partial) — archived on the [`v2-archive`](../../tree/v2-archive) branch

## Repository layout

```
server/   ASP.NET Core API (.NET solution)
client/   React SPA (added in a later phase)
deploy/   Docker Compose, reverse-proxy configs, scripts (added in a later phase)
```

## License

AGPL-3.0-or-later — see [LICENSE](LICENSE).
