# Moving rows between a device and a SilexGIS installation

This document is for whoever writes the client side. It describes the transfer itself: how a device
finds out what a server can do, how it names the caves it wants to carry, and how it reads them a
page at a time so that a lost connection costs one page rather than the whole sync.

It is written as the server is built, so a section that is not here yet is a part that does not
exist yet. Both directions now exist: §§3–7 are the read, and §8 is the write. The server says which
it serves — `features` in the capabilities answer names `download` and `upload` — and a device is
expected to read that rather than to discover a gap at the first request.

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
  "features": ["download", "upload"]
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

### 1.1 What a version bump means, and what it does not

**A client must ignore fields it does not recognise, everywhere in this protocol**, and must not
treat an unexpected member as a malformed response. That rule is what makes the version below stable:
adding an optional field to a response, or accepting a new optional field on a request, is
deliberately **not** a contract change, and a build that fails on one would break on a server upgrade
it was never meant to notice.

**`contractVersion` moves only for a change that breaks a client pinned to the previous value** —
a field removed or renamed, a meaning changed, a value that used to be accepted and is not. New
capability is announced through `features` instead, which is why that list exists.

This is worth stating in full because it is an exception to how the rest of this project works. The
server is pre-alpha and deliberately waives backward compatibility of its schema, its model, its API
and its stored data: a wrong shape is rewritten rather than preserved. **The sync contract is the one
place that waiver does not reach.** A phone in the field pins what it was built against, cannot be
redeployed on the server's schedule, and has no way to discover a change except by being told. So
this surface is versioned, the version is answered before anything else is asked, and an upload that
states a version this build cannot serve is refused with the server's own version in the message
rather than being interpreted generously.

### 1.2 The settings an installation can change under a device

Four of this installation's numbers are configuration rather than code, and an operator may set
them per installation with an environment variable. Two of them are the ceilings answered in
`capabilities`; the other two change what an answer contains without being announced anywhere,
which is why they are listed here as well. A client that reads the ceilings from `capabilities` at
every sign-in rather than compiling them in is unaffected by any of it.

| Setting | Default | What it changes for the client |
|---|---|---|
| `SILEXGIS__Sync__PageSizeMax` | 500 | The ceiling in `pageSizeMax`. A larger `pageSize` on a download is clamped to it, silently — count the rows you got rather than assuming you got what you asked for |
| `SILEXGIS__Sync__DefaultPageSize` | 100 | The page a download hands back when the request names no `pageSize` at all. **It is announced nowhere**, so a device that needs to know its page size sends one rather than inferring this. Set above `PageSizeMax` it is served as that maximum |
| `SILEXGIS__Sync__UploadRowsMax` | 500 | The ceiling in `uploadRowsMax`. A batch above it is refused whole, before any row is looked at |
| `SILEXGIS__Sync__DuplicateRadiusMeters` | 50 metres, the same default a file import uses | How near an existing row a newly written one has to be to appear in the duplicate report. **Zero turns the report off entirely**, so an empty report is not evidence of no duplicates |

Both ceilings are themselves clamped by the server to a sane range, so a misconfigured installation
cannot answer a `pageSizeMax` of zero or of a million; the default page is in turn held inside
whichever ceiling is in force, so it can never hand back more than the announced maximum.

## 2. Naming what a device carries

A device does not download "everything visible". It carries an explicit **sync set**: a named
selection of root features, plus the settings its code generation depends on. Sync sets are created
and edited through `/api/v1/sync/sets` — see the interface itself for the write shapes; what matters
to the transfer is:

- **A set names roots, not rows.** Whatever is contained in a named root is in the set, including
  anything added under it later. There is no list of synced objects to keep up to date on either
  side.
- **A set is private to the account that owns it**, and a set somebody else owns is reported
  exactly like one that does not exist. One narrow exception exists and is off unless an
  installation switches it on: `SILEXGIS__Sync__AllowAdministratorRead` lets a full administrator
  *read* a set whose identifier they already know, so that somebody can answer "which caves does
  this phone hold?". It turns on the account rather than on the client, so a device signed in as a
  full administrator reaches it too — but it widens that one route and nothing else: no listing
  widens, no set becomes editable by anyone but its owner, and **the download and upload routes
  always resolve a set by its owner**, so a device can never transfer through a set the account it
  is signed in as does not own. It is not announced in `capabilities` for that reason: it says
  something about the people running an installation, not about the protocol.
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
- **`pageSize` is optional.** Omitting it asks for the installation's own default page — 100 unless
  an operator has changed it, and never more than the announced `pageSizeMax`. Any size that is
  named is clamped to that same maximum.
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
| `clientUpdatedAt` | The moment a device believed it last wrote the row, by that device's own clock. It is `null` on every row the web interface wrote, and carries whatever the device sent on every row an upload wrote |

**`updatedAt` is this row's revision on the server, and the value to send back as the base revision
when writing it.** It is the server's own stamp, and the only value the server compares.

**`clientUpdatedAt` is provenance, not a revision.** It is carried so that two devices which edited
the same row while both were offline can compare their versions with each other — a question the
server is in no position to answer for them. It never decides which write the server keeps: a
phone's clock is unsynchronised, resettable by whoever holds it and routinely wrong by hours, so a
rule that preferred the later `clientUpdatedAt` would let one bad clock overwrite anything.

**A point may carry a third ordinate, and it is the altitude.** A downloaded
`geometry` is GeoJSON, so a position that has an altitude arrives as
`[longitude, latitude, altitude]` and one that has none arrives as `[longitude, latitude]`. There is
no separate `altitude` member on a downloaded row: a client that reads only the first two ordinates
silently drops every altitude the installation holds. `positionQuality` is **write-only** in this
generation — an entrance stores it, and nothing hands it back.

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

**Replacing the set is arbitrated on `setRevision`, exactly as a row is on its own revision.**
A write to `/api/v1/sync/sets/{setId}` replaces the whole document — the selection and the settings
together — so it carries `baseRevision`: the revision the caller last read. Sending none is answered
`400 sync.set_revision_required`; sending one the set has moved past is answered `409
sync.set_conflict`, and the reflex is the same as for any other conflict: read the set again,
re-apply the change, send it back with the revision that read returned. This matters most for the
settings: two devices allocating place codes from different digit widths produce codes that do not
fit together, and a stale write that won silently would leave nothing anywhere to notice it by.

**A moved `setRevision` also retires every cursor issued before it** (§3). A device that notices the
revision has moved can drop its cursor immediately; a device that does not notice is told, because
its next request with the old cursor is answered `409 sync.cursor_stale` rather than answered short.
Either way the response is the same: read the set again from the beginning.

## 8. Writing rows back

```
POST /api/v1/sync/sets/{setId}/upload
Authorization: Bearer <token>
```

```json
{
  "batchId": "0198f2aa-…",
  "contractVersion": 1,
  "rows": [
    {
      "id": "0198f2ab-…",
      "kind": "cave",
      "baseRevision": null,
      "deleted": false,
      "name": "Peștera Mică",
      "caveTypeCode": "cave",
      "isMain": false,
      "clientUpdatedAt": "2026-08-28T07:02:11.000Z"
    }
  ]
}
```

Four ideas hold this half together, and each of them exists because of a way an offline device
breaks.

### 8.1 The batch identifier, which is what makes a retry safe

**The device mints `batchId`, and sending the same one again returns the first answer and writes
nothing.** An answer lost on the way back is the ordinary case on a mobile connection, and without
this it would cost a duplicate registry rather than a resend. Mint a version-7 uuid per *attempt*;
never reuse one for different rows, and never mint a new one for a resend of the same rows. If a
batch has to be split for `uploadRowsMax`, each part is its own attempt and gets its own identifier.

The recorded answer is the decisions, not the rows: a replay is answered `"replayed": true` with the
same `rows` and the same `importBatchId`, while the conflict echo and the duplicate report are
worked out afresh — an account's right to see a position can be withdrawn between the two calls.

**Every upload becomes an import batch**, which is what lets a caver look at what a phone put into
the registry and take all of it back through the same screen and the same button that undoes a bad
file. `importBatchId` is that batch.

### 8.2 The row identifier, adopted verbatim

**A new row carries `baseRevision: null` and the identifier the device gave it, and the server
adopts that identifier.** Both sides mint version-7 uuids and neither re-keys the other's, which is
what removes the translation table a two-sided identifier scheme would need, and with it every way
that table could go stale.

Two consequences:

- **A create whose identifier is already here writes nothing** and comes back `unchanged` with the
  row's current revision. That is what makes a retry safe when the answer to the first attempt was
  lost but the write itself landed.
- **An identifier in use by a row this account may not read** is answered `rejected` /
  `sync.id_conflict`. That does disclose that the identifier is taken, and it is accepted: a
  version-7 uuid carries enough randomness that guessing a live one is infeasible unless it has
  already leaked, and the alternative is a translation table for ever. **Do not re-key the row.**

### 8.3 The base revision, which is the only thing arbitrated on

**An edit carries `baseRevision`: the `updatedAt` the device last saw for that row.** It is compared
for equality against what the server holds now, and nothing else is compared — never the device's
own clock, which is unsynchronised, resettable by whoever holds the phone, and routinely wrong by
hours. A row whose revision has moved is answered `conflict` and **not applied**; the rest of the
batch still is.

**Arbitration is per row, and so is the answer.** A device that edited forty caves offline and lost
one to a conflict is told which one, and does not have the other thirty-nine thrown away with it. So
a row's verdict rides a `200`:

```json
{
  "batchId": "0198f2aa-…",
  "importBatchId": "0198f2b0-…",
  "replayed": false,
  "written": 1,
  "refused": 1,
  "rows": [
    { "id": "0198f2ab-…", "status": "created", "revision": "2026-08-28T09:14:52.113Z", "code": null, "detail": null },
    { "id": "0198f2ac-…", "status": "conflict", "revision": "2026-08-28T08:00:00.000Z", "code": "sync.conflict", "detail": "…" }
  ],
  "conflicts": [ … ],
  "duplicates": [ … ]
}
```

`status` is one of `created`, `updated`, `deleted`, `unchanged`, `conflict`, `rejected`. **Store the
`revision` that comes back** — it is what the next edit of that row sends as its `baseRevision`.
The errors document is the list of `code` values and what to do about each.

**Rows are applied in the order they are given**, so a container may be created earlier in the same
batch than the row that hangs under it. Send a cave before its entrances and an area before the
places inside it. A row may appear only once in a batch; a batch naming one twice is refused whole.

**Deleting is arbitrated exactly as editing is.** Send `"deleted": true` with the `baseRevision` the
device holds. A row already gone here answers `unchanged`; a row that moved on answers `conflict`.
Removing a row and editing one are **different rights**, so an account allowed to correct a cave may
still be refused its deletion — a delete takes the row's whole containment subtree with it.

### 8.4 What a row may carry, and what it may never carry

| Field | Notes |
|---|---|
| `id` | The device's own identifier, adopted verbatim |
| `kind` | `cave`, `caveEntrance` or `generic`. Anything else is `rejected` / `sync.kind_unsupported` |
| `baseRevision` | The `updatedAt` last seen, or `null` for a new row |
| `deleted` | Asks for the row to go |
| `parentId` | The row this one is contained by — see below |
| `name`, `description` | As stored. `name` is at most 255 characters |
| `caveTypeCode`, `entranceTypeCode`, `featureTypeCode` | The kind, **by its stable code, never by a numeric id** |
| `isMain` | For an entrance: whether it is the cave's main one |
| `geometry`, `altitude`, `positionQuality` | The position, and how it was obtained. `altitude` is kept on every kind that carries a point — it is stored as the point's third ordinate, which is also how it comes back. `positionQuality` is stored only on an entrance, and is not returned by any route |
| `properties` | The property document, stored verbatim. Where the device's own identifiers and codes live |
| `clientUpdatedAt` | When the device believes it last wrote the row. Stored as provenance; **never consulted in the arbitration** |

**An upload is a partial write.** `name`, `description`, `properties` and the position are what a
device owns on a row that already exists. A member the device does not send is a member the server
leaves alone, and that now holds for every one of them — until 2026-09-01 `name` and `description`
were written unconditionally, so an update naming only one of them emptied the other.

**A field cannot be cleared in this generation.** An absent JSON member and an explicit `null`
arrive at the server as the same value, so the rule that makes omission safe also makes "the caver
emptied this description" unsendable: sending `"description": null` leaves the stored description
alone rather than clearing it. A device that needs to clear a field sends a space, or waits for a
generation whose row shape distinguishes the two. This is a real limit, not an oversight, and it is
the other half of the decision that stopped the data loss. **Containment and kind are set when a row is created and
are not carried by this contract afterwards**: on an update `parentId`, `caveTypeCode`,
`entranceTypeCode` and `featureTypeCode` are read by nothing, so a row sent with a different
container comes back `updated` — honestly, for the fields that were written — while the edge stays
where it was. Moving a row to another container, or changing its kind, is not something version 1
carries; do it in the browser.

The thirty-odd server-only cave fields — the survey figures, the exploration status, the descriptive
text somebody typed in the browser — are **never cleared** by an upload that does not mention them,
because there is no way for a device to mention them.

**No coordinate may ever go in `properties`.** That document is handed to every reader of a row with
no protection filter anywhere on its path, so a position stored in it would be published to exactly
the people the guard below exists to keep it from. Keep the device's index and codes there; keep
positions in `geometry`.

**`parentId` is a request, and the server makes the edge.** Containment is never accepted as a
free-standing structure a device declares, because protection and visibility are inherited along
containment and along nothing else — a client-declared edge would be a client-declared audience. The
container has to exist and be one this account may add to, or the row is refused
(`sync.parent_not_found`, `sync.parent_forbidden`). Whether a row *needs* one is a property of its
kind: `cave_area` and `cave_place` do; `surface_area` does not and is written with nothing above it.
A container named on a **new** row is always made into a real edge, never quietly dropped. On an
existing row the field is inert, as above.

**A cave's own `geometry` is not a field.** It is a copy of its main entrance's, kept in step by the
server, so a cave that arrives carrying a point is stored without it rather than refused — the
entrance that follows in the same batch is where that position belongs.

### 8.5 The write-right rule, stated per field

**An account that may write a row but may not see its position exactly keeps its name, description
and property edits, and loses only the geometry, the altitude and the position quality.** The row
comes back `updated`, and it is `updated` honestly: refusing the whole row would lose a caver's
rename to protect a coordinate that was never going to move.

The reason for the loss is what §6 describes. A caller without exact view was never shown the stored
position — elsewhere in this API they are shown a grid-snapped one — so a coordinate they send back
came from somewhere other than the truth, and writing it would replace a real position with a
degraded one.

**This applies to creates as well, which is the part most likely to be got wrong.** The usual
reasoning — "a coordinate arriving with a new row is the caller's own, so echoing it back discloses
nothing" — is about *where the value came from*, not about which verb carried it, and it does not
survive this channel: a device replays coordinates this server handed it. Creating an entrance
writes a position onto the cave above it, so an entrance sent for a cave whose position this account
may not see exactly is refused outright with `sync.location_forbidden` rather than half-applied.

### 8.6 The conflict echo, and its absence rule

`conflicts` carries **this server's own version of every row that lost a conflict**, shaped exactly
as a download would have delivered it, so a device can show a caver what it is being asked to merge
against instead of making them go and fetch it.

**A row that lost and is missing from `conflicts` is a row whose position this account may not
have.** That is the same answer the download gives — absence, never a blurred stand-in (§6) — and it
is read the same way: the row changed, and re-reading it is how to find out what to. The echo is a
list beside the decisions rather than a field inside them, so the recorded answer a replay is served
from can never hold a position.

### 8.7 The duplicate report, which is never a verdict

`duplicates` says what was already here, near a row this batch **created**. Every row listed was
written and its entry in `rows` says so; nothing here changes a status.

It exists because a device is offline when it decides to add a cave and cannot ask first. A caver
who surveyed a shaft forty metres from one a clubmate entered last week has no way of knowing, and
refusing the row would leave the work nowhere to go — so the row lands and the answer says what it
landed next to. Deciding whether the two are the same cave is the caver's, not the server's.

```json
{ "id": "0198f2ab-…",
  "nearby": [ { "id": "…", "name": "Avenul Mare", "kind": "caveEntrance",
                "distanceMeters": 41.2, "caveFeatureId": "…" } ] }
```

Two properties to plan for. **Rows this same batch wrote are never each other's duplicates** — a
cave and the places inside it are a few metres apart by construction. And the pool searched is what
this account may read *and* place exactly: "something is within fifty metres of this point" is
itself a position, so a protected row this account may not place is not compared against and not
reported. The cost is real and is accepted — a duplicate of such a row will not be noticed — because
the alternative is answering a position to somebody entitled to none.

### 8.8 Whole-batch refusals

Some failures are the batch's, not a row's, and then the answer is a problem document and nothing at
all was written: a `contractVersion` this server does not speak, more rows than `uploadRowsMax`, a
set that is not this account's, a caving-group binding this account is no longer entitled to, and
the same batch identifier arriving twice at once. They are listed with their codes in the errors
document.

## 9. The recorded traffic, which is the actual specification

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

Five cover the write direction, and these have a `request.json` as well: a write is not described by
its request line, and what a device sends is the half the other application has to compose rather
than merely parse.

| Directory | What it shows |
|---|---|
| `11-upload-create` | A new row under the identifier the device minted, with `baseRevision: null` |
| `12-upload-retry` | The same batch identifier sent again: `replayed: true`, the first answer, nothing written |
| `13-upload-conflict` | A stale `baseRevision`: the row is refused and the server's own version comes back in `conflicts` |
| `14-upload-conflict-withheld` | The same refusal for a row this caller may not place: the decision is there and the echo is **absent** |
| `15-upload-delete` | A tombstone going up, arbitrated on `baseRevision` exactly as an edit is |

Three cover refusals, under `16-errors/`. A refusal carries a `status.txt` beside its body, holding
the status line: the status is what a client branches on before it has read a single field, and it
travels in the status line rather than in the payload. A successful exchange has no such file — its
status is 200 by construction.

| Directory | What it shows |
|---|---|
| `16-errors/cursor-invalid` | `400`, `sync.cursor_invalid`. A resume position this server never issued: the device throws the position away rather than retrying |
| `16-errors/cursor-stale` | `409`, `sync.cursor_stale`. A position that was good until the selection moved. A different status and code for what is the same decision on the device — drop it and read the set from the beginning |
| `16-errors/contract-unsupported` | `409`, `sync.contract_unsupported`. A build pinned to a version this server does not speak. The batch is refused whole, and **there are no per-row results at all** — a client that reads `rows` without first checking the status crashes here |

**What no recording covers yet, stated so it is not read as absence of the thing:** there is no
fixture showing a `centerline` row, none showing a duplicate report, and none showing a row-level
refusal that is not a conflict. All of them are asserted by the test suite and described above; they
are simply not among the bodies committed as bytes.

Two files in that directory are about the recordings rather than part of them. `manifest.json`
carries a digest for every file and one roll-up over all of them, which is how a copy of the
directory living in another repository tells whether it is current without diffing it — and, because
it is generated from a walk of the directory rather than from what the tests name, it is also the
only thing that notices a file left behind by an exchange that was renamed or deleted.
`CHANGELOG.md` is written for somebody whose build pins a contract version: every entry says whether
that build still works.

Three kinds of value are replaced in those files, because they differ on every run and would
otherwise make the comparison meaningless: identifiers (minted server-side as each row is written,
so there is nothing to pin), timestamps (stamped from the server's own clock) and the resume cursor
(opaque, and it moves with the data) — and, in a refusal, the correlation identifier the framework
attaches to every problem document, which is for reading a server log with rather than for a client
to act on. `contract/speleoloc-sync/v1/README.md` lists them. Everything
else — field names, field order, nesting, and every value not in that list — is exactly as sent.

## 10. Generating a client from the served description

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
  `syncDeleteSet`, `syncDownload` and `syncUpload`. An unnamed operation gets a name invented by the generator
  from its path, which then moves whenever a path is tidied; these are chosen deliberately and are
  part of the contract. Naming the rest of the server's operations is a larger change than this one
  and has not been made, so do not expect an identifier on any operation outside this slice.

Failure statuses are declared alongside those names, which is what puts the sync failures into the
description at all. **The declaration is orientation, not a contract**: it is incomplete in at least
one place — the delete route declares the `404` it can answer and not the `409` it can also answer —
and it never carries the `code`, which is a value rather than a type. The errors document is the
authority for both, and it says what each code means and what the client is supposed to do about it.
