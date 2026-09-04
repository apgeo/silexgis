# Deploying SilexGIS on a server

This is the operator's runbook for putting SilexGIS on a machine of its own and keeping it
there: preparing the host, standing the stack up, updating it without losing data, and getting
it back after a bad update.

It complements [INSTALL.md](INSTALL.md) rather than replacing it. INSTALL.md is about the
application — its settings, its optional services, what each one does. This document is about
the *server*: the account the stack runs as, the firewall, the swap, the backup schedule, and
the update procedure. Where the two overlap, INSTALL.md is the authority on the application's
own configuration.

Everything here is done by five scripts under [`deploy/server/`](../deploy/server/):
`provision-host.sh`, `harden-ssh.sh`, `install-app.sh`, `install-ops.sh` and `set-domain.sh`.
They are idempotent, they are the same scripts used to build the reference installation, and
running them is what makes this reproducible rather than a list of commands somebody has to
retype correctly.

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

An operator who needs the opposite — direct root logins, or password authentication, for
convenient remote access — can ask for it explicitly:

```bash
sudo SILEXGIS_SSH_ALLOW_ROOT=1 SILEXGIS_SSH_ALLOW_PASSWORD=1 bash harden-ssh.sh
```

Both are off by default and each is a real cost, not a preference. Permitting root logins
removes the record of which person did what, since everyone arrives as the same account.
Permitting passwords exposes every account on a public address to continuous credential
guessing; fail2ban blunts that without removing it. Re-running the script without the switches
puts both back.

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

The update logic is *copied* to `/opt/silexgis-ops/`, outside the deployed checkout, rather
than being run from inside it. A script that lives in the tree it is about to `git pull` can be
rewritten underneath the shell executing it, and a copy placed into the checkout by hand
collides with the pull the moment the same path arrives from upstream. Re-run `install-ops.sh`
after an update to refresh that copy.

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
| `pgadmin` | `127.0.0.1:5050` | if the pgAdmin overlay is in use. Loopback only: it stores database credentials, so it is reached over an ssh tunnel rather than published |
| `caddy` | **80 and 443** | if the TLS overlay is in use, in which case it fronts `web` and `web` stops publishing a port of its own |

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

One command covers the four places a hostname is written — the reverse proxy, the public URL,
the OIDC redirect registrations and the TLS contact address:

```bash
/opt/silexgis-ops/set-domain.sh \
  --primary caves.example.org \
  --also    158-220-114-220.sslip.io \
  --email   you@example.org
```

`--primary` becomes `PublicUrl`: the address in emails and share links, the OIDC issuer, and
the redirect URI sign-in returns to. `--also` is repeatable and keeps the previous address
working across the move — the SPA takes its OIDC authority from whatever is in the browser's
address bar, so each additional name needs its own `/auth/callback` registered or signing in
through it is refused as an invalid redirect. The script registers them, restarts the proxy and
the API, and waits until the primary name really is serving a trusted certificate before it
reports success. `sudo ufw allow 443/tcp` first if the host was provisioned without TLS.

#### Get the DNS right first, because the failure does not look like DNS

The script inspects the records before changing anything and refuses if they are wrong. Three
things must hold for every name:

1. **Exactly one `A` record, pointing here.** Two `A` records is not redundancy, it is
   round-robin: half your visitors reach the other machine, and so does much of Let's
   Encrypt's validation traffic.
2. **No `AAAA` record unless it points here.** This is the one that costs an afternoon.
   Browsers and Let's Encrypt both *prefer* IPv6, so a stale `AAAA` sends everything to the
   wrong server while the `A` record you just fixed sits there looking correct. The CA reports
   it as an unauthorized challenge naming an address you were not expecting:

   ```
   2a0f:4480:0:7::48e: Invalid response from
   http://caves.example.org/.well-known/acme-challenge/...: 404
   ```

   If you are not deliberately serving over IPv6, delete the `AAAA`. If you are, point it at
   this host and confirm the host answers there.
3. **Ports 80 and 443 reachable.** Port 80 is not optional even though the site redirects away
   from it — the HTTP-01 challenge arrives on it.

DNS caches for its TTL. Lower the TTL *before* the change; afterwards you are only waiting.
Once the records are right, Caddy picks the certificate up on its next retry, or immediately
with `docker compose restart caddy`.

#### Configuring a name before its DNS is ready

`--skip-dns-check` writes the configuration anyway, and doing so deliberately is the safe
order: keep the working address as `--primary`, add the new one with `--also`, and the live
site is untouched while Caddy retries the new name in the background. When DNS is corrected
the certificate appears on its own; the only remaining step is to re-run the command with the
two names swapped.

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
