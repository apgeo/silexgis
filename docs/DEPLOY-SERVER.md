# Deploying SilexGIS on a server

This is the operator's runbook for putting SilexGIS on a machine of its own and keeping it
there: preparing the host, standing the stack up, updating it without losing data, and getting
it back after a bad update.

It complements [INSTALL.md](INSTALL.md) rather than replacing it. INSTALL.md is about the
application — its settings, its optional services, what each one does. This document is about
the *server*: the account the stack runs as, the firewall, the swap, the backup schedule, and
the update procedure. Where the two overlap, INSTALL.md is the authority on the application's
own configuration.

Everything here is done by four scripts under [`deploy/server/`](../deploy/server/). They are
idempotent, they are the same scripts used to build the reference installation, and running
them is what makes this reproducible rather than a list of commands somebody has to retype
correctly.

## What you need

- A machine running Ubuntu (tested on 26.04 LTS) with a public IP, reachable on port 22.
- Root access to it, once, to install your SSH key.
- 4 vCPU, 8 GB RAM and 40 GB of disk is enough for a small installation including the terrain
  bake worker. Images and build cache alone take about 15 GB.
- Nothing else on the machine. These scripts assume the host is theirs — in particular they
  reset the firewall and enable a swap file.

## Install, from a fresh machine

### 1. Prepare the host

Copy [`deploy/server/provision-host.sh`](../deploy/server/provision-host.sh) to the machine
and run it as root:

```bash
bash provision-host.sh
```

It installs Docker from Docker's own repository (the Ubuntu archive's engine does not come
with a Compose plugin new enough for the TLS overlay), creates an unprivileged `silexgis`
account that will own the checkout, copies root's authorised keys to it, adds a swap file,
turns on a firewall, fail2ban and automatic security updates, and caps container and journal
logs so a single disk cannot fill with them.

Reboot if it says a reboot is required, then **verify you can log in as `silexgis` and run
`sudo -n id`** before going any further.

### 2. Close off password logins

```bash
sudo bash harden-ssh.sh
```

Separate from step 1 on purpose: it disables root logins and password authentication, and
doing that before a key login has been proven is the classic way to lock yourself out of a
remote machine. It refuses to run if the deploy account has no authorised key, and it checks
the result with `sshd -T` rather than trusting that writing the file was enough.

That check matters more than it looks. Ubuntu cloud images ship
`/etc/ssh/sshd_config.d/50-cloud-init.conf` containing `PasswordAuthentication yes`, and sshd
keeps the **first** value it reads for a keyword — the opposite of the last-one-wins convention
these directories usually follow. A hardening file named `60-something.conf` is therefore
present, correct, and completely ineffective, which reads as success in every check that looks
at the file rather than at the running daemon. The shipped drop-in is named `01-silexgis.conf`
for exactly this reason.

### 3. Install the application

As the `silexgis` account:

```bash
SILEXGIS_HOST=203.0.113.10 bash install-app.sh
cd /opt/silexgis/deploy
docker compose build
docker compose up -d
```

`install-app.sh` clones the repository to `/opt/silexgis` — **with its submodules**, which is
not optional: the compiled cave-survey readers live in `server/external/ThIDE`, and a checkout
without them does not fail at clone time but much later, as a C# "type or namespace not found"
error inside the Docker build. Re-running the script repairs a checkout cloned without them.
It then writes `deploy/.env` with generated database and administrator passwords. It never overwrites an existing `.env` — that
file holds the password the database was initialised with, and regenerating it would leave the
API unable to reach its own data.

The `.env` it writes sets `COMPOSE_FILE`, so the optional overlays this installation uses are
recorded once, in the file, instead of being retyped on every command. Plain `docker compose`
in that directory then means the same thing the update script means by it. Check with
`docker compose config --services`; you should see every service you expect and no others.

### 4. Install the update and backup tooling

As root:

```bash
bash install-ops.sh
```

This installs the `silexgis-update` command and a nightly backup timer. It also installs a
weekly automatic-update timer but leaves it **disabled**: updating an application unattended,
when its schema may change underneath it, is a decision to take deliberately rather than a
default to inherit.

### 5. Create the first content

```bash
cd /opt/silexgis/deploy
docker compose run --rm api seed-demo     # optional demo dataset
```

The administrator account named in `.env` is created on first start and placed in Full
Administrators. Sign in with it and create the accounts you need.

## What ends up running

| Service | Published port | Notes |
|---|---|---|
| `web` | **80** (the only one) | nginx: serves the SPA and proxies `/api`, `/connect`, `/health`, `/openapi` to the API |
| `api` | none | reachable only from `web`, on the project network |
| `db` | none | PostGIS. Never published; reach it with `docker compose exec db psql` |
| `convert` | none | Gotenberg, if the conversion overlay is in use |
| `terrain-worker` | none | if the terrain overlay is in use; talks to the API through a directory, not a socket |

**The mobile SpeleoLoc API is not a separate port.** It is served under `/api/v1/…` by the same
API container and proxied by the same nginx, so one public port carries both the browser and
the phone. Nothing additional needs opening.

Persistent state lives in four named Docker volumes and nowhere else:

| Volume | Holds |
|---|---|
| `silexgis-db` | the database |
| `silexgis-files` | uploaded documents and photographs |
| `silexgis-keys` | data-protection keys (sessions, file links) |
| `silexgis-terrain` | terrain builds and published pyramids |

## Updating

```bash
silexgis-update --check    # what would change
silexgis-update            # do it
```

The update fetches the tracked branch, **takes a backup**, fast-forwards the checkout,
rebuilds the images, recreates the containers, and waits for `/health/ready`. If the build
fails, or the new version does not become healthy within five minutes, it resets the checkout
to the previous commit, rebuilds, and brings that back up.

Data survives because it is in volumes, and rebuilding an image or recreating a container does
not touch a volume. Database migrations run automatically when the API container starts.

**The one command that destroys data is `docker compose down -v`.** The `-v` removes the named
volumes. It appears nowhere in these scripts and should appear nowhere in any procedure you run
against an installation whose data you want. `docker compose down` without it is safe.

### The limit of an automatic rollback

Rolling the *code* back is not always enough. If the new version started and applied a database
migration before failing, the restored old code may meet a schema it does not understand. That
is what the pre-update backup is for, and why the script prints the path to it when it gives up.
Restore with [`deploy/scripts/restore.sh`](../deploy/scripts/restore.sh).

There is also a case with no upgrade path at all, and it is worth knowing about before it
happens. Development branches fold their accumulated migrations into one before merging, so a
database that tracked an unreleased branch can carry migration identifiers the new build has
never heard of while already having the tables it wants to create. Migrations run at start, so
this presents as a container that restarts for ever rather than as an error message. The remedy
is to recreate the database and reload — which, on an installation whose data matters, means
the backup. An installation that only ever tracks released versions is never in this position.

## Backups

`silexgis-backup.timer` runs nightly at 03:20 UTC and keeps the last 14, plus one before every
update. Each backup is a directory containing `db.sql.gz` (a `pg_dump`) and `files.tar.gz` (the
uploads volume).

```bash
systemctl list-timers silexgis-\*        # when it last ran and next runs
journalctl -u silexgis-backup --since -7d
ls -lh /var/backups/silexgis
```

Three things are deliberately *not* in a backup, and all three are recoverable without one:

- **Data-protection keys.** Losing them invalidates outstanding file links and signs everyone
  out. It does not touch stored data.
- **Baked terrain.** Derived data whose recipe is in the database. Rebuild it. The exception is
  a raster **uploaded through the browser**, which has exactly one copy under the build root and
  none in the file store — an installation fed that way should add `silexgis-terrain` to its
  backups.
- **`.env`.** It holds the database password. Keep a copy somewhere off the machine, or a
  restore will meet a database it cannot open.

Backups are written in the clear. Encrypting them is an operator step by design — see
INSTALL.md, "Encryption at rest". A backup on the same disk as the thing it backs up is not a
backup; copy them off the host.

## Security posture

What these scripts set up:

- SSH: public keys only, no root login, three auth attempts, 30-second grace, fail2ban on top.
- Firewall: deny inbound except SSH and the web port.
- Automatic **security** updates only. Docker's repository is excluded — an engine upgrade
  restarts every container, which is an operator's decision, not a nightly one.
- The stack runs as an unprivileged account; the API runs as a non-root user inside its
  container; the database and every optional service publish no port at all.

Two things to be clear-eyed about:

**The firewall does not gate published container ports.** Docker inserts its own rules ahead of
ufw's filter chain, so `ufw deny 5432` would not close a published database port. What keeps the
database private is that it is never published. Check with `docker compose config | grep -A2
published` before believing any port is closed, and prefer `127.0.0.1:` bindings for anything
that should be local-only.

**Membership of the `docker` group is equivalent to root.** Anyone who can reach the container
engine can start a privileged container and read the host. The deploy account therefore also has
passwordless `sudo` — not because that widens anything, but because pretending otherwise would
be theatre.

## HTTPS is required, not recommended

**An installation served over plain HTTP at anything other than `localhost` cannot be signed
into — by anyone.** This is not a policy or a hardening preference; it is a browser rule with no
way around it.

The sign-in flow is OpenID Connect authorization-code with PKCE, and the server *requires* PKCE.
Computing the PKCE challenge means `crypto.subtle.digest('SHA-256', …)`, and browsers expose the
Web Crypto API only in a **secure context** — HTTPS, or `localhost`. On `http://<an-ip>` the
page gets `window.isSecureContext === false` and `crypto.subtle === undefined`, so the redirect
throws before a single request leaves the browser.

The failure is quiet and points the wrong way. The API is healthy, the discovery document is
served, `/connect/authorize` works when asked directly — and the site shows "Cannot reach the
server / Sign-in could not be started", with **nothing in the browser console**, because the SPA
catches the rejection to avoid a permanent spinner. It reads as a broken server. It is not one.

This is also why `docker compose up -d` followed by `http://localhost:8080` works on a developer's
own machine and the same image on a server does not: `localhost` is a secure context by
definition, and an IP address is not.

### With no domain yet

A wildcard DNS service resolves an IP-derived hostname with nothing to configure, and Let's
Encrypt will issue for it — so you can have real HTTPS before you have a domain:

```
158-220-114-220.sslip.io   ->   158.220.114.220
```

Pass it to the installer and the TLS overlay is configured for you:

```bash
SILEXGIS_DOMAIN=158-220-114-220.sslip.io bash install-app.sh
```

### Moving to your own domain

Once DNS points at the host:

```bash
cd /opt/silexgis/deploy
# in .env:
#   SILEXGIS_DOMAIN=caves.example.org
#   SILEXGIS_PUBLIC_URL=https://caves.example.org
#   SILEXGIS_TLS_EMAIL=you@example.org
#   COMPOSE_FILE=...:docker-compose.tls.yml
sudo ufw allow 443/tcp
docker compose up -d
```

Caddy obtains and renews the certificate; the TLS overlay stops publishing the plain port. Ports
80 and 443 must be reachable for the ACME challenge. `SILEXGIS_PUBLIC_URL` must match the
hostname exactly — OIDC redirect URIs, including external-provider callbacks, derive from it.

Switching from a wildcard-DNS hostname to your own domain is exactly these three lines: the
OIDC client registration is re-seeded from `PublicUrl` on every API start, so the redirect URI
follows automatically.

## Troubleshooting

```bash
cd /opt/silexgis/deploy
docker compose ps                    # what is up, and what keeps restarting
docker compose logs -f --tail 100 api
docker compose logs --tail 50 web
curl -sS localhost/health/ready
```

**"Cannot reach the server" on a site that is plainly up.** Almost always the secure-context
rule above: the site is being served over plain HTTP somewhere other than localhost. Confirm it
in the browser console with `window.isSecureContext` — `false` is the answer. Serve it over
HTTPS; no server-side setting changes this.

**The API restarts in a loop.** Almost always a migration that cannot be applied — see the note
above. `docker compose logs api` shows the failing step.

**Login returns 500 while health checks pass.** The data-protection key ring cannot be written.
Check that `silexgis-keys` is mounted and owned by the container's `app` user.

**Uploads fail at a certain size.** Three ceilings must agree: `SILEXGIS__Files__MaxUploadBytes`,
Kestrel's limits (derived from it automatically), and the proxy's `client_max_body_size`, which
is baked into the `web` image at build time. INSTALL.md, "Upload sizes", has the detail.

**Disk filling.** `docker system df`. Image layers live under `/var/lib/docker` and old build
caches accumulate; `docker system prune -a` reclaims them, at the cost of a slower next build.
