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

The first start creates the database schema, seeds taxonomies, default map layers and the
built-in permission groups, and creates the admin account as a member of **Full
Administrators**. Sign in at `SILEXGIS_PUBLIC_URL` with the admin credentials.

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
to the `https://` address. Allow request bodies at least as large as
`SILEXGIS__Files__MaxUploadBytes` (512 MB by default) — nginx's `client_max_body_size` and
IIS's `maxAllowedContentLength` both default well below that, and an upload refused at the
proxy fails with an error the application never sees and cannot explain.

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

## Browsing the database

The database container publishes no port — it is reachable only from inside the stack's Docker
network. To inspect it, add pgAdmin to that network instead of opening the database to the host:

```bash
cd deploy
# set SILEXGIS_PGADMIN_PASSWORD in .env first
docker compose -f docker-compose.yml -f docker-compose.pgadmin.yml up -d pgadmin
```

Open <http://127.0.0.1:5050> and sign in with `SILEXGIS_PGADMIN_EMAIL` (default
`admin@example.com`) and `SILEXGIS_PGADMIN_PASSWORD`. A server entry named **SilexGIS** is
already there; expanding it asks for the database password, which is `SILEXGIS_DB_PASSWORD`.
Tick *Save password* to be asked only once.

The web UI binds to `127.0.0.1` — it is not reachable from other machines, which is deliberate
for a tool that stores database credentials. Reach a remote installation's pgAdmin over an SSH
tunnel (`ssh -L 5050:127.0.0.1:5050 user@host`) rather than by changing the binding.

Stop it when you are done; it is not part of the running service:

```bash
docker compose -f docker-compose.yml -f docker-compose.pgadmin.yml stop pgadmin
```

### Desktop clients, during development only

A client on your own machine (DBeaver, psql, a desktop pgAdmin) needs the database to have a
host port, which the deployed stack deliberately does not give it.
`deploy/docker-compose.dbport.yml` adds one, bound to `127.0.0.1`:

```bash
cd deploy
docker compose -f docker-compose.yml -f docker-compose.dbport.yml up -d db
```

| Setting | Value |
|---|---|
| Host / Port | `localhost` / `5433` (`SILEXGIS_DB_HOST_PORT` to change it) |
| Database | `silexgis` |
| User | `silexgis` |
| Password | `SILEXGIS_DB_PASSWORD` from `.env` |

Applying or removing the overlay recreates the database container — the data is in a named
volume and survives, but open connections drop, so the API logs one connection error and
reconnects. Plain `docker compose up -d db` puts it back the way it was, without the port.

Do not use this overlay on a deployed installation. Keeping the database off the host is what
stops a single guessed password from reaching every row, entrance coordinates included; use
the pgAdmin overlay above there instead.

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

## Email and SMS

SilexGIS runs perfectly well with neither configured — confirmation links and sign-in codes are
written to the API log for you to read, which is the intended mode for a small installation. To
have them actually delivered, configure one or both. Everything below is also editable in the app
under **Messaging** (administrators only); what you save there replaces the values here.

**Email (SMTP).**

```env
SILEXGIS__Mail__Enabled=true
SILEXGIS__Mail__Host=smtp.example.org
SILEXGIS__Mail__Port=587
SILEXGIS__Mail__Security=Auto          # Auto | StartTls | SslOnConnect (465) | None
SILEXGIS__Mail__Username=silexgis@example.org
SILEXGIS__Mail__Password=...
SILEXGIS__Mail__FromAddress=silexgis@example.org
SILEXGIS__Mail__FromName=Cave Register
```

**SMS.** There is no vendor lock-in: you describe the HTTP request your gateway expects, and
`{to}`, `{text}` and `{from}` are substituted in (escaped for the content type, so message text
can contain anything).

```env
SILEXGIS__Sms__Enabled=true
SILEXGIS__Sms__Url=https://gateway.example.org/send
SILEXGIS__Sms__Method=POST
SILEXGIS__Sms__ContentType=application/x-www-form-urlencoded
SILEXGIS__Sms__BodyTemplate=To={to}&From={from}&Body={text}
SILEXGIS__Sms__AuthHeader=Basic <base64 of user:password>
SILEXGIS__Sms__From=+40712345678
```

For Twilio, point `Url` at
`https://api.twilio.com/2010-04-01/Accounts/<ACCOUNT_SID>/Messages.json` and set `AuthHeader` to
`Basic <base64 of ACCOUNT_SID:AUTH_TOKEN>`. For a JSON gateway, set
`ContentType=application/json` and a body such as
`{"to":"{to}","from":"{from}","message":"{text}"}`.

Use **Messaging → Send test** in the admin pages to confirm a channel works before anyone
depends on it; a failure is reported with the server's own error message.

**Message wording** is editable per language under **Message texts**. Leave a message alone and
it follows the product; rewrite it and your version is used until you reset it.

## Accounts and permissions

There are no fixed roles. What an account may do is decided by **permission groups** — named
rulesets of allow/deny rules, managed under **Administration → Permission groups** — plus a
per-object *Permissions* tab for one-off grants. The first start seeds a working set:

- **Full Administrators** — membership itself is the grant; the bootstrap admin starts here.
  The application refuses any change that would leave it without a member who can sign in.
- **Administrators** — runs the installation (users, settings pages, audit, jobs) but cannot
  edit permission groups, feature sets or installation secrets.
- **Editors** and **Reviewers** — create/edit content, and read-past-visibility, respectively.
- **All Users** — implicit for every account: the map-layer/taxonomy/tag catalogues, the
  caving-groups and people directories, saving map views, and creating a caving group.

A fresh account is a member of *All Users* only: it can sign in, browse what visibility
settings and share links admit, keep its own saved views and content, and take part in trips —
but it cannot create shared content until somebody adds it to a group that allows that (or
`SILEXGIS__Auth__DefaultPermissionGroups` names groups every new account should join, e.g.
`editors`). Every group, including the seeded ones except Full Administrators and All Users,
is editable — rename them, change their rules, or add your own.

## Sign-in security

Users choose their own second factor under *Settings → Security*: an authenticator app (scan the
QR code), an emailed code, or a texted code. Recovery codes are issued the first time any of them
is switched on and are always accepted, so nobody is locked out if a channel later becomes
unavailable.

As an administrator you decide what the installation permits, under **Messaging → Sign-in
policy**:

- **Require a confirmed address to sign in** — off by default. It has no effect until a mail
  server is configured, because otherwise nobody could ever confirm. Existing accounts are not
  affected by upgrading.
- **Which second factors are allowed.** Texted codes are **off by default**: a phone number can be
  moved to another SIM by persuading a mobile operator, which makes SMS the weakest of the three.
- **Code lifetime and the wait between codes.**

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
   Use `deploy/nginx/silexgis.conf` as a template — it serves the static files, proxies
   `/api`, `/connect`, `/health`, `/openapi` and `/.well-known` to the API, and sets a
   `client_max_body_size` above the default upload limit.
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
| `SILEXGIS__Mail__Enabled` / `__Host` / `__Port` | `false` / — / `587` | SMTP server; unset means messages go to the log |
| `SILEXGIS__Mail__Security` | `Auto` | `Auto`, `StartTls`, `SslOnConnect` (465) or `None` |
| `SILEXGIS__Mail__FromAddress` / `__FromName` | — / `SilexGIS` | sender of every message |
| `SILEXGIS__Sms__Enabled` / `__Url` | `false` / — | SMS gateway endpoint |
| `SILEXGIS__Sms__BodyTemplate` | `To={to}&From={from}&Body={text}` | request body; `{to}`, `{text}`, `{from}` are escaped for the content type |
| `SILEXGIS__Security__RequireConfirmedEmail` | `false` | refuse sign-in until confirmed (inert with no mail server) |
| `SILEXGIS__Security__SmsTwoFactorEnabled` | `false` | allow texted codes as a second factor |
| `SILEXGIS__Security__TwoFactorCodeLifetimeMinutes` | `5` | how long a delivered code stays valid |
| `SILEXGIS__Protection__RevealProtectedAssociations` | `false` | show a caller without exact-location rights that a document is attached to a position-protected cave. The document itself is served either way; only the pairing is affected, and switching this on never reveals a position — a photo carrying its own capture point stays unpaired regardless |
| `SILEXGIS__Notifications__PollSeconds` | `15` | how often queued notifications are sent; **0 switches sending off entirely, and queued messages keep accumulating** |
| `SILEXGIS__Notifications__DigestHourUtc` | `7` | the hour (UTC) at which daily summaries go out |
| `SILEXGIS__About__InstanceName` | `SilexGIS` | name used in the messages this installation sends |
| `SILEXGIS__Auth__DefaultPermissionGroups` | *(empty)* | comma-separated permission-group slugs (e.g. `editors`) every new account joins at registration or first external sign-in |
| `SILEXGIS__Map__CenterlineDetailZoom` | `18` | zoom at which cave centerlines switch from passage outlines to full survey detail |
| `SILEXGIS__Map__CenterlineMaxPaths` | `25000` | line budget per centerline request; over it, outlines are served instead |
| `SILEXGIS__Files__Root` | `data/files` | uploaded-files directory |
| `SILEXGIS__Files__MaxUploadBytes` | `536870912` (512 MB) | largest accepted upload. The request-body and multipart limits follow this value automatically; the reverse proxy in front has its own cap that must be at least as large (the bundled web service allows 1 GB) |
| `SILEXGIS__Keys__Path` | `data/keys` | data-protection keys (must persist across restarts) |
| `SILEXGIS__FeatureIntegrity__Interval` | `24:00:00` | how often a background pass re-checks the map data for internal inconsistencies; findings go to the log and the admin jobs list. `00:00:00` turns the schedule off |

Secrets belong only in the environment / `.env`, never in the repository.
