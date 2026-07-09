# Installing SilexGIS

SilexGIS runs anywhere Docker runs, and can also be installed without Docker. Everything is
configured through environment variables with sensible defaults; the only mandatory settings
are a database password, an admin account, and the public URL.

- [Quick start (Docker)](#quick-start-docker)
- [Enabling HTTPS](#enabling-https)
- [Backups](#backups)
- [Upgrades](#upgrades)
- [External login providers](#external-login-providers)
- [Non-Docker install](#non-docker-install)
- [Configuration reference](#configuration-reference)

## Requirements

- **Docker path:** Docker Engine with the Compose plugin (`docker compose`). 2 GB RAM is
  comfortable for a small community.
- **Non-Docker path:** PostgreSQL 15+ with the PostGIS 3.3+ and `unaccent` extensions, the
  .NET runtime (current LTS), and any static web server (nginx, Caddy, Apache, IIS).

No system GDAL is required on any path — vector import/export and raster conversion run
in-process.

## Quick start (Docker)

```bash
git clone https://github.com/apgeo/silexgis.git
cd silexgis/deploy
cp .env.example .env          # then edit .env (see below)
docker compose up -d          # → http://localhost:8080
```

Edit `.env` before first start and set at least:

| Variable | Meaning |
|---|---|
| `SILEXGIS_DB_PASSWORD` | database password (internal network only, but pick a strong one) |
| `SILEXGIS_ADMIN_EMAIL` / `SILEXGIS_ADMIN_PASSWORD` | the first administrator, created on first run |
| `SILEXGIS_PUBLIC_URL` | the URL users reach the app on (must match exactly — sign-in redirects derive from it) |

The first start creates the database schema, seeds taxonomies and default map layers, and
creates the admin account. Sign in at `SILEXGIS_PUBLIC_URL` with the admin credentials.

To load a small demo dataset (2 caves, entrances, features, a geofile and a raster):

```bash
docker compose exec api dotnet SilexGis.Api.dll seed-demo
```

## Enabling HTTPS

TLS is not on by default. Two easy options:

**Bundled Caddy (automatic Let's Encrypt).** Point a domain's DNS at the host, open ports 80
and 443, then set all three in `.env` (each is required by this overlay):

```
SILEXGIS_DOMAIN=caves.example.org
SILEXGIS_PUBLIC_URL=https://caves.example.org
SILEXGIS_TLS_EMAIL=admin@example.org
```

and bring the stack up with the TLS overlay:

```bash
docker compose -f docker-compose.yml -f docker-compose.tls.yml up -d
```

Caddy obtains and renews the certificate automatically.

**Your own proxy.** Run any reverse proxy (Caddy, nginx, Traefik) in front of the `web`
service, terminating TLS and forwarding `X-Forwarded-Proto: https`. Set `SILEXGIS_PUBLIC_URL`
to the `https://` address.

## Backups

`deploy/scripts/backup.sh` dumps the database and the uploaded-files volume:

```bash
cd deploy
sh scripts/backup.sh                 # → ./backups/<timestamp>/{db.sql.gz,files.tar.gz}
```

Restore into a stopped stack:

```bash
docker compose stop api
sh scripts/restore.sh ./backups/<timestamp>
docker compose up -d
```

A cron example that keeps nightly backups:

```
15 3 * * *  cd /opt/silexgis/deploy && sh scripts/backup.sh >> /var/log/silexgis-backup.log 2>&1
```

## Upgrades

```bash
cd silexgis
git pull
cd deploy
docker compose build --pull
docker compose up -d
```

Database migrations run automatically on start. Take a backup first; release notes call out
any manual steps.

## External login providers

Sign-in with Google, GitHub, or any OpenID Connect provider is optional and off by default —
the login page shows only the password form until you configure a provider. Add one block per
provider to `.env` (they pass straight through to the API):

```
SILEXGIS__Auth__ExternalProviders__0__Name=google
SILEXGIS__Auth__ExternalProviders__0__Type=google
SILEXGIS__Auth__ExternalProviders__0__DisplayName=Google
SILEXGIS__Auth__ExternalProviders__0__ClientId=...
SILEXGIS__Auth__ExternalProviders__0__ClientSecret=...
```

Register this redirect URI with the provider (using the `Name` value):
`<SILEXGIS_PUBLIC_URL>/api/v1/signin-<name>`. Types are `google`, `github`, or `oidc`
(generic — also set `__Authority`). External identities link into local accounts; a new local
account is created on first sign-in only when the provider block sets `__AllowCreate=true`
and the email is verified by the provider. To hide the password form entirely once a provider
is configured, set `SILEXGIS__Auth__ExternalOnly=true`.

See `deploy/.env.example` for GitHub and generic-OIDC examples.

## Non-Docker install

1. **Database.** Create a PostgreSQL database and enable extensions:
   ```sql
   CREATE DATABASE silexgis;
   \c silexgis
   CREATE EXTENSION postgis;
   CREATE EXTENSION unaccent;
   ```
2. **API.** Publish and run it:
   ```bash
   dotnet publish server/src/SilexGis.Api -c Release -o /opt/silexgis/api
   ```
   Configure via environment variables (or `appsettings.Production.json`). On Linux, use the
   provided systemd unit (`deploy/systemd/silexgis-api.service`) — it documents the service
   user, data directories, and an `EnvironmentFile` for secrets. On Windows, run it as a
   Windows Service (e.g. with `sc.exe create`) or under IIS with the ASP.NET Core Module,
   pointing at `SilexGis.Api.dll`.
3. **Client.** Build the SPA and serve `client/dist/` with your web server:
   ```bash
   cd client && npm ci && npm run build
   ```
   Use `deploy/nginx/silexgis.conf` as a template — it serves the static files and proxies
   `/api`, `/connect`, `/health`, `/openapi` and `/.well-known` to the API.
4. **Admin.** If you did not set `SILEXGIS__Admin__Email/Password`, create the first admin
   interactively:
   ```bash
   dotnet /opt/silexgis/api/SilexGis.Api.dll bootstrap-admin
   ```

The same code runs on Windows and Linux; paths, casing and line endings are handled
cross-platform.

## Configuration reference

All settings bind from `SILEXGIS__{Section}__{Key}` environment variables. The commented
`server/src/SilexGis.Api/appsettings.json` is the authoritative list. Common keys:

| Variable | Default | Meaning |
|---|---|---|
| `SILEXGIS__Db__ConnectionString` | — | PostgreSQL connection string |
| `SILEXGIS__Db__AutoMigrate` | `true` | run migrations on start |
| `SILEXGIS__PublicUrl` | `http://localhost:8080` | public URL (sign-in redirects derive from it) |
| `SILEXGIS__Admin__Email` / `__Password` | — | first-run administrator |
| `SILEXGIS__Auth__OpenRegistration` | `false` | allow self-registration |
| `SILEXGIS__Auth__ExternalOnly` | `false` | hide the password form when providers exist |
| `SILEXGIS__Access__AllowAnonymousRead` | `false` | let anonymous visitors read public content |
| `SILEXGIS__Files__Root` | `data/files` | uploaded-files directory |
| `SILEXGIS__Keys__Path` | `data/keys` | data-protection keys (must persist across restarts) |

Secrets belong only in the environment / `.env`, never in the repository.
