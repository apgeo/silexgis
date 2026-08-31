# A server to work against

Everything below is one command away from a SilexGIS checkout. The server it starts is a
**throwaway**: its own database container, its own volume, its own ports, seeded from nothing, and
meant to be deleted rather than maintained. Nobody hosts it, nobody backs it up, and its passwords
are printed here.

---

## 1. The one command

```
node deploy/speleoloc-dev.mjs up
```

Run from the root of a SilexGIS checkout. It needs Docker and the .NET SDK on the machine, and
nothing else — the script is plain Node with no packages of its own. §5 is the route for a machine
that has Docker but no .NET SDK.

What it does, in order:

1. Starts a PostGIS container of its own, `silexgis-speleoloc-db`, on `127.0.0.1:5434`, with its own
   named volume.
2. Builds the server and runs it once to migrate the schema and seed the reference data — cave
   types, rock types, map layers, permission groups and the bootstrap administrator.
3. Loads the **demonstration dataset** — six caves, entrances, an area, documents.
4. Adds the **sync development data** — a caving group, a second account that is a plain member of
   it, and the group binding the demonstration dataset leaves off. §3 says why that step is not
   optional.
5. Serves the API on `http://127.0.0.1:5205`.
6. **Signs in as the member account, completes the whole authorization-code exchange, and reads
   `/api/v1/sync/capabilities`** before saying a word about success.

That last step is the point of the script and not a flourish. Both seeding commands exit *zero* when
they refuse to do anything — they log a reason and return — so a script that trusted an exit code
would happily announce a working server with no accounts in it. Signing in for real is the only
check that fails when the thing it is checking is broken.

Then it prints the base URL, both accounts, the group, the client id and the whole sign-in dance as
`curl` commands, and stays in the foreground serving. **Ctrl-C** stops the API and leaves the
database running.

Options: `--db-port N` and `--api-port N`, if either default collides with something you are already
running.

## 2. What you get

| | |
|---|---|
| Base URL | `http://127.0.0.1:5205` |
| Sync capabilities | `GET /api/v1/sync/capabilities` — contract version 1, `download` and `upload` |
| OpenAPI document | `http://127.0.0.1:5205/openapi/v1.json` (no database needed to render it) |
| Database | `127.0.0.1:5434`, database `silexgis`, user `silexgis`, password `silexgis` |
| Client id | `silexgis-speleoloc` — public, **no secret**, PKCE `S256` required |
| Redirect URIs | `http://127.0.0.1:54321/callback` (any loopback port; the registered entry carries none and the port is excluded from matching) and `speleoloc://auth` |

Two accounts:

| Account | Password | What it is |
|---|---|---|
| `member@dev.local` | `dev-member-pass-1` | **Sign in as this one.** A plain member of the caving group who owns nothing, so what it can see is decided by the permission and protection rules rather than by ownership. |
| `admin@dev.local` | `dev-admin-pass-1` | The bootstrap administrator. A full administrator: it sees every coordinate exactly and owns every demonstration object, so it demonstrates none of the behaviour a client has to handle. |

Caving group: **Demo Caving Club** (`demo-caving-club`). Both accounts are members; the
administrator owns it.

## 3. The data, and what the member account sees

The demonstration dataset alone cannot show a single rule this API exists for: every object in it
belongs to the administrator, who is also a full administrator, so every signed-in caller sees every
position exactly, and there is no caving group at all. The second seeding step is what makes the
rules observable from outside.

| Code | Cave | Visibility | What `member@dev.local` gets |
|---|---|---|---|
| `DEMO-0001` | Peștera Demo Mare | Public | Everything, exact. Two entrances. |
| `DEMO-0002` | Avenul Demo Protejat | Authenticated | **Its exact position is protected.** The cave and its entrance are **absent from the sync download entirely** — not delivered blurred. See below. |
| `DEMO-0003` | Peștera Demo Mică | Public | Everything, exact. |
| `DEMO-0004` | Peștera Demo Ursului | Public | Everything, exact. |
| `DEMO-0005` | Avenul Demo Vântului | Authenticated | Everything, exact. |
| `DEMO-0006` | Peștera Demo Izvorului | Caving group | Visible **because** the account is in Demo Caving Club. Drop the membership and it disappears. |

`DEMO-0002` is the row worth writing tests against. Create a sync set whose roots are all six caves
and download it:

- as `member@dev.local` the page carries **11 rows**, and `Avenul Demo Protejat` and its `Shaft`
  entrance are simply not among them — no row, no geometry, no placeholder;
- as `admin@dev.local` the same selection carries **13 rows**, those two included, with exact
  coordinates.

That difference, against the same selection on the same server, is the protection rule working, and
it is the cheapest thing to point a first client test at. (Creating the set needs both
`rootFeatureIds` and a `settings` object — an empty one is fine; `01-protocol.md` §2 has the shape.)

Note the member's page **does** carry `Peștera Demo Izvorului`, the group-only cave. Visibility and
location protection are two different rules and the download applies both: the group membership is
what lets that cave through, and the protection flag is what keeps the other one out.

**Where the withholding does and does not reach.** It bounds the sync channel, not the server. The
same member account, with the same token, reaches a **grid-snapped** position for `DEMO-0002` through
`/api/v1/caves`, `/api/v1/features` and `/api/v1/export`, which are unchanged and are not part of
this contract — that cave comes back from the cave list with `approximateLocation: true` and a
coordinate on a grid, rather than not at all. The grid is **5 km** on this installation
(`SILEXGIS__Access__LocationGridMeters`, default `5000`), and the position is rounded to the nearest
intersection on both axes, so the snapped point is typically kilometres from the true one: for
`DEMO-0002`'s entrance the answer is `25.19763, 45.49946`, about **2.4 km** away. Read it as "somewhere
in this massif", never as a position anybody can walk to, and do not write a client test asserting a
tighter bound — the server does not promise one, and an installation may set that grid wider still. The sync channel is stricter than the rest of the installation on purpose: a phone
keeps what it downloads, in cleartext, and re-shares it over offline archives, so an approximate
position delivered there would be permanent. Do not read the sync behaviour as a statement about
what the account may see anywhere else.

## 4. Signing in

The script prints the whole sequence with the port it actually served on filled in. It is the same
dance `03-auth.md` describes, and the two must agree — if they ever do not, the printed one is the
one that ran. In outline:

1. `POST /api/v1/auth/login` with `{"email": …, "password": …}` → a `silexgis.session` cookie. The
   authorization endpoint consumes that cookie and this is the only thing that produces one.
2. `GET /connect/authorize?…` carrying the cookie, **without following redirects**. The code is in
   the `Location` header and the response has no body.
3. `POST /connect/token`, form-encoded, no client secret, with the PKCE verifier from step 2. The
   `redirect_uri` must be byte-identical to the one in step 2, port included.
4. `GET /api/v1/sync/capabilities` with `Authorization: Bearer …`.

Ask for `offline_access` in the scope or step 3 returns no refresh token, and the device is back at
the password prompt every fifteen minutes.

## 5. Without the .NET SDK

The same stack, with the server as an image:

```
docker compose -f deploy/docker-compose.speleoloc-dev.yml --profile api up -d --build
```

The first build pulls the .NET SDK and ASP.NET runtime images and compiles the server: roughly 3 GB
of image storage and several minutes. Image layers do **not** land on the Docker named-volume disk —
they go wherever the container runtime keeps its content store, which on a split-disk machine is
usually the smaller one, so watch that rather than the volume disk.

This route migrates and seeds the reference data at startup, but the demonstration dataset and the
sync development data are commands that run and exit, so they are two more invocations:

```
docker compose -f deploy/docker-compose.speleoloc-dev.yml run --rm api seed-demo
docker compose -f deploy/docker-compose.speleoloc-dev.yml run --rm api seed-speleoloc-dev
```

The image's entrypoint already invokes the server assembly, and `run` replaces the *command*, not
the entrypoint — so `seed-demo` on its own is the whole argument. Naming the runtime and the
assembly again passes them to a process that is already running them.

Same accounts, same ports, same everything else. Confirm it the same way the script does — sign in
and read the capabilities — rather than trusting that the containers came up.

## 6. Stopping, and starting over

```
node deploy/speleoloc-dev.mjs down     # stops the database; the volume and its data survive
node deploy/speleoloc-dev.mjs reset    # removes the container AND its volume
```

After `reset`, the next `up` starts from an empty database and reseeds everything, which is how you
get back to the table in §3 after an afternoon of writing rows into it. Both seeders are idempotent,
so running `up` again over an existing database tops up what is missing and changes nothing else.

## 7. Two things it deliberately does not do

**It never touches the shared development database.** A SilexGIS checkout also carries
`deploy/docker-compose.dev.yml`, which is the database the web application's own developers run,
published on every interface. Migrating and seeding that one would rewrite somebody else's working
data, and it would look exactly like a successful run of this script right up until their session
broke. This stack has its own project name, its own container, its own volume and its own ports, and
the script passes `docker compose` exactly one file — its own.

**Nothing it publishes leaves the machine.** Both ports are bound to `127.0.0.1`. That is not
belt-and-braces: this installation's passwords are printed in a public document, and a container
port published without an address is reachable from the network even on a host whose firewall says
otherwise, because published ports are inserted ahead of the host's own rules. If you need to reach
it from another machine, tunnel to it; do not rebind it.
