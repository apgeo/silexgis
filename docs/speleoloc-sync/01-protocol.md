# Moving rows between a device and a SilexGIS installation

This document is for whoever writes the client side. It describes the transfer itself: how a device
finds out what a server can do, how it names the caves it wants to carry, and how it reads them a
page at a time so that a lost connection costs one page rather than the whole sync.

It is written as the server is built, so a section that is not here yet is a part that does not
exist yet. What is described below is **the read direction only** — a device can currently take rows
from a server and cannot yet send any back. The server says so itself: `features` in the
capabilities answer names `download` and does not name `upload`, and a device is expected to read
that rather than to discover the gap at the first write.

Signing in is a separate document; everything here assumes a bearer token obtained the way that one
describes. **No route in this slice is reachable without one.** There is no anonymous read of a sync
route, and there never will be — a device is an account holder or it is nothing, and it sees exactly
what that account would see in the web interface.

---

## 1. Asking what the server can do

```
GET /api/v1/sync/capabilities
Authorization: Bearer <token>
```

```json
{
  "contractVersion": 1,
  "pageSizeMax": 500,
  "uploadRowsMax": 500,
  "features": ["download"]
}
```

- **`contractVersion`** is the protocol generation this build speaks. It is a statement about the
  code, not a setting an installation may lower. A device pins the version it was built against; a
  server that has moved on is answered rather than guessed at.
- **`pageSizeMax`** and **`uploadRowsMax`** are the ceilings a device sizes itself to. Asking for
  more is not an error — the server clamps — but a device that asks blind and gets clamped cannot
  tell how much it actually got without counting.
- **`features`** names the optional halves of the contract this build actually serves. It is how a
  device meets a server that has shipped some of the protocol and not the rest, and takes what is
  there.

Ask this first, once per server, and keep the answer. It is also the cheapest place to discover that
a stored credential has lapsed: it answers `401` like everything else here.

## 2. Naming what a device carries

A device does not download "everything visible". It carries an explicit **sync set**: a named
selection of root features, plus the settings its code generation depends on. Sync sets are created
and edited through `/api/v1/sync/sets` — see the interface itself for the write shapes; what matters
to the transfer is:

- **A set names roots, not rows.** Whatever is contained in a named root is in the set, including
  anything added under it later. There is no list of synced objects to keep up to date on either
  side.
- **A set is private to the account that owns it.** Nobody else can read it, administrators
  included, and a set somebody else owns is reported exactly like one that does not exist.
- **Membership is a choice, not a right.** What actually leaves the server is decided at read time
  against the caller's own visibility, so a root that stops being readable simply stops producing
  rows. Naming a cave in a set never widens what the account may see.

## 3. Reading a page

```
GET /api/v1/sync/sets/{setId}/download
GET /api/v1/sync/sets/{setId}/download?cursor=<opaque>&pageSize=100
Authorization: Bearer <token>
```

```json
{
  "setRevision": 4,
  "settings": { "pciStrategy": "ro-default", "digits": 4 },
  "features": [ … ],
  "tombstones": [ { "id": "…", "deletedAt": "2026-08-28T09:14:52.113Z" } ],
  "nextCursor": "MXwxNjM4…",
  "hasMore": true
}
```

**The cursor is the whole of the device's position, and the server keeps nothing between requests.**
That is not an optimisation: a phone loses its connection in a car park and comes back the next
weekend from a different address, and a server-side session would either have expired or would have
to be kept per device for ever. Send back the `nextCursor` you were last given and you will get what
has changed since, in the same order, with nothing repeated. Two limits on "nothing skipped" are
spelled out below — a changed selection, and a concurrent write — and neither is a detail a client
can be written without.

- **Treat `nextCursor` as opaque.** Its contents are the server's business and its shape may change
  between contract versions. A cursor this server did not issue is refused with
  `sync.cursor_invalid` rather than read as "start again" — a silent restart is indistinguishable
  from an incremental page and would cost a device a full re-download it never asked for.
- **`nextCursor` is present whenever the page carried anything**, so a device that has caught up
  still keeps a watermark to come back with. It is null only when there was nothing at or after the
  position it asked from.
- **`hasMore` false means level with the server as of this read**, not "stop asking". It is the
  signal to stop looping now, not to stop syncing.
- **`pageSize` is optional**, defaults to 100 and is clamped to the announced `pageSizeMax`.
- **The order is the change order**, and both halves of the payload share it: a page can carry live
  rows and tombstones together, and the single cursor resumes either. Do not sort the arrays.

**A cursor belongs to the selection it was issued against.** Edit the sync set — add a root, remove
one, change its settings — and every cursor issued before that edit is refused with `409` and
`sync.cursor_stale`. Drop the cursor and read the set from the beginning.

This is not fussiness. Adding a cave to a set writes nothing to any feature row, so nothing under
the new cave is ever *after* a caught-up device's position: a server that accepted the old cursor
would answer "nothing new", and the cave the caver had just asked their phone to carry would stay
invisible until some unrelated edit happened to touch it. The refusal is the only signal that can
exist, and it is why `setRevision` is worth watching (§7) even though watching it is not required.

**A child can arrive before its parent.** The order is the change order and has nothing to do with
containment: rename a cave after creating a place inside it and the place is now the older change,
so it arrives first — a whole page earlier at small page sizes. A device must buffer a `parents`
reference it cannot resolve yet, or apply the containment edges in a second pass, rather than
assuming the parent has already been seen.

**"Nothing skipped" is not absolute under concurrent writes.** The change key is stamped by the
application as a row is written, which is fractionally before that write commits. A slow write that
began before a download and committed after it therefore carries a key behind the cursor that
download handed out, and is not delivered again until something touches the row. It is rare — it
needs a write whose commit is slow enough to straddle a read — and this document states it rather
than promising an absolute a client would trust. A device that wants certainty can drop its cursor
and re-read the set from the beginning periodically; a full read is always correct.

## 4. What a feature row carries

| Field | Notes |
|---|---|
| `id` | The row's identity on the server, stable for its lifetime |
| `kind` | `cave`, `caveEntrance`, `centerline` or `generic` — **treat an unknown value as a row to store and ignore, never as an error** |
| `featureTypeCode` | The kind's stable code — `cave_place`, `cave_area`, `surface_area` and others. **This is not a closed list**, and **never resolve a kind by a numeric id**: ids belong to whichever installation seeded the table |
| `category`, `name`, `description` | As stored |
| `geometry` | GeoJSON, SRID 4326. Exact or absent — see §6 |
| `properties` | The property document, verbatim. Where the device's own identifiers live |
| `propertiesSchemaVersion` | The kind's schema version this row was last validated against |
| `locationProtected` | Whether this row is itself a protection root |
| `protectedEffective` | Whether the position this row carries is guarded at all — by this row or by anything containing it. **This is the field to branch on**, not `locationProtected` |
| `visibility` | The audience the row is stored with |
| `parents` | The containment edges above it, `{ parentId, isPrimary }`, restricted to parents this caller may read — so **not necessarily a complete ancestry** |
| `createdAt`, `updatedAt` | Server-stamped |
| `clientUpdatedAt` | The moment a device believed it last wrote the row, by that device's own clock — `null` on everything the web interface made, which today is everything |

**`updatedAt` is this row's revision on the server, and the value to send back as the base revision
when writing it.** It is the server's own stamp, and the only value the server compares.

**`clientUpdatedAt` is provenance, not a revision.** It is carried so that two devices which edited
the same row while both were offline can compare their versions with each other — a question the
server is in no position to answer for them. It never decides which write the server keeps: a
phone's clock is unsynchronised, resettable by whoever holds it and routinely wrong by hours, so a
rule that preferred the later `clientUpdatedAt` would let one bad clock overwrite anything.

**The table above is the whole row.** Kind-specific columns are deliberately not folded into it, so
a device never receives a field whose protection was decided for a different channel.

**A selection carries everything under its roots, of every kind.** There is no filter by kind or by
type code: a synced cave brings its entrances, the places and areas inside it, and its surveyed
centerline, because all of them are contained in it. Two consequences to plan for:

- **`centerline` rows exist and can be large.** A centerline's geometry is the cave's whole survey
  as a `MultiLineString`, and one row of it can be far bigger than the other ninety-nine on a page.
  `pageSize` counts rows, not bytes, so a device on a poor connection should lower it rather than
  assume a page is small.
- **New kinds and new type codes appear without a contract change**, because an installation's
  taxonomy is its own. Store a row whose `kind` or `featureTypeCode` you do not recognise and leave
  it alone; do not drop it, or your next upload deletes it.

**Containment is not decoration.** Protection and visibility are both inherited along containment and
along nothing else, so a device that drops `parents` rebuilds a tree that means something different
from the stored one — and a row that ends up with no container inherits from nothing.

**But do not derive protection from `parents`.** That list is filtered to parents this caller may
read, so a row can sit inside a guarded cave the caller cannot see and arrive with no parents at
all. `protectedEffective` is on the row for exactly that reason and is the answer; the edges are for
rebuilding the tree.

## 5. What a tombstone carries

```json
{ "id": "0198f1b4-…", "deletedAt": "2026-08-28T09:14:52.113Z" }
```

An identifier and the moment the row went. **Nothing else, ever.** Everything else a feature row
holds says something: a name is half of what protection hides, and the ancestry would publish which
cave a deleted place hung under. A device needs neither — it already holds the row it is being told
to drop and matches it by identifier.

Tombstones ride the same cursor as live rows, which is what makes a delete propagate at all: without
them a device re-uploads the cave the club removed and the deletion undoes itself.

**A row can be gone from the payload for two different reasons, and they are not the same.** A
tombstone says the row was deleted. A row that simply stops appearing may have been withheld, or may
have left the set, or may have stopped being readable — none of which is a deletion, and none of
which a device should treat as one.

**A tombstone can arrive for a row you were never sent.** The withholding of §6 applies to live rows
only: a row whose position this account may not see is absent from `features`, but its deletion is
still reported. That is deliberate in both directions. A stub is an identifier and a moment, and
neither of those locates anything — while a deletion that fails to propagate is permanent, because a
device that held the row from before it was guarded would keep it for ever and put it back on its
next upload. Apply a tombstone for an unknown identifier as a no-op.

## 6. Geometry is exact or absent — never approximate

Elsewhere in this API, a caller who may not see a protected cave's exact position is shown a
grid-snapped point flagged as approximate. **The sync channel does not do that.** A row whose exact
position this caller is not entitled to see is **absent from the payload entirely** — no geometry,
no stub, no flag, no count.

The reason is what this channel is. A map view is looked at once; a download is written to a phone in
cleartext, kept for as long as the app is installed, and re-shared by the device to people this
server never authenticated. And these rows come in numbers: a scatter of protected places snapped
into the same few grid squares outlines the cave whose position the protection exists to hide.

Two consequences a client author should plan for:

- **A withheld row is invisible, not empty.** It does not shorten a page and it is not counted. A
  device cannot tell a withheld row from one that was never there, which is deliberate.
- **A row that becomes readable does not arrive on its own.** Being granted the right to place a
  cave exactly writes nothing to that cave's row, so its change key does not move and an incremental
  read will not carry it — the same shape as a changed selection (§3), but with no revision anywhere
  for the server to notice it by. A device that has just been granted something, or that wants to be
  sure, drops its cursor and reads the set from the beginning. A full read is always correct.
- **This is a property of this channel, not of the server.** The same account, with the same token,
  still reaches grid-snapped coordinates for the same caves through `/api/v1/caves`,
  `/api/v1/features` and `/api/v1/export`. If a device ever reads those routes, it is reading
  approximate positions, and it must never send one back as if it were surveyed.

## 7. The settings document

Every page carries `settings` and `setRevision`.

`settings` is the device's own configuration — the code-generation settings that keep two devices
producing compatible place codes. **The server stores and returns it verbatim and forms no opinion
about what any of it means.** It is not validated for strategy semantics, because validating it here
would reject settings that are legal on the device.

`setRevision` is a counter, not a timestamp: two edits inside one clock tick must still be
distinguishable. It moves only when something about the set actually changed — re-sending the
selection a device already holds is not an edit and does not tell it that its copy has gone stale.
Compare it to the revision you last saw to find out whether to re-read the document.

**A moved `setRevision` also retires every cursor issued before it** (§3). A device that notices the
revision has moved can drop its cursor immediately; a device that does not notice is told, because
its next request with the old cursor is answered `409 sync.cursor_stale` rather than answered short.
Either way the response is the same: read the set again from the beginning.

## 8. The recorded traffic, which is the actual specification

Everything above is an explanation. The thing to write code against is in `contract/speleoloc-sync/v1/`:
one directory per exchange, each with the request line and the exact body that came back.

They are taken from a real server by the test suite that also asserts about that traffic, and a
normal run of the suite compares the live server's answers against them and fails on any difference.
They change only when somebody re-records them deliberately, so a diff there is a contract change
and is read as one. **Where this document and those files disagree, the files are right** — prose
drifts from a payload silently and a byte comparison cannot.

Four cases cover the read direction:

| Directory | What it shows |
|---|---|
| `07-download-first-page` | A first read: settings, a cave, and the place inside it with its containment edge |
| `08-download-cursor-restart` | The page after it, asked for with the cursor the first one returned |
| `09-download-tombstones` | A row that has gone — an identifier and a moment, and nothing else |
| `10-download-protected-withheld` | A caller who may read a cave but not one point inside it: the point is simply not in the payload |

**What no recording covers yet, stated so it is not read as absence of the thing:** there is no
fixture showing a `centerline` row, and none showing a refusal — neither `sync.cursor_invalid` nor
`sync.cursor_stale` has a recorded exchange. All three are asserted by the test suite and described
above; they are simply not among the four bodies committed as bytes.

Three kinds of value are replaced in those files, because they differ on every run and would
otherwise make the comparison meaningless: identifiers (minted server-side as each row is written,
so there is nothing to pin), timestamps (stamped from the server's own clock) and the resume cursor
(opaque, and it moves with the data). `contract/speleoloc-sync/v1/README.md` lists them. Everything
else — field names, field order, nesting, and every value not in that list — is exactly as sent.

## 9. Generating a client from the served description

The server publishes its own description at `/openapi/v1.json`. Two things in it matter to whoever
generates code from it, and both are recent:

- **The bearer scheme is declared, document-wide.** Until it was, the description said nothing about
  authentication at all — the requirement lives on the route group rather than on any per-route
  metadata a describer can see — so a generator reading it would have emitted calls carrying no
  credential. It is declared once and required for the whole document, and the short allow-list of
  deliberately open routes takes the requirement back off itself with an empty per-operation
  `security`, which OpenAPI defines as "no requirement here". That half matters most on the sign-in
  calls: a generated client must not demand a token on the one call whose purpose is to obtain one.
  No sync route is on that allow-list.
- **The sync operations are named**, and almost nothing else on this server is. The names are
  `syncCapabilities`, `syncListSets`, `syncCreateSet`, `syncGetSet`, `syncReplaceSet`,
  `syncDeleteSet` and `syncDownload`. An unnamed operation gets a name invented by the generator
  from its path, which then moves whenever a path is tidied; these are chosen deliberately and are
  part of the contract. Naming the rest of the server's operations is a larger change than this one
  and has not been made, so do not expect an identifier on any operation outside this slice.

The failure responses this slice can produce are declared alongside them, which is what puts the
`sync.*` codes into the description. What each one means, and whether retrying could ever change the
answer, is in the errors document.
