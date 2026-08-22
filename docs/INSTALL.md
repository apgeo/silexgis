# Installing SilexGIS

SilexGIS runs anywhere Docker runs, and can also be installed without Docker. Everything is
configured through environment variables with sensible defaults; the only mandatory settings
are a database password, an admin account, and the public URL.

- [Quick start (Docker)](#quick-start-docker)
- [Enabling HTTPS](#enabling-https)
- [Encryption at rest](#encryption-at-rest)
- [Showing Word and Excel files](#showing-word-and-excel-files)
- [Terrain (optional)](#terrain-optional)
- [Backups](#backups)
- [Upgrades](#upgrades)
- [External login providers](#external-login-providers)
- [Non-Docker install](#non-docker-install)
- [Configuration reference](#configuration-reference)

## Requirements

- **Docker path:** Docker Engine with the Compose plugin (`docker compose`). 2 GB RAM is
  comfortable for a small community.
- **Non-Docker path:** PostgreSQL 15+ with the PostGIS 3.3+, `unaccent` and `ltree` extensions
  (the last two ship with PostgreSQL itself), the .NET runtime (current LTS), and any static web
  server (nginx, Caddy, Apache, IIS).

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

To load a small demo dataset (2 caves, entrances, features, a geofile and a raster, plus a
two-shelf archive holding a survey report and a link joining that report to the cave it
describes):

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

## Encryption at rest

**SilexGIS does not encrypt anything it stores.** Uploaded files, the thumbnails and page
pictures derived from them, the text read out of documents, and every database row are written
in the clear. Encrypting them where they sit is the operator's job, and this section says how to
do it and what it buys you.

That is a deliberate position, not an omission, and it is worth understanding before you decide
whether this installation is a suitable home for the data you are about to put in it.

### Why the application does not do it

Encrypting inside the application would have to keep its key on the same machine as the bytes it
protects — in practice in a directory beside them. Anyone able to read the files could read the
key, so it would defend against nothing that a stolen disk does not already defend against, while
adding a permanent way to lose the whole archive: lose that key and every stored file is gone,
with no recovery and no partial answer. It would also cost real things that work today — files
are handed to the imaging and mapping libraries by path, and downloads are streamed straight off
disk with resumable byte ranges — and it would still leave the database, the derived pictures and
the search text in the clear unless each of those was solved separately.

Encryption at the volume or disk layer, by contrast, covers all of it at once, is a solved and
audited problem on every operating system, and puts key custody where the person who owns the
machine can actually manage it.

### What to do instead

**1. Encrypt the storage the volumes live on.** All application state is in three Docker volumes
(`silexgis-db`, `silexgis-files`, `silexgis-keys`), which on a default Linux install live under
`/var/lib/docker/volumes`. Encrypting the filesystem or block device that holds that path covers
the database, the uploads and the keys in one step:

- **Bare metal / VPS you install yourself:** put the filesystem on a LUKS volume
  (`cryptsetup luksFormat`), or install the operating system with full-disk encryption
  selected. The passphrase or key file must be supplied at boot and must not live on the
  encrypted device.
- **Cloud provider:** select the provider's encrypted-volume option for the disk that carries
  `/var/lib/docker` (or the whole instance). This is usually a checkbox at volume-creation time
  and cannot be turned on afterwards without moving the data.
- **Storing files elsewhere:** if `SILEXGIS__Files__Root` points at a mount of your own rather
  than the bundled volume, that mount is the thing to encrypt, and the database volume still
  needs covering separately.

**2. Encrypt the backups.** `deploy/scripts/backup.sh` writes plain `db.sql.gz` and
`files.tar.gz`. A backup is the copy most likely to end up somewhere you do not control, so
encrypt it before it leaves the host and keep the passphrase somewhere other than the backup
medium:

```bash
cd deploy
sh scripts/backup.sh
age -r age1yourrecipientkey... -o backups/<timestamp>/db.sql.gz.age    backups/<timestamp>/db.sql.gz
age -r age1yourrecipientkey... -o backups/<timestamp>/files.tar.gz.age backups/<timestamp>/files.tar.gz
shred -u backups/<timestamp>/db.sql.gz backups/<timestamp>/files.tar.gz
```

`gpg --encrypt --recipient …` works the same way. To restore, decrypt back to the original two
file names first — `scripts/restore.sh` expects them.

**3. Turn on HTTPS.** Encryption at rest does nothing for data crossing the network, and TLS is
off by default here. See [Enabling HTTPS](#enabling-https).

**4. Keep host access short.** Volume encryption protects a disk that is switched off. While the
stack is running the volumes are mounted and readable by anyone with root on the host or access
to the Docker socket, so the list of people holding either is the real access-control boundary.

### What this does and does not protect

Disk or volume encryption protects data when the machine is off or the storage has left your
hands: a stolen or seized server, a decommissioned or resold drive, a disk image copied by
someone at the hosting provider, an unencrypted backup found on a shelf.

It protects against none of the following, and nothing at the storage layer can:

- Anyone with root on a running host, or access to the Docker socket. The volumes are mounted
  and in the clear while the service is up.
- A compromise of the application itself, which by definition can read what it stores.
- A SilexGIS account holding more rights than it should. Access control inside the application
  is a separate matter, managed through permission groups and access entries.
- Anyone who is legitimately given a copy of a file.

### Two things stored in the clear that may surprise you

- **The data-protection keys** in `/data/keys` are stored unencrypted. They protect sign-in
  cookies, file-download links and confirmation e-mails — all short-lived — so losing them logs
  everyone out and invalidates outstanding download links, and nothing more. They are
  deliberately excluded from backups for that reason. Anyone who can read them can forge a
  download link or a session, so the directory deserves the same care as the files themselves.
- **Secrets saved through the admin settings pages** — the outgoing-mail password and the SMS
  gateway's authorisation header — are stored as plain text in the database. The interface never
  sends them back to a browser, but they are readable in the database and therefore in any
  unencrypted database dump. Configuring them as `SILEXGIS__Mail__Password` /
  `SILEXGIS__Sms__AuthHeader` environment variables keeps them out of the database instead — but
  only for as long as nobody saves that section from the admin page, because a saved section is
  stored whole and from then on takes precedence over the environment. Pick one place for these
  and stay there.

## Showing Word and Excel files

Word, Excel, PowerPoint and OpenDocument files are stored, searched and downloaded like
everything else, but the browser cannot show them page by page the way it shows a PDF. That
is not a gap in the reader: those formats have no pages until something decides a paper size
and a font, which takes a whole office suite.

SilexGIS can use one if you give it one. Start the converter alongside the stack and every
such upload gets a PDF copy stored next to it — the original file is never altered — and the
document then reads page by page in the browser, with real page numbers.

```bash
cd deploy
# in .env:
#   SILEXGIS__Conversion__Enabled=true
#   SILEXGIS__Conversion__Url=http://convert:3000
docker compose -f docker-compose.yml -f docker-compose.convert.yml up -d
```

The converter is [Gotenberg](https://gotenberg.dev/) (MIT-licensed) by default; set
`SILEXGIS_CONVERT_IMAGE` to pin a different tag. It publishes no port and is reachable only
from inside the stack, which is deliberate — it is a headless office suite that opens
whatever it is handed.

**It is entirely optional.** Without it nothing breaks: those documents are still stored,
still found by content search, still downloadable, and the document page says plainly that
this installation cannot lay them out rather than showing an empty panel or claiming the file
is damaged. Whether a document can be *found* never depends on the converter — the words are
read out of the uploaded file either way, by a reader every installation has.

What the converter does change is how precisely a match can be pointed at. Where a readable
copy exists, a content hit is read off that copy, so the page a search result names is the
page the browser opens — including for Word and OpenDocument files, which otherwise have no
page numbers at all. The uploaded file is still searched as well, so nothing a copy leaves out
(a deck's speaker notes, a hidden worksheet, a cell clipped at the column boundary) becomes
unfindable the day you turn the converter on. Without a converter, a match in a spreadsheet or
a presentation still says which sheet or slide it is in, and a match in a Word file opens the
document at its beginning.

Notes worth knowing before you turn it on:

- **Conversion happens at upload time**, in the background. Files uploaded *before* you
  enabled it are not converted by that alone — start the sweep once after enabling it
  (`POST /api/v1/jobs/document-conversion-backfill`, which needs the job-execution right) and
  every document that could have a readable copy and has none gets one. The sweep is safe to
  run as often as you like, and does nothing at all while no converter is configured.
- It costs disk. A converted copy is a second file, typically of similar size to the original.
- A document the converter cannot open is recorded as a conversion failure and stays
  downloadable. The upload itself is never touched by any of this.
- If the converter is restarting or busy when a document arrives, that document is recorded as
  *not converted yet* rather than as one this installation cannot lay out — the two sentences
  are different and the page says the right one. Nothing retries on its own, deliberately, so
  that the failure is visible in the jobs list; the same sweep above picks the document up once
  the converter is answering again.
- Turning it off again: stop the `convert` service **and** remove
  `SILEXGIS__Conversion__Enabled` from `.env`, or uploads keep queuing conversions for a
  service that is no longer there.

## Terrain (optional)

By default the 3D view draws the globe as a smooth mathematical sphere. That needs nothing
installed, nothing downloaded and no elevation server — it is what `docker compose up` gives you,
and if real relief is not wanted, **nothing in this section applies and nothing changes.**

To put the caves under real hillsides, you bake elevation data into a tile pyramid once and serve
it as static files. It is entirely local afterwards: no account, no key, and no request leaves
your installation while somebody is looking at a cave.

You need Docker (for the pre-baker) and Node 18+ (for the script). Budget roughly **45 MB of
download and 40 MB of tiles per 1°×1° cell**, and about **6 minutes** of one machine's time per
cell at full detail. Romania is about 30 cells.

### 1. Download the elevation data

```bash
node deploy/terrain.mjs fetch --bbox 22,46,23,47 --out ./dem
```

The box is `west,south,east,north` in degrees. This pulls Copernicus DEM GLO-30 from the AWS
open-data bucket — free for any use including commercial, attribution required, no account and no
key. Cells that are entirely sea are simply not published, and are reported and skipped.

Each cell is written under a temporary name and moved into place only once all of it has arrived
and its length has been checked, so re-running skips the cells that are finished and re-fetches
only the one an interruption caught in the middle. Nothing that stops a run part way — a closed
terminal, a dropped link, a disk that filled — can leave behind a fragment that a later run
mistakes for a finished cell.

### 2. Bake the pyramid

```bash
node deploy/terrain.mjs bake --in ./dem --out /srv/silexgis/terrain
```

This runs `gaia3d/mago-3d-terrainer` (MPL-2.0; the image carries its own Java) and writes a static
tile pyramid. Add `--max-depth 9` for a quick first run — every extra level roughly quadruples
both the tile count and the time. The pre-baker asks for a good deal of memory; give Docker 8 GB
or more before baking a large area.

When it finishes it verifies what it produced and **prints the exact `.env` lines for it**. Paste
those into `deploy/.env` rather than writing them by hand — the vertical datum in particular is
not a thing to guess at (see below).

You can re-run the verification at any time, and it is worth re-running on the machine that will
actually serve the files, after they have been copied there:

```bash
node deploy/terrain.mjs check --dir /srv/silexgis/terrain
```

It reads every tile, and refuses a pyramid that is empty, cut short, or missing whole levels it
says it holds — the shapes a bake that ran out of disk or a copy that was interrupted leave
behind. None of those announces itself while somebody is looking at it: the missing tiles answer
`404` and the ground is quietly drawn from the coarser level above them, which is the wrong
heights presented as the right ones.

### 3. Serve it

```bash
docker compose -f docker-compose.yml -f docker-compose.terrain.yml up -d
```

The overlay mounts your terrain directory read-only into the web service, which serves it at
`/terrain/`. Nothing else about the stack changes: no new service, no new image, no new port.

**Not using Docker?** Uncomment the `location /terrain/` block in `deploy/nginx/silexgis.conf`,
point its `alias` at the baked directory, and set the same `SILEXGIS__Terrain__*` variables for
the API.

### The one serving rule

**Whatever your web server declares a tile's encoding to be, it must describe the bytes on disk,
exactly.**

This is worth stating on its own because getting it wrong has practically no symptom. Terrain
tiles are a binary mesh format, and a browser handed one whose declared encoding is wrong carries
on as though nothing had happened: every request answers `200 OK`, no request fails, and the only
real sign is a globe with no ground on it. Measured on this stack, serving compressed tiles
without saying so raised **no error and no warning** — two ordinary log lines, which anyone whose
console is filtered to errors, as most are, never sees — while every sampled height came back
empty.

The shipped configuration is already correct — the pre-baker writes uncompressed tiles and the
`/terrain/` block declares no encoding — so this only matters if you change something.

If you want the tiles compressed (worth roughly 60% over the wire), **gzip each tile into a
sibling `.gz` file and keep the original**:

```bash
find /srv/silexgis/terrain -name '*.terrain' -exec gzip -k {} \;
```

`gzip_static` then serves whichever of the two the browser can accept and labels it itself, which
is the only way to get the pairing right that cannot also get it wrong. **Never gzip a tile in
place**, and never add a `Content-Encoding` header by hand.

If the tiles cannot be used, the 3D view says so on screen and falls back to the smooth globe
rather than showing an empty one. `node deploy/terrain.mjs check` reports the same thing from the
files themselves.

### Re-baking later

Elevation tiles are cached hard by browsers, for a week, because they are large and they normally
never change. Re-baking is the case where they do — extending coverage, or switching the vertical
datum — and it has to be able to reach a viewer who was looking at the old ones yesterday.

**`bake` handles this for you: it stamps the pyramid with a version derived from the tiles
themselves, and that version is on the end of every tile URL a browser requests.** Change any
tile and every address changes, so nothing stale can be reused; re-bake tiles that come out
identical and the caches stay warm. `check` prints the version, so you can confirm it moved.

Two things to keep in mind:

- **Bake through the script, not by calling the pre-baker directly.** The pre-baker writes the
  same constant version into every pyramid it has ever produced. With that, a browser that saw
  any earlier pyramid at your address keeps drawing it from its own cache — not one request
  reaches your server, nothing errors, and if what you changed was the datum, every cave is now
  forty metres off its hillside. `check` warns if it finds an unstamped pyramid.
- **If you serve the tiles yourself, do not cache `layer.json` the way you cache the tiles.** It
  is where the version comes from, so a browser that does not re-read it keeps building the old
  addresses. The shipped configuration sets `no-cache` on it and a week on everything else.

### Which heights your tiles hold

Cave survey altitudes are heights above sea level. A globe draws every terrain tile as a height
above the WGS84 ellipsoid, whatever the numbers in it actually meant — so the same surveyed
altitude needs a different correction depending on the elevation model, and getting it wrong
moves **every cave about forty metres** off its hillside with nothing on screen to say so.

That is why the datum is declared per source rather than assumed:

| `SILEXGIS__Terrain__HeightDatum` | Use it when | Correction applied |
|---|---|---|
| `Orthometric` (default) | The tiles hold heights above sea level. This is what an unconverted elevation model holds, Copernicus included, and what most hosted terrain services publish. | none — the tiles and the survey already agree |
| `Ellipsoidal` | The tiles were converted when they were baked (`--datum ellipsoidal`). | surveyed altitudes are raised by `SILEXGIS__Terrain__GeoidHeightM` |

Baking with the default leaves the heights as they came, which is the simpler and safer of the
two: terrain and survey are then consistent with each other without any correction at all. Baking
with `--datum ellipsoidal` is geodetically truer and then requires `SILEXGIS__Terrain__GeoidHeightM`
to be set to the local geoid undulation — measured values over Romanian karst run **+39 m to
+45 m**, about **+43** in the Apuseni. Leaving it at zero with an ellipsoidal bake puts every cave
that far below its hillside; the API says so in its log at startup, and so does the bake command.

### Attribution

Copernicus GLO-30 requires a credit, and the pre-baker writes a placeholder into the pyramid's own
metadata rather than a real one. `bake` replaces it, and also prints the credit as
`SILEXGIS__Terrain__Attribution`, which is what the 3D view shows on screen. Keep it.

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

Both outputs are unencrypted. If a copy leaves the host — offsite, object storage, a USB disk —
encrypt it first; see [Encryption at rest](#encryption-at-rest).

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

### Upgrading across the document text-reading release

An installation that already holds documents queues one background sweep the first time it
starts on the new schema, and that sweep queues one reading per stored file whose format
carries text. Nothing you uploaded is modified — a reading only writes the text it found beside
the file it read — but on a large archive the background worker will be busy for a while after
the upgrade, and until a document has been reached its panel says its text has not been read
yet. Nothing else waits on it: the site is usable throughout.

An administrator can start the sweep again at any time (`POST /api/v1/jobs/text-extraction-backfill`,
which needs the job-execution right). It is safe to run as often as you like — a file that has
already been read by the current reader costs one query and nothing else — and it is how you
pick up an improved reader after a later upgrade.

Two things about the schema step itself are worth knowing before you take it. It arrives as a
single migration on top of the one every earlier release shipped, so an installation that has
been running a **released** version upgrades in place: every stored file becomes a version of a
document, keeping its own identifier and the version chain it was already in, and the accounts
that could upload before can still upload afterwards. Nothing is deleted and nothing is moved
on disk.

An installation that has been tracking an **unreleased development branch** of this feature is
the exception. The development history was rewritten into that single step, so its identifier
is one such a database has never seen while the tables it creates are already there, and
starting the new build against it will fail — repeatedly, because migration runs on start.
There is no upgrade path across that rewrite: recreate the database (or restore the backup you
took before switching to the development branch) and load your data again. A released
installation is never in this position.

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
- **Code lifetime and the wait between codes.** The wait is counted **per account**, in the
  database, and nobody can reset it by deleting their number and starting again. A wrong code
  counts against the account's ordinary lockout, so a six-digit code cannot be sat and guessed at.
  The **Send a test message** buttons on the mail and SMS pages carry the same per-account wait, so
  a test cannot be looped into a bill.

A member's **telephone number is a sign-in credential**, not a profile field: it can only be changed
through the security page, by returning a code texted to the new number, and one number belongs to
one account. Whether other members can see it is still a profile setting.

## Non-Docker install

1. **Database.** Create a PostgreSQL database and enable extensions:
   ```sql
   CREATE DATABASE silexgis;
   \c silexgis
   CREATE EXTENSION postgis;
   CREATE EXTENSION unaccent;
   CREATE EXTENSION ltree;
   ```
   The application creates these itself on first start if the database user is allowed to, so
   this step is a convenience for installations where it is not. Document content search also
   creates three text-search configurations in the `public` schema on first start
   (`simple_unaccent`, `romanian_unaccent`, `english_unaccent`), which needs no special
   privilege beyond owning the database.
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
| `SILEXGIS__Notifications__RetentionDays` | `365` | how long a notification stays readable in the recipient's list before it is deleted — read or unread alike, and whether or not an email ever went out for it. Values of zero or less are ignored in favour of the default, so a mistyped setting cannot empty the list |
| `SILEXGIS__About__InstanceName` | `SilexGIS` | name used in the messages this installation sends |
| `SILEXGIS__Auth__DefaultPermissionGroups` | *(empty)* | comma-separated permission-group slugs (e.g. `editors`) every new account joins at registration or first external sign-in |
| `SILEXGIS__Map__CenterlineDetailZoom` | `18` | zoom at which cave centerlines switch from passage outlines to full survey detail |
| `SILEXGIS__Map__CenterlineMaxPaths` | `25000` | line budget per centerline request; over it, outlines are served instead |
| `SILEXGIS__Terrain__Url` | *(empty)* | where the baked elevation tiles are served from, e.g. `/terrain/`. Empty means the 3D view draws a smooth globe, which needs nothing installed. See [Terrain](#terrain-optional) |
| `SILEXGIS__Terrain__HeightDatum` | `Orthometric` | what the tile heights are measured from: `Orthometric` (above sea level) or `Ellipsoidal` (converted when baked). Wrong here puts every cave about 40 m off its hillside |
| `SILEXGIS__Terrain__GeoidHeightM` | `0` | the local geoid undulation in metres, used **only** with `Ellipsoidal`. +39 to +45 over Romanian karst |
| `SILEXGIS__Terrain__Attribution` | *(empty)* | credit the elevation data's licence requires; shown on the 3D scene |
| `SILEXGIS__Files__Root` | `data/files` | uploaded-files directory |
| `SILEXGIS__Files__MaxUploadBytes` | `536870912` (512 MB) | largest accepted upload. The request-body and multipart limits follow this value automatically; the reverse proxy in front has its own cap that must be at least as large (the bundled web service allows 1 GB) |
| `SILEXGIS__Keys__Path` | `data/keys` | data-protection keys (must persist across restarts) |
| `SILEXGIS__Conversion__Enabled` | `false` | use a document-conversion service so office documents can be read page by page. Needs the converter overlay running; without it those documents are still stored, searched and downloaded |
| `SILEXGIS__Conversion__Url` | — | where that service is, e.g. `http://convert:3000` |
| `SILEXGIS__Conversion__TimeoutSeconds` | `120` | how long one conversion may take before it is given up on |
| `SILEXGIS__AccessHistory__Retention` | `365.00:00:00` | how long the record of who downloaded which document is kept. `00:00:00` switches the record off entirely and the next sweep deletes everything already held |
| `SILEXGIS__AccessHistory__CollapseWindow` | `01:00:00` | within this window, the same person fetching the same file again is the same reading and adds no row |
| `SILEXGIS__AccessHistory__SweepInterval` | `1.00:00:00` | how often the pass that deletes expired access-history rows is queued. `00:00:00` turns the schedule off, and nothing is then deleted. Note the leading `1.` — a day is written `d.hh:mm:ss`, and `24:00:00` on its own means twenty-four **days** |
| `SILEXGIS__FeatureIntegrity__Interval` | `1.00:00:00` | how often a background pass re-checks the map data for internal inconsistencies; findings go to the log and the admin jobs list. `00:00:00` turns the schedule off |

Secrets belong only in the environment / `.env`, never in the repository.
