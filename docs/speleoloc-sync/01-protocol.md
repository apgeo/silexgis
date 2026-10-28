# Moving rows between a device and a SilexGIS installation

This document is for whoever writes the client side. It describes the transfer itself: how a device
finds out what a server can do, how it names the caves it wants to carry, and how it reads them a
page at a time so that a lost connection costs one page rather than the whole sync.

It is written as the server is built, so a section that is not here yet is a part that does not
exist yet. Both directions exist: §§3–7 are the read, and §8 is the write. §§11–15 are what a phone
carries beyond caves and places — photos and documents, the people directory, trip recordings, the
review hold and raster maps — each with its own stream and its own upload sections. The server says
which parts it serves — `features` in the capabilities answer — and a device is expected to read that
rather than to discover a gap at the first request.

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
  "features": ["download", "upload", "documents", "documentUpload", "trips", "reviews", "rasters", "people"]
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
  there. `documents` means the photo and document catalogue of section 11 and the address route
  beside it are served; `documentUpload` means a phone may send photos and documents back (§11.3):
staged bytes through the transfer routes, and the upload's `documents` and `documentLinks` sections;
`trips` means the trips stream and the upload's `tripRecordings` and `tripPoints` sections of section 13;
`rasters` means the rasters stream and the upload's `rasterMaps` and `rasterPins` sections of section 15;
`reviews` means the reviews stream of section 14 is served, and that an account under review is
answered `held` to a device that opts in with `clientFeatures: ["held"]`;
`people` means the people directory and the upload's `people` section of
  section 12 are, which an installation can switch off and which is named only to an account that
  may be told who people are. The list is worked out for each request, not fixed for the build: a part an
  installation can switch off is named only while it is on, so read it from the answer every time
  rather than once per build. A name a device does not recognise is ignored.

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
| `featureTypeCode` | The kind's stable code — `cave_place`, `cave_area`, `surface_area` and others. **This is not a closed list**, and **never resolve a kind by a numeric id**: ids belong to whichever installation seeded the table. A `cave` row carries `cave` here too — the type every cave has, which holds a club's own cave fields; read a row by its `kind` first, and never send `cave` as the type of a `generic` row (it is refused with `sync.type_unknown`) |
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

**An upload is made of sections.** `rows` — the feature rows above — is the first; the request may
carry further arrays beside it as later parts of the contract are served (each announced through
`features`), and each array is answered in the array of the same name, item by item, in the shape
`rows` is answered in. All arrays are optional, but an upload carries at least one item in some
array. The sections are applied in a fixed order, whatever order the arrays sit in the request —
`rows` always first — so a later section may refer to what an earlier one created. `uploadRowsMax`
counts the items of every section together; `written` and `refused` count them together too. A
device that sends only `rows`, as every device did before sections, sends a complete upload, and is
answered exactly as before.

**`clientFeatures`** (optional, a list of names, at most 16) says which protocol behaviours beyond
the first contract this device understands. A server only answers in a way a device opted into here,
so a build that predates a behaviour is never handed an answer it would misread. The list is kept with
the upload's batch as sent; names this server does not recognise are kept and otherwise ignored. Each
name is defined in the section that introduces it. The one defined so far is **`held`**: the device
understands the row status `held` (below).

**When an account's uploads wait for a reviewer.** An installation can mark an account so that what
it uploads is reviewed before it lands. For such an account an upload writes nothing: each row that
would have been written is kept as a proposal and answered `held`, with a `reviewId` and the revision
the row already had (null for a new row). A request from such an account that does not list `held` in
`clientFeatures` is refused whole with `409 sync.client_update_required` and nothing is kept, because a
build that cannot be told a row is held would read the answer as lost or as written. An account that is
not marked is answered exactly as before, and no answer to it carries `reviewId`.

The proposals are checked the way an upload is applied — each row after the rows before it, so an
entrance sent beside its new cave is checked against that cave — and the account's proposals still
waiting count too: a place sent today under a cave still waiting from an earlier upload is checked
against that cave and waits on it. Nothing a held row proposes is visible to anyone through any route
until it is accepted. Send held rows again as usual: the same content is answered with the same
`reviewId` and keeps nothing new; different content replaces the waiting proposal under a new
`reviewId`. The decisions are reported on their own route — section 14.

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

**Taking an upload back is a true undo** (`POST /api/v1/import-batches/{importBatchId}/revert`, the
confirmer's or a full administrator's act, called by the web interface, not by a device): the rows the
upload created are deleted, the rows it deleted come back, and every field it changed returns to its
value before the upload — read out of the history, where every row an upload writes is filed under
its batch. A field somebody changed after the upload is left and reported as superseded unless the
undo is forced; a position goes back only for a caller with exact view. The phone's own edit time
(`clientUpdatedAt`) goes back with the other fields. A cave's main entrance goes back to the entrance
that held the role before the upload — whether the upload promoted another one or deleted the main
one — so the cave plots where it did; a role somebody moved since is left where they put it. A row the
upload deleted comes back only for a caller who may still delete it, the right any restore asks. A
line of an upload that wrote nothing to its row (a row sent back as it stood) counts as undone; a
line of an upload made before the history named its batch cannot be taken back and is answered
`no_history`, and closed with the rest. The undo can take a few lines at a time (`itemIds`), and the
batch counts as reverted once every line has been. **A device needs
nothing new for this**: every row the undo touches is written anew, so the next download from the
device's cursor hands over the undone state — the edited rows as they were, the deleted ones back,
the created ones as tombstones.

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
all was written: a `contractVersion` this server does not speak, more items than `uploadRowsMax`, a
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

Three more refusals sit under `16-errors/`, and one of them is not a refusal of the exchange:

| Directory | What it shows |
|---|---|
| `16-errors/client-update-required` | `409`, `sync.client_update_required`. An account under review uploading from a build that does not declare `clientFeatures: ["held"]`. Refused whole, with no `rows`, like the version refusal |
| `16-errors/feature-disabled` | `409`, `sync.feature_disabled`. The people directory asked for while this installation has it off |
| `16-errors/file-bytes-missing` | A `200`, **with no `status.txt`**: the batch is accepted and its one document row comes back `rejected` with `sync.file_bytes_missing` (§11). A client reads the row's status, not the exchange's |

The second set of parts (§§11–15) is recorded from `17-` upward:

| Directory | What it shows |
|---|---|
| `17-documents-first-page` | The documents stream for a reader who may not place everything a photo hangs on: `reach: "locationFree"`, and `sizeBytes`/`sha256` `null` because the bytes it will be given are not the stored file (§11) |
| `18-files-links` | The address route, one call for the whole file and one picture; each item answered on its own (§11) |
| `19-upload-document-and-link` | An upload carrying a staged photo and the link hanging it on a cave (§11) |
| `20-upload-held` | An upload from an account under review: both rows `held`, no revision, a `reviewId` each, nothing written (§14) |
| `21-reviews-first-page` | A held edit turned down, with the server's row as `current` for a caller who may place it (§14) |
| `22-reviews-current-withheld` | The same decision for a caller who may not: `current` is absent, not blurred (§14) |
| `23-people` | The people directory: clubmates and the caller, names only, and the caller's links (§12) |
| `24-trips-first-page` | A club recording as another member is handed it, the scan at a place it may not see left out without a word (§13) |
| `25-rasters-first-page` | A raster map with the one pin a member may see; the pin on a protected place is absent (§15) |

The pictures these exchanges upload are generated, so their hash is replaced by `<sha256>` in the
recordings, and an optional `sizeBytes` is left out of what goes up: both are the imaging library's,
and pinning them would rewrite the recordings on an upgrade of that library rather than on a change
to this protocol. The `CHANGELOG.md` entry of 2026-10-26 announces these recordings; fixtures
`07`–`16` were not changed by it.

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

The server publishes its own description at `/openapi/v1.json`. Four things in it matter to whoever
generates code from it:

- **The bearer scheme is declared, document-wide.** The requirement lives on the route group rather
  than on any per-route metadata a describer can see, so it is declared once and required for the
  whole document, and the short allow-list of deliberately open routes takes the requirement back
  off itself with an empty per-operation `security`, which OpenAPI defines as "no requirement here".
  That half matters most on the sign-in calls: a generated client must not demand a token on the one
  call whose purpose is to obtain one. No sync route is on that allow-list.
- **Every operation is named.** The sync names are `syncCapabilities`, `syncListSets`,
  `syncCreateSet`, `syncGetSet`, `syncReplaceSet`, `syncDeleteSet`, `syncDownload`,
  `syncUpload`, `syncDocuments`, `syncFileLinks`, `syncPeople` and `syncTrips`; they are chosen deliberately, are pinned by the server's own tests, and are part of
  the contract. Every other operation on the server is named too, after the code that handles it,
  so a generated client no longer invents a name from a path — but only the sync names are
  promised not to move.
- **The problem document is declared with its `code`.** The published problem shape carries the
  `code` member beside `type`, `title`, `status`, `detail` and `instance`, so a generated client
  types it rather than dropping it. The *values* it may take are not enumerated there; section 9
  of the errors document says why.
- **What a request always needs is marked required.** A member a request's validator refuses to do
  without is published as required and not nullable, and one that may not be empty carries a
  minimum length or item count, so a generated client cannot build a request that is always
  refused without a type error to warn it. Rules that hold only when something else does, or that
  apply to each item of a list, are not published, because they say nothing about whether the
  member is present.

Failure statuses are declared alongside those names, which is what puts the sync failures into the
description at all; the `401` is declared on every route that needs a token and the `429` on every
route a rate limit applies to. **The declaration is orientation, not a contract**: it is incomplete
in at least one place — the delete route declares the `404` it can answer and not the `409` it can
also answer — and it never says which `code` a status arrives with, because that is a value rather
than a type. The errors document is the
authority for both, and it says what each code means and what the client is supposed to do about it.

## 11. Photos and documents

A set's features carry photos and documents — an entrance photo hung on a cave, a survey sheet, a
report. A device is told about them through a **catalogue**, and fetches the files themselves only
when its caver asks for them. Nothing is pushed: the catalogue is small, a photo is not, and a phone
on a mountain decides what it can afford to carry. The server keeps no record of what a device
fetched.

Served when `features` names `documents`.

### 11.1 The catalogue

```
GET /api/v1/sync/sets/{id}/documents?cursor=<opaque>&pageSize=<n>
Authorization: Bearer <token>
```

It is read exactly as the feature download of section 3 is read — the same set resolution (a set
another account owns answers `404 sync.set_not_found`), the same opaque cursor, refused with `400
sync.cursor_invalid` when it was not issued by this stream and with `409 sync.cursor_stale` when the
set changed, the same `pageSize` clamped to `pageSizeMax`, the same `setRevision` and `settings`, and
tombstones interleaved with live rows on the one cursor. **The two streams have separate cursors**: a
position the feature download issued is refused here, and the other way round, because each stream
orders its own rows and one's position says nothing about where to resume the other.

```json
{
  "setRevision": 4,
  "settings": { },
  "links": [
    {
      "id": "0199…",
      "documentId": "0199…",
      "target": { "kind": "feature", "id": "0199…" },
      "role": "photoEntrance",
      "caption": "Entrance in winter",
      "sortOrder": 0,
      "isPrimary": true,
      "inDefaultSet": true,
      "revision": "2026-10-24T09:14:52.113412Z",
      "document": {
        "id": "0199…",
        "title": "entrance.jpg",
        "description": null,
        "fileKind": "photo",
        "mimeType": "image/jpeg",
        "sizeBytes": 2483112,
        "sha256": "9f…",
        "versionId": "0199…",
        "reach": "original",
        "renditions": [2400, 1200, 480, 160],
        "photographerCaverId": null,
        "createdByDeviceUserId": null,
        "capturedAt": "2026-02-11T10:02:00Z",
        "createdAt": "2026-10-20T18:00:01.000001Z",
        "updatedAt": "2026-10-20T18:00:01.000001Z"
      }
    }
  ],
  "tombstones": [ { "id": "0199…", "deletedAt": "2026-10-24T09:20:00.5Z" } ],
  "nextCursor": "…",
  "hasMore": false
}
```

**A row is a link** — one document hung on one feature — with the document embedded. The same
document hung on two caves is two rows with two identifiers; a device that keeps one copy of the file
keys it on `documentId` and `versionId`.

- **Which links are listed.** Only links on features the feature download would hand this account
  right now: inside the set, readable, and not withheld (section 6). A link on a withheld cave is
  **absent, not stubbed** — a row hanging there would say where to look. And only documents this
  account may read by the document's own rules: a document hung on a cave the account reads is
  normally readable through that cave, but a rule written against the document itself wins, and
  then the link is absent though its cave is not.
- **When a link changes.** Its change key is the latest of the link's own last change, its
  document's and its feature's, so a renamed document, a new revision and a changed caption or flag
  all bring the link back past a caught-up device's position — and so does any change that makes the
  feature download hand the feature out again (its location guard lifted, say): the feature's links
  come back with it. A device may therefore be sent a link it already holds, unchanged; it replaces
  its copy. `revision` is the link's own last change.
- **`inDefaultSet`** marks the files somebody curating the cave chose as the ones worth carrying by
  default. A phone may offer "download the usual files" at a size its caver picks; the flag decides
  nothing else.
- **`fileKind`** is one of `photo`, `video`, `audio`, `document`, `other`.
- **`reach`** says what fetching the file itself would hand this account, and is decided by the same
  rule the web interface uses:
  - `original` — the stored file, byte for byte. `sizeBytes` and `sha256` are its own.
  - `locationFree` — the whole picture at full size, rewritten without its location data: the GPS
    block, the XMP packet and the IPTC place names are removed, the camera, lens and exposure
    details stay. Given to an account that may read the photo but may not be told where it was
    taken, and to every account when the installation switched location in photo files off.
    `sizeBytes` and `sha256` are **absent**: the rewritten copy is made the first time somebody asks
    for it, and quoting the stored file's numbers would announce bytes the device will never
    receive. Check what arrives against the response's own length — or, where the installation
    turned on *Tell phones the size and fingerprint of photos sent without their location*
    (`DescribeLocationFreeCopies`), against the numbers the address route gives beside the URL
    (section 11.2).
  - `renditions` — no file itself, only the pictures the server draws of it. What an image in a
    format that cannot be rewritten is, for an account that may not have its location (or for every
    account while location in photo files is switched off).
  - `none` — neither: what a video or a sound recording is for the same accounts. A photo, a video
    and a recording are treated as carrying a location whether or not one was ever read out of
    them — a cleared position or a fix a phone wrote into a video is still in the bytes.
  - With location in photo files switched off, an installation may also turn on *still let people
    who may see the location download videos, recordings and pictures that cannot be rewritten*
    (`ServeUnstrippableOriginalsToLocationReaders`): then an account that **may** be told the
    location gets those files as `original` again; an account that may not is unaffected and keeps
    `renditions` or `none`.
- **`renditions`** lists the picture sizes the server draws, largest first; it is empty for any
  file that is not an image.
- **`photographerCaverId`** is who took a photo: the caver named on it in the browser, or — when
  nobody is named, by caver or by name — the caver its phone user is linked to (section 12),
  resolved as the catalogue is read, so a link made in the browser later applies and an unlink
  takes it off. A photographer named in the browser always wins. It is given to every reader of the
  photo, whatever the people directory lists for them, exactly as the web gallery shows the
  photographer to every reader of the photo — it is the photo's credit, not a directory entry; a
  device names it from `people` when it can, and otherwise shows the photo without a name.
- **`createdByDeviceUserId`** is the phone user who added the file, as the phone's upload stated it
  (section 11.3); null when none did, or when the file did not come from a phone. It round-trips as
  a recording's does.
- **No position, ever.** Not the photo's capture point, not its target's coordinates. Where a picture
  was taken is not part of this row on any reach; a device that needs the cave's position reads it
  from the feature download, under that stream's rules.

**Tombstones** are an identifier — the link's — and a moment, exactly as in section 5. A link goes in
two ways: detached (by whatever path: a person, a regenerated report replacing the one it superseded,
an import undone in bulk, a file deleted outright), or left behind by a document that was deleted —
then every link to it is a tombstone at the moment the document went. Both are reported for links on
features the set hands this account. A link that stops appearing for any other reason — its cave
withheld, gone from the set, its document no longer readable — is not a deletion, and as in section 5
a device should not treat it as one; a tombstone for a link it never held is a no-op. When a feature
itself is deleted, its own tombstone arrives on the feature download; links on it are not repeated
here.

### 11.2 Fetching files

```
POST /api/v1/sync/files/links
Authorization: Bearer <token>
Content-Type: application/json

{ "items": [ { "documentId": "0199…", "rendition": "original" },
             { "documentId": "0199…", "rendition": 480 } ] }
```

`rendition` is the word `"original"` or one of the sizes `2400`, `1200`, `480`, `160` as a number.
One request names between 1 and 200 items; anything else, or an unknown rendition, refuses the
request whole with `400`.

```json
{
  "links":   [ { "documentId": "0199…", "rendition": "original",
                 "url": "/api/v1/files/0199…/content?token=…", "expiresAt": "2026-10-24T13:14:52Z",
                 "sizeBytes": null, "sha256": null } ],
  "refused": [ { "documentId": "0199…", "rendition": 480, "code": "sync.rendition_unavailable" } ]
}
```

**Every item is decided again** — the catalogue a device read may be hours old, and access can have
been withdrawn since. An item is answered only if the account may read the document now, at the reach
the rule gives it now; the URL opens the document's current revision. A refused item names its
`code`, and the rest of the request is answered as usual:

| Code | Meaning |
|---|---|
| `sync.document_unavailable` | No such document, deleted, or no longer readable by this account — one answer, so the three cannot be told apart |
| `sync.original_withheld` | The file itself is not handed to this account; pictures of it may be (`reach` was `renditions` or `none`) |
| `sync.rendition_unavailable` | The server draws no picture of this kind of file |

The URLs are relative to the server, need no `Authorization` header — the signed token in them is the
credential — and stop working at `expiresAt`. A file itself gets four hours, because the content route
answers HTTP `Range` requests and a phone resumes a large file across a poor connection; a picture gets
ten minutes, because it is small and fetched at once. A URL that has expired answers `404` — ask for
a new one. A URL already handed out keeps opening until it expires even if access is withdrawn in the
meantime; that bounded staleness is what the short lifetimes are for. An original fetched under
`locationFree` reach is the rewritten copy; it has the same name and type as the stored file.

**`sizeBytes` and `sha256` on a link** are `null` unless both hold: the item is an `"original"`
handed under `locationFree` reach, and the installation turned on *Tell phones the size and
fingerprint of photos sent without their location* (`DescribeLocationFreeCopies`, off by default).
Then the server makes the rewritten copy while answering — for every such item of the request, which
makes a request for many of them slower — and the two are the copy's own: exactly the bytes the URL
serves (`sha256` lower-case hex, like the catalogue's). A stored original is described by the
catalogue and gets no numbers here; a picture never does. Should the copy fail to be made, the link
comes without numbers and fetching it answers `404` either way. The catalogue never quotes the copy's
numbers, whatever the switch says.


### 11.3 Sending photos and documents

A phone sends a file in two moves: **the bytes first**, through the server's ordinary resumable
transfer routes, and then **the row that names them**, in an upload batch. The bytes travel on their
own because a photograph is megabytes and the link is often poor: a transfer resumes where it stopped,
and a batch of rows is never held hostage to a half-sent picture.

#### Sending the bytes

```
POST /api/v1/files/uploads
Authorization: Bearer <token>
Content-Type: application/json

{ "fileName": "IMG_0001.jpg", "sizeBytes": 2483112, "stageForSync": true }
```

`stageForSync: true` is what makes this a phone's staged file rather than a browser upload. The answer
is `201` with the transfer's `id`, how much has arrived (`receivedBytes`, `0`), the piece size to send
(`chunkBytes`, 8 MiB) and `expiresAt`. A staged transfer has no destination of its own — where the
file ends up is the upload's to say — so naming a cabinet, folder, attachment, batch or club beside
`stageForSync` is refused with `400`.

Send the bytes in order, each piece as the raw request body:

```
PUT /api/v1/files/uploads/{id}?offset=0
Content-Type: application/octet-stream
```

Each answer says how far the file has got. A piece the server already holds is answered `200` again,
so a piece whose answer was lost can simply be sent again; a piece that does not continue the file is
`409 upload.chunk_out_of_order` with the offset to send from. Send from the refusal's
`expectedOffset` (an integer member beside `code`), or, when that answer was lost, read
`receivedBytes` from `GET /api/v1/files/uploads/{id}`, which says how far a transfer got after a
dropped connection. While the bytes are still arriving the transfer lives for 24 hours after its last
piece.

The answers of these routes and of `GET /api/v1/files/config` are recorded in the contract
(`26-files-upload-open` to `30-files-config`, and `16-errors/chunk-out-of-order`), and their members,
as recorded there, are part of contract version 1.

When every byte has arrived:

```
POST /api/v1/files/uploads/{id}/complete
```

```json
{ "id": "0199…", "fileName": "IMG_0001.jpg", "sizeBytes": 2483112,
  "sha256": "9f2c…", "mimeType": "image/jpeg", "kind": "image",
  "stagedAt": "2026-10-25T09:30:00Z", "expiresAt": "2026-11-01T09:30:00Z" }
```

**Completing makes no document.** The server hashes the bytes (`sha256`, lower-case hex), types them
by their content rather than their name (`mimeType`, `kind`), and keeps them waiting under the
transfer's `id` — the **staged upload** a document row then names as `stagedUploadId`. Compare
`sha256` with the phone's own hash of the file: the row must state the same one. Completing again
answers the same thing and does not move `expiresAt`, so a lost answer costs one request.

A staged upload waits until `expiresAt` — **the installation's staging retention** after it was
staged (seven days unless the administrator chose otherwise on the settings page, *Sync*). One that no
upload has adopted by then is deleted with its bytes, and the transfer's routes answer `404
upload.session_not_found` for it from that moment, before the sweep has even run: send the bytes
again. `GET /api/v1/files/uploads/{id}` on a staged upload answers `stagedForSync: true`, its
`stagedAt` and `sha256` — the way to ask whether the bytes are still there before sending the row
that needs them. `DELETE /api/v1/files/uploads/{id}` withdraws a staged upload and its bytes.

Staging needs an account that may add documents — anywhere, or in one of its own caving groups.
Whether it may add this document under this set's group is decided when a row adopts the bytes.
**Staged bytes count against the account's storage room from the moment the transfer opens**, by the
size it was opened with, until a row adopts them (they then count as the document they became) or
they are withdrawn or swept. `GET /api/v1/files/config` reports the room left, and an open that would
not fit is refused before a byte moves (`409 file.quota_exceeded`). The same transfer limits apply as
for any upload: the largest file the installation accepts, and the types it refuses.

A staged upload is the account's own. Another account — an administrator included — is answered
`404 upload.session_not_found` for it on every route, the same answer as for an id that never
existed, so that a transfer id says nothing about who is sending what.

#### Sending the row

The row travels in the upload's `documents` array, beside `rows` and `people`, and is applied after
them — so a document row and the feature rows sent with it land in one batch, one transaction and one
undo:

```json
{ "batchId": "0199…", "contractVersion": 1,
  "documents": [
    { "id": "0199…", "baseRevision": null, "deleted": false,
      "title": "Shaft entrance", "description": "Looking down the shaft",
      "fileName": "IMG_0001.jpg", "sha256": "9f2c…", "sizeBytes": 2483112,
      "stagedUploadId": "0199…", "capturedAt": "2026-07-14T10:30:00Z",
      "createdByDeviceUserId": null, "clientUpdatedAt": "2026-07-14T10:31:12Z",
      "baseVersionId": null } ] }
```

Every row that is not a removal states `sha256`, the lower-case hex hash of the content the document
has on the phone — except an edit that names its `baseVersionId` and brings no staged bytes, which
may leave it out. `fileName` is at most 255 characters, the width a stored file's name is kept in;
a staged transfer opened with `stageForSync: true` is held to the same width when it opens. Each is answered in the answer's `documents` array, in the per-row shape the feature
rows use, with the document's new `revision` when it was written; the array is absent when the upload
carried none. A resend of the same `batchId` is answered from the record and writes nothing.

- **A new `id`** becomes a document under that identifier — adopted as a feature row's is — whose
  first revision is the staged bytes `stagedUploadId` names. The server re-reads them as it reads every
  upload: their type from the content, the camera's position, direction and time from the file. The
  staged upload's hash must be the row's `sha256` (and its size the row's `sizeBytes`, when stated),
  otherwise `rejected sync.file_hash_mismatch`; no staged upload this account still holds is
  `rejected sync.file_bytes_missing`. Adopted, the staged upload is gone — its bytes are the
  document's now — and a later row cannot name it again.
- **The document is owned by the account that uploaded it**, bound to the selection's caving group,
  filed on no shelf, and given the visibility a plain upload with no destination gets — private —
  until somebody changes it in the web interface or hangs it on a feature. Adding a document needs the right to add one under that
  group (`access.create_forbidden`, `sync.caving_group_forbidden` otherwise).
- **An `id` the server has** is arbitrated on `baseRevision`, which is the document's own revision,
  exactly as a feature row is: a stale one is `conflict sync.conflict` with the revision that won, and
  a row without one is a create resent after its answer was lost, answered `unchanged`. The revision
  moves on every change to what the catalogue says about the document, wherever it was made — a new
  revision of its bytes or a caption written in the web interface included — so a phone that read the
  document before such a change is answered `conflict` rather than writing over it.
- **Whether the row brings new bytes** is said by `baseVersionId` — the catalogue's
  `document.versionId` for the bytes the phone holds. **Send it on every edit.** A phone handed a copy
  without location data, or only a rendition (`originalReach` `locationFree`, `renditions` or `none`),
  holds a file whose hash was never the stored one and is not told the stored one, so the hash alone
  cannot tell its caption edit from new content. With `baseVersionId`:
  - a row naming **no `stagedUploadId`** changes only what is said about the file — its title and,
    for a photo, its caption — and is `unchanged` if neither differs; `sha256` is not consulted;
  - a row naming a **`stagedUploadId`** whose bytes differ from the stored file's makes them a **new
    revision**: they become what the document serves, everything hanging on it follows them, and the
    earlier revision stays in the document's history, readable by whoever may edit it. Staged bytes
    identical to the stored file's are an edit of what is said, and leave the staged upload waiting;
  - a `baseVersionId` that is **not the current revision** — somebody gave the document new bytes
    since — is `conflict sync.conflict` with the document's revision: read it again.

  Without `baseVersionId` the hash decides, which is right only for a phone holding the stored file
  itself (`originalReach: original`): **the same `sha256` as the current revision** is an edit of
  what is said, **a different one** is a new revision, and new bytes without a staged upload are
  `rejected sync.file_bytes_missing`.
- **`deleted: true`** removes the document. That is a right of its own — in practice the document's
  owner's and an administrator's; an account that may only edit it is answered `rejected
  sync.row_delete_forbidden`, and should take the document off its feature instead. Removing a
  document the server does not have, or has already removed, is `unchanged`; editing one it has
  removed is `rejected sync.row_deleted`.

`description` is a photo's caption (the catalogue's `document.description`); it is ignored for a file
that is not a picture. An absent member says nothing — the caption stands — and an empty one clears it.
`title`, absent or blank, keeps the name the document has; on a create the file name stands in.
`capturedAt` is kept only when the file itself states no capture time, because the file is the better
witness. `createdByDeviceUserId` is kept on the document a create makes, as the phone user's
identifier and never as a caver, and the catalogue gives it back (section 11.1); a photo that names
no photographer is shown as taken by the caver that phone user is linked to (section 12), wherever a
photographer is shown or filtered on. An edit does not move it. A phone user the phone never described
is recorded nameless when the document is written (section 12). `clientUpdatedAt` is informational.

#### Hanging a document on a feature

Where a document hangs travels in the upload's `documentLinks` array, applied after `documents`, so a
link may name a document sent in the same upload:

```json
{ "batchId": "0199…", "contractVersion": 1,
  "documentLinks": [
    { "documentId": "0199…", "target": { "kind": "feature", "id": "0199…" },
      "role": null, "caption": null, "sortOrder": null, "deleted": false } ] }
```

A link is named by its **document and its target together** — a document hangs on an object once, and
the phone keeps no identifier of its own for a link. Each is answered in the answer's `documentLinks`
array, **in the order sent**, in the per-row shape, under **the link's identifier** — the one the
catalogue (§11.1) names the link by and later reports its removal under — or, for a link refused
before it existed, under its document's. The array is absent when the upload carried none.

- **A new link** hangs the document's current file on the target. Without a `role`, the role follows
  from the file and the target: a photo on an entrance is `photoEntrance`, on a cave place or a cave
  area `photoInterior`, on a cave as a whole (or anything else) `other`; a file that is not a photo is
  `document` wherever it hangs. A link already there is answered `unchanged`.
- **An existing link** takes the row's `role`, `caption` (the link's own — what the file is called on
  that feature, not the photo's description) and `sortOrder`; an absent member leaves the link's as
  it is. Nothing different is `unchanged`.
- **`deleted: true`** takes the document down from the target; the document itself stays. A link
  that is not there is `unchanged`.

Hanging a document or taking it down needs **Write on the target** (`rejected sync.row_forbidden`),
and hanging it needs a document the account may read (`rejected sync.document_unavailable` — send the
document first). The target must be **in the selection** and readable (`rejected
sync.target_not_found`), and **not withheld from the account** (`rejected sync.location_forbidden`):
a link on a cave whose location the account may not see is a row the catalogue would never have
handed it, because a file hanging there says where to look. Both are answered by the same rule that
decides what the catalogue hands the account, so a link is accepted exactly where the catalogue would
list it. A feature the same upload created in its `rows` is exempt from both, as it is for the
feature rows; a document sent under the same identifier as a feature is not a feature, and exempts
nothing. Three target kinds are accepted — `feature`, `tripRecording` and `tripPoint`
(next section); any other kind is `rejected sync.kind_unsupported`.

#### Photos on a recording and on a scan

A phone's photos taken on a trip hang on the **recording** (`target.kind` `tripRecording`, `id` the
recording's identifier) or on one **scan** within it (`tripPoint`, `id` the scan's
identifier). They are sent exactly as any link is — in `documentLinks`, after `documents`,
`tripRecordings` and `tripPoints`, so a link may name a recording, a scan and a document sent in the
same upload:

```json
{ "documentId": "0199…", "target": { "kind": "tripPoint", "id": "0199…" },
  "role": null, "caption": null, "sortOrder": null, "deleted": false }
```

- **Role.** Without a `role`, a photo on a recording or a scan is `other` (where it hangs on a trip
  says nothing about what it shows), and a file that is not a photo is `document`.
- **What is checked.** The recording or scan must exist and be readable by the account, otherwise
  `rejected sync.target_not_found` — answered alike for one that is absent, removed, or somebody else's,
  so a link cannot be used to ask what exists. The recording's **cave** must be in the selection and not
  withheld from the account, and for a scan so must its **place** (`rejected sync.location_forbidden`
  for a withheld one): a photo on a scan of a place the catalogue would never hand the account is a row
  it may not create. A cave or place the same upload created in its `rows` is exempt, as for a feature
  link. Then Write on the recording (`rejected sync.row_forbidden`): a scan is changed under the right
  to change its recording.
- **What the catalogue hands out.** The documents stream (§11.1) lists these links in the same pages as
  the links on features, with `target.kind` `tripRecording` or `tripPoint` and the target's
  identifier. A link on a recording is listed when the recording is readable by the caller and its cave
  is handed to them; a link on a scan, when its place is handed as well — a scan that names no place
  carries only the recording's conditions. A photo on a scan of a withheld place is **absent**, not
  stubbed, exactly as the trips stream leaves that scan out. When the recording or scan is removed, or
  the link is taken down, the link arrives as a tombstone under its own identifier.
- **Position in the picture.** A photo on a recording is placed by the recording's cave, and a photo on
  a scan by its cave and its place: the original file is handed only to a reader who may be told where
  those are (the reach rule of §11.1), and everyone else is given the picture with its location data
  taken out.
- **Conversion.** When the server makes a trip report from a recording (§13.3) every photo hanging on the
  recording or on one of its scans is hung on the report too, once each, the recording's own first. The
  files are not copied. A photo added to a recording after its report was made does not reach the report:
  the report is not rewritten.
- **Undo.** The upload's undo (below) takes a link it hung off a recording or scan, including one on a
  recording the same undo removes, and hangs a link it took down again — unless the recording or scan is
  gone, which is `not_undone` / `container_deleted`.

#### Undoing an upload that sent files

The upload's undo (§8, `POST /import-batches/{id}/revert`) takes back the photo sections too, one line
per document and one per link, newest first, by the rule the feature rows follow — a value goes back
only while it still holds what the upload left (`force` overrides that), and each line answers its
`documentId`:

- a document the upload **created** is removed (into the bin, restorable from the web interface), and
  the catalogue reports its links gone;
- **new bytes** it gave a document step back: the revision before them is current again, everything
  hanging on the document follows it back, and the stepped-back revision stays in the version list
  (`applied` names `file`). If somebody has given the document newer bytes since, the step is left
  and reported `superseded` — even when forced, because stepping back past a later revision would
  discard it;
- a **title or caption** it changed goes back;
- a document it **removed** comes back, for an account that may remove it;
- a **link it hung** is taken down, and a **link it took down** is hung again as it was — role,
  caption, place and the phones' default-download mark — on the document's current file, under a
  new identifier (the old one has already been reported removed). Both need Write on the target (for a
  recording or a scan, on the recording, whether or not it has since been removed), and
  hanging a link again needs a document the account undoing may still read (`forbidden` otherwise),
  because a link hands the document to every reader of the target.

The phone learns the undone state through its ordinary cursors: every document and link the undo
touches moves its revision.

## 12. People

A phone has users of its own — the people sharing it on a trip — each with an identifier the phone
minted (`users.uuid`). They are not accounts here and never become accounts or cavers by being seen.
What the server keeps is **one record per phone user**: the names the phone last sent and, once
somebody has said so, which caver they are. Everything that later needs to say who on a phone did
something resolves the phone's identifier through that record, and so does the archive import's
review, so a phone user mapped once — on the phone, in the browser or while importing an archive —
is not asked about again.

A phone user the phone **names without describing** — as who added a photo or a document, started a
recording or made a scan (`createdByDeviceUserId`), with no `people` item for it ever — is recorded
**at once, nameless**, when the row naming it is written, so somebody keeping the roster sees it
waiting and can link it to a caver. A later `people` item fills its names into the same record. Naming
a phone user that is already on record, whoever described it, changes nothing about it.

This part is announced as `people` in `features`, **only while the installation offers it** (the
*people directory* switch, on by default) **and the account may be told who people are**. Read it
from each capabilities answer.

### 12.1 The directory

```
GET /api/v1/sync/people
Authorization: Bearer <token>
```

```json
{
  "people": [
    { "id": "…", "fullName": "Ana Pop", "shortName": "Ana", "hasAccount": true, "isMe": true }
  ],
  "links": [
    { "deviceUserId": "…", "caverId": "…" }
  ]
}
```

- **`people`** are the cavers a phone may say one of its users is — by default the members of any
  caving group the account belongs to and the account's own caver; an installation may list the whole
  roster instead. **Names only**: no e-mail address, telephone number or roster remark ever travels
  here. `id` is the caver identifier an upload names; `fullName` is the name the account is shown for
  that person anywhere else (an account holder's own label wins over the roster's name).
- **`links`** is the current link of every phone user this account has sent, whoever made it, with
  `caverId` null for one nobody has mapped. It is how a phone learns of a mapping made in the browser:
  replace the phone's own choice with what `links` says.
- Switched off, the route answers `409 sync.feature_disabled`; an account that may not be told who
  people are gets `403 access.forbidden`. Neither is a reason to retry.
- The same listing decides whether a recording or a scan on the trips stream names the caver who
  recorded it (`createdByCaverId`, section 13.1): only a caver `people` lists for this account, and
  nobody while the directory is switched off.

### 12.2 The `people` section of an upload

An upload may carry a `people` array beside `rows`; it is applied **after** `rows` and answered in a
`people` array of the same per-item shape (`id` is the `deviceUserId`; `revision` is always null —
people are not arbitrated on a revision). An upload without the array is answered without it.

```json
"people": [
  { "deviceUserId": "…", "username": "ana", "firstName": "Ana", "lastName": "Pop", "caverId": "…" }
]
```

- `deviceUserId` and `username` are required; a phone user appears at most once per upload.
- **`caverId` is optional and absent means "nothing to say about the link"**: an existing link is left
  as it stands. There is no way to unlink from a phone; that is done in the browser, by somebody
  keeping the roster (`PUT` and `DELETE /api/v1/phone-users/{deviceUserId}/link`, web routes a
  device does not call).
- A `caverId` must be one the directory lists for this account, or the item is `rejected` with
  `sync.person_not_listed` and nothing about that phone user is written. While the directory is
  switched off every `caverId` is refused with `sync.feature_disabled`; a phone user sent without one
  is still recorded.
- **A link somebody made in the browser is not replaced by a phone.** The item is still applied — its
  names are taken — and answered `updated` or `unchanged`; the directory's `links` then says which
  caver stands. A link the phone made earlier is replaced by its new choice.
- A phone user sent again exactly as recorded is `unchanged`, and nothing is added to the history.
  One the server recorded nameless, because something an upload sent named it first, is `updated`
  when the phone first describes it.
- Undoing the upload takes back what it did to each phone user: a link it made is removed, names it
  changed go back. A phone user it described for the first time stays recorded, without the link. A
  value somebody changed since is left, as for rows — and a link is changed since when its caver or
  who made it is: a link the roster's keeper has confirmed is theirs, and stays. A link a forced undo
  puts back is put back as the phone's.

`uploadRowsMax` counts the items of every section together — `rows`, `people`, `documents`,
`documentLinks`, `rasterMaps`, `tripRecordings`, `tripPoints` and `rasterPins` (sections 11, 13 and 15).

## 13. Trips

A phone records a trip as **a recording** (`cave_trips`): a cave, a start and an end, a title, a
description and a running log, and **a scan** (`cave_trip_points`) at each marked place reached. The
server keeps the recording as a recording — beside, not instead of, any trip report the club makes
from it — under the phone's own identifiers, so the same recording sent again, by this phone or
another, or arriving later inside an exported archive, is recognised as the one already here.

A recording is governed by the installation's **trip rules** (the same rules a club writes over its
trips), owned by the account that uploaded it, and bound to the caving group of the set it was
uploaded into: **visible to that club**, or private to the uploader when the set names no club.

*Announcement.* This part is announced as `trips` in `features` (section 13.5).

### 13.1 Reading recordings

```
GET /api/v1/sync/sets/{id}/trips?cursor=…&pageSize=…
Authorization: Bearer <token>
```

```json
{
  "setRevision": 4,
  "settings": { },
  "recordings": [
    {
      "id": "…", "caveId": "…", "title": "…", "description": "…", "log": "…",
      "startedAt": "2026-09-12T08:00:00+00:00", "endedAt": "2026-09-12T16:00:00+00:00",
      "createdByDeviceUserId": "…", "createdByCaverId": "…", "revision": "…",
      "points": [
        { "id": "…", "placeId": "…", "scannedAt": "…", "notes": "…", "createdByDeviceUserId": "…",
          "createdByCaverId": null, "revision": "…" }
      ]
    }
  ],
  "tombstones": [ { "id": "…", "deletedAt": "…" } ],
  "pointTombstones": [ { "id": "…", "recordingId": "…", "deletedAt": "…" } ],
  "nextCursor": "…",
  "hasMore": false
}
```

- Paged exactly as the feature stream is (section 3): an opaque cursor of its own — a cursor of
  another stream is refused `400 sync.cursor_invalid` — `409 sync.cursor_stale` when the set moved,
  `pageSize` clamped to `pageSizeMax`, both kinds of tombstone riding the same cursor.
- **Which recordings.** A recording is on a page when its cave is one the feature stream would hand
  this account right now — inside the set, readable, and **not withheld** — and the recording itself is
  readable under the trip rules. A recording in a withheld cave is **absent, not stubbed**, exactly as a
  photograph on that cave is.
- **Which scans.** `points` lists the recording's live scans in the order they were made, **leaving out
  every scan of a place the feature stream would not hand this account**. A scan carries no position
  of its own: where it was is its place's position.
- **When a recording comes again.** When it changes, when any of its scans is added, edited or removed,
  or when its cave changes (so a cave that comes into the set brings its recordings). `revision` is the
  recording's own revision, the value an upload sends back as `baseRevision`; each scan has its own.
- **Tombstones** name recordings removed after the cursor, in caves the set hands this account, that the
  account may read. A removed recording takes its scans with it; they are not named again.
- **`pointTombstones`** name scans removed after the cursor (from the start, on a first read) from
  recordings this page's rules hand the account, each with the recording it was on — and only a scan
  the account would be given were it live: one of a place the feature stream hands it now, or of no
  place. The removal of a scan of a withheld place is silent, as the scan itself is. The recording
  also comes again without the scan, so a device that ignores the array works as before.
- **What a device does with the scans.** Remove a scan that a `pointTombstones` entry names; **keep
  any other scan that is merely missing** from `points` — it is withheld, or now stands at a place
  the account may not be given, and the device's own copy is not wrong for that.
- `deviceUuid` never travels; `createdByDeviceUserId` is the phone user's identifier (section 12).
- **`createdByCaverId`**, on a recording and on each scan, is the caver that phone user is linked
  to — given **only while the people directory is switched on and lists that caver for this account**
  (section 12.1), so the name it stands for is one the device can already show from `people`. It is
  null when the phone user is linked to nobody, is linked to a caver the directory does not list for
  this account, or when the directory is off. A recording made on another account's phone names its
  recorder this way too; its `createdByDeviceUserId` is not among this account's `links`. A link
  made or changed later does not by itself bring the recording again: the device reads the new value
  at the recording's next change, and resolves `createdByDeviceUserId` through `links` meanwhile
  where it can.

### 13.2 The `tripRecordings` and `tripPoints` sections of an upload

An upload may carry `tripRecordings` and `tripPoints` arrays. They are applied **after** `rows` and
`people`, recordings before scans — so a scan sent beside its new recording finds it, and a recording
in a cave sent in `rows` of the same upload finds the cave — and each is answered in an array of the
same name in the per-item shape of `rows` (`id`, `status`, `revision`, `code`, `detail`). An upload
without the arrays is answered without them.

```json
"tripRecordings": [
  { "id": "…", "baseRevision": null, "deleted": false, "caveId": "…", "title": "…",
    "description": "…", "log": "…", "startedAt": "…", "endedAt": "…",
    "createdByDeviceUserId": "…", "clientUpdatedAt": "…" }
],
"tripPoints": [
  { "id": "…", "recordingId": "…", "baseRevision": null, "deleted": false, "placeId": "…",
    "scannedAt": "…", "notes": "…", "createdByDeviceUserId": "…" }
]
```

- **Identity, arbitration and resend** are the rows' (section 8): the identifier is adopted verbatim, a
  create whose identifier is here answers `unchanged` with the current `revision`, an edit or a removal
  is written only against the current `baseRevision` (`conflict` otherwise, with the server's revision
  and no echo — read the recording again from the stream), and a removal of something never received
  is `unchanged`. A recording or a scan appears at most once per upload.
- A recording to be written needs `caveId` and `startedAt`; a scan needs `recordingId` and `scannedAt`.
  A removal needs only `id`, `baseRevision` and `deleted: true` (and a scan its `recordingId`). Missing
  either refuses the whole upload with `400`, before anything is written.
- **The cave** must be one the account may read; one that is not here and one it may not read are both
  `sync.cave_not_found`. A cave the account may read but not place exactly is accepted — a recording
  carries no position. Adding a recording needs the right to add trips (bound to the set's club when the
  set names one): `access.create_forbidden` or `sync.caving_group_forbidden` otherwise.
- **A scan is part of its recording**: adding, editing or removing one needs the right to edit the
  recording (`sync.row_forbidden`), and a recording that is not here, may not be read or was removed is
  `sync.recording_not_found`. A scan belongs to one recording for good; its identifier sent under another
  recording is `sync.id_conflict`. Its `placeId` must be a place the account may read
  (`sync.place_not_found` otherwise, alike for absent and unreadable).
- Editing a recording needs the right to edit it, removing it the right to remove it
  (`sync.row_forbidden`, `sync.row_delete_forbidden`), as for rows. A removed recording answers
  `sync.row_deleted` to an edit, as a removed row does.
- `clientUpdatedAt` is kept as provenance and never compared.
- Every recording and scan an upload wrote is a line of the upload's import batch.

### 13.3 What the server makes of a recording

Once an upload that wrote or changed recordings (or their scans) has been applied, the server makes
**trip reports** from them in the background — nothing in the upload's answer waits for it, and the
phone is not asked anything.

- **A recording still open** (no `endedAt`) is left as it is. The upload that closes it makes the trip.
- **A recording that settles** becomes a finished trip report owned by the uploader, in the recording's
  club and audience, named after the recording (or its first day), linked to its cave, with the people
  the recording's phone users are mapped to (section 12) on its roster, and **one position per scan** at
  the survey station the scan's place stands for. A scan **settles** when its place is a marked place the
  uploader may read, the place's depth (`speleolocDepthInCave`) points at **exactly one** nearest station
  of the cave's current survey — a tie between two stations at the same distance, a place with no depth,
  a cave with no survey or a survey the uploader may not see all leave the scan unsettled — and its
  phone user (or, when the scan names none, the recording's) is mapped to a person.
- **A recording that does not fully settle** is, by default, still made a trip report with the scans
  that did settle, and the trip is **flagged as needing attention**, naming each reason and the scans it
  covers (`place_unknown`, `unresolved_station`, `unmapped_person`, `recorded_in_future`). The people
  who look after phone uploads and the uploader are notified. An administrator may switch this off
  (*convert trips with problems*): the recording is then left as a recording, and the same people are
  told it is waiting.
- **A recording already made into a trip that changes afterwards** — edited, given a scan, a scan
  changed or deleted, or the recording itself deleted by the phone — does not rewrite the trip: the trip
  is flagged with `recording_changed_after_conversion` and the same people are told.
- **A recording left waiting** — switched off as above, or not made because the uploader may no longer
  make the trip — becomes a trip **by hand**, on the archive import's review screen reading the recording
  instead of an archive: `POST /api/v1/speleoloc-imports/recordings/{recordingId}/preview` and `/commit`
  take the archive import's own requests and answer with its own answers, through the same resolver, so a
  scan is proposed the same stations whichever way the recording came. The person reviewing needs to be
  able to read the recording and to change it (making it a report marks it), and the usual rights to
  write onto the trip or create one. There is no saved review for a recording; the confirmation carries
  the decisions. Undoing that import takes the mark off again.
- A recording becomes **one** trip report, whichever way it is made; positions made this way are
  withheld from exactly the readers a typed position on that cave is withheld from.

### 13.4 The same recording by two paths, and taking an upload back

**The recording's identifier is its identity on both paths.** A recording a phone sends and the same
recording read out of an exported archive (`speleo_loc.sqlite` in a SpeleoLoc export, imported from the
browser) are one row on the server. The archive import keeps the recording too — creating it when the
server has never seen it, or marking the one a phone already sent — and marks it as made into the trip
the import wrote onto. From then on:

- an archive import of a recording that **already has its trip report** — made from a phone upload or by
  an earlier import — is refused, on the dry run and on the confirmation alike, with `409`
  `import.recording_already_converted`; the answer's `tripLogId` names the report when the person may
  read it, and is `null` when they may not;
- a phone upload of a recording an archive import already made into a report makes **no second
  report**: the scans it adds flag that report with `recording_changed_after_conversion`, as any change
  after conversion does;
- a recording the server already holds from a phone is kept as the phone sent it; the import only marks
  it, and only for a person who may change that recording (and, for one deleted here, delete it — the
  import brings it back). Anybody else is refused with `409` `import.recording_not_writable`, and nothing
  is written. Undoing the import takes the mark off, so the recording can become a report again; a
  recording the import brought in (or back from deleted) is deleted again, which the trips stream tells
  the phones;
- a recording the archive import brings in is the importer's, with an audience never wider than the
  report's: the report's club when the report is visible to the club (or wider) and the importer may add
  trips to that club and bind them to it — so the club's phones receive it — and private to the importer
  otherwise.

One trip may hold several recordings — two phones of one party, each imported onto the trip the party
planned. What is refused is one recording becoming two reports.

**Undoing a phone upload** (the upload's undo, §8) takes back the recordings and scans the upload
created — deleted, so the trips stream sends their tombstones — and, with a recording, the trip report
a conversion of that recording's uploads made from it **when nobody has touched the report since**: any
change to the trip, its people, its positions or its flag by anything other than that conversion means
somebody worked on it, and it is left. (The conversion may be a later upload's — the one that closed a
recording sent while the trip was under way; undoing either upload takes the untouched report back.)
What an upload **changed or deleted** in a recording or a scan is put back out of the history, as a
feature's change is: a deleted one comes back (its write time moved, so the trips stream re-sends it),
and each changed field returns to its earlier value while it still holds what the upload wrote; a field
somebody changed since is left (`superseded`). Undoing the change that closed a recording, when that
upload's conversion made the report and nobody touched it, also deletes the report and takes the mark
off the recording, which is open again. The undo's line for the recording names the report
(`tripLogId`, `null` for a caller who may not read it) and says what became of it (`tripOutcome`:
`undone` or `not_undone`; `tripReason`: `trip_changed`, or `converted_elsewhere` for a report an archive
import made). The phone that sent the change still holds it, and a later upload of it is a change
again.

### 13.5 Announcement

`features` in `GET /sync/capabilities` names **`trips`** on every installation running this build: the
trips stream and the two upload sections are served. A device that does not see the name does not send
the sections.

## 14. When an account's uploads are reviewed

An installation can place an account under review: everything it uploads waits for a person who
may decide about uploads, and nothing of it is written until they have. Section 8 says what such an
upload is answered (`held`, with a `reviewId`) and what a device must send for it (`held` in
`clientFeatures`). This section is what happens afterwards. Edits made in the browser are never held.

**File imports by such an account are held too.** A vector file, photographs, a catalogue selection,
a trip spreadsheet, a SpeleoLoc archive and a kept recording are previewed and chosen on the account's
own screen as before; pressing commit is what waits. The confirmation route makes every check it makes
for anybody else — what the commit refuses of the selection itself included: a selection over the
source's ceiling (`*.selection_too_large`), one that sets every candidate aside (`*.nothing_created`,
`400`, for every source; unheld, a vector, photo or catalogue confirmation that sets everything aside
lands as an empty batch, which held would leave a reviewer nothing to decide), and a catalogue
selection on an installation that cannot reach the catalogue at all (`503 speologie.not_configured`) —
then answers that the import waits instead of writing it — `held: true` on the
answer (the photo answer's batch has `reviewState: pending`; the vector answer is `202` with no
`jobId`; a SpeleoLoc answer for a trip it would create has no `tripLogId` yet) — and keeps the request
on a waiting batch, with one waiting line per candidate: a row of the file, a group of pictures, a
catalogue cave, a sheet row, a scan, and for a SpeleoLoc confirmation that creates its trip, a line for
that trip, on which every scan depends. The account's saved review and its file are left as they are.
Accepting runs **the same commit** the route runs unheld, over the kept request with every candidate
not accepted turned into a skip, with the uploader's rights at that moment (the checks the route makes
are made again, so a file deleted since, a right lost since, or a recording made into a report since
is decided `failed`); turning the batch down writes nothing and spends nothing, so the account may
confirm again. A catalogue that does not answer when a reviewer accepts leaves the lines waiting
(`503`, the catalogue's own code). The phone's reviews stream carries phone uploads only.

A reviewer accepts or turns down a held upload whole, row by row, or — for an edit — by field: of a
feature, `name`, `description`, `properties` and `position` (the point, altitude, position quality and
main-entrance flag together); of a photo or document, its `title`, its caption (`description`) and its
new `file`, taken whole; of a recording, `title`, `description`, `log`, `times` (start and end
together) and `cave`; of a scan, `notes`, `place` and `time`; of a raster map, `title`, `mapType`,
`sortOrder`, `caveArea` and its new picture (`file`). A field left out is not written — the row keeps
what it holds for it, and new bytes not taken stay unadopted, kept no longer than a turned-down row's.
A link, a pin, a phone user, a creation and a removal are accepted or turned down whole. The device
sees none of this beyond the outcome: a row accepted in part is `accepted`, and its `revision` and the
downloads give its state. Accepting writes exactly what the same upload would have
written unheld, with **the uploading account's rights as they are at that moment**: a review never
lands what the account could not write itself, so a row the account has since lost the right to write
is decided `failed`. Accepting a row accepts the held rows it depends on (a place under a held cave, a
scan of a held recording, both the held photo and the held place a link joins, a map of a held cave,
both the held map and the held place a pin joins), even when an earlier upload left them waiting; turning one down turns down every held row that depends on it.

A reviewer may change their mind about a row they turned down, while the installation still keeps a
turned-down photo's bytes (`SILEXGIS__Sync__RejectedHeldBytesRetentionDays`, 30 days by default) and as
long as no later upload sent the same row again. The row is then accepted after all, and **the same
`reviewId` appears a second time on the reviews stream**, with the later decision: a device keeps the
last outcome it read for a `reviewId`, not the first.

### 14.1 Reading the decisions

**`GET /api/v1/sync/sets/{id}/reviews?cursor=&pageSize=`** — the decisions on rows this account's
phones sent while it was under review, oldest decision first, a page at a time, on the same cursor
rules as every other stream (section 3): the cursor is this stream's own, a set edit makes it stale,
and a caught-up page echoes the position it was asked from. The stream is the **account's**: it
carries decisions on rows sent through any of the account's sets, because an upload's identity
belongs to the account, not to the set it went through. An account that was never under review reads
an empty stream.

```json
{
  "setRevision": 4,
  "settings": { },
  "reviews": [
    {
      "reviewId": "0199f0c2-…",
      "entity": "rows",
      "rowId": "0199f0a1-…",
      "outcome": "rejected",
      "decidedAt": "2026-10-26T09:12:44.512Z",
      "note": "That is the old shaft",
      "revision": "2026-10-20T17:03:10.118Z",
      "current": { "id": "0199f0a1-…", "kind": "caveEntrance", "…": "…" }
    }
  ],
  "nextCursor": "…",
  "hasMore": false
}
```

| Member | Meaning |
|---|---|
| `reviewId` | The id the upload answered the row with. |
| `entity` | The upload section the row came in: `rows`, `people`, `documents`, `documentLinks`, `tripRecordings`, `tripPoints`, `rasterMaps` or `rasterPins`. |
| `rowId` | The row's id as the upload answered it; for a document link, the document's id. |
| `outcome` | `accepted` — written, whole or in part; `rejected` — turned down, nothing written; `superseded` — the device sent the same row again with other content, which waits in its place under a new `reviewId` (decided when that upload arrived, with no reviewer); `failed` — accepted, but refused when it was written. |
| `decidedAt` | When. The stream's order. |
| `note` | What the reviewer wrote to the uploader; absent when nothing. |
| `revision` | The revision the server holds the row at now, when it holds one this account may read. After `accepted` it is the row as written — store it as the row's base revision. Absent for a new row that was not written. |
| `code` | For `failed` only: why the row could not be written — the code the upload itself would have answered (section 6.2 of the errors document), `review.dependency_failed` when a row it depends on was not written, or `review.proposal_unreadable` when the kept proposal could no longer be read. |
| `partial` | On an `accepted` **edit** of which the reviewer wrote only some of the proposed fields: `true`. Absent when the whole edit was written, and on every other outcome. |
| `current` | For a `rejected` or `failed` **edit or removal**, and for an edit `accepted` with `partial`, of a row of any section but `people`: the server's row, shaped exactly as that section's own stream delivers it, so the device can put back what it still shows — for `rows` a feature of the feature download; for `documents` the catalogue's `document`; for `documentLinks` a catalogue link (when the document hangs there more than once, the link the upload named, else the first by identifier); for `tripRecordings` a recording of the trips stream with its `points`; for `tripPoints` one entry of a recording's `points`; for `rasterMaps` a map of the rasters stream with its `pins`; for `rasterPins` one entry of a map's `pins`. **Absent whenever that stream would not hand this account the row now** — its cave, place or target withheld (the conflict echo's rule, section 8.6: absence and never a blurred stand-in), unreadable, or gone — whichever of the account's sets the row was sent through. `people` carries none: a phone user's link is read from `GET /sync/people` `links`. |

What a device does with each outcome:

- **`accepted`** — nothing to undo. The written row also reaches the device through its own stream as
  any written row does; `revision` saves waiting for it. **With `partial: true`** the device's version
  of the fields that were not written is not the server's: replace the row with `current`, or, when
  `current` is absent, with what that section's stream hands when it is read again from the
  beginning — the row did not change after the revision the device now holds, so an incremental read
  would not bring it again.
- **`rejected`** — the device's version will not be written unless a reviewer later accepts it after
  all (which arrives as a later `accepted` for the same `reviewId`). A new row is dropped locally (or kept
  as a local draft, the app's choice); an edit or removal is replaced by `current`, or, when `current`
  is absent, by what **that section's stream** hands when it is read again **from the beginning** —
  drop that stream's cursor, not the others'. The server's row did not change, so an incremental read
  from the device's position would not bring it again.
- **`superseded`** — ignore; the later `reviewId` will be decided in its turn.
- **`failed`** — as `rejected`, and worth telling the caver: the account may have lost a right.

### 14.2 Announcement

`features` in `GET /sync/capabilities` names **`reviews`** on every installation running this build:
the reviews stream is served, and an upload from an account under review is answered `held` to a device
that lists `held` in `clientFeatures`. Whether a given account is under review is not announced — it
can change at any time; a device learns it from its first `held` answer, or from
`409 sync.client_update_required` if it did not list `held`.

## 15. Raster maps

A phone keeps **raster maps** of a cave — a scanned plan, a drawn profile — and the places **pinned**
on them by pixel. The server keeps a map as a map, under the phone's own identifier, and its picture as
a document; a pin is a place, a pixel across and a pixel down. Neither carries a position, and there is
no georeference.

A map is as visible as its cave: it is on a phone's page exactly when the feature stream hands that
phone the cave. Who may write a map is whoever may change the cave.

### 15.1 Reading maps

```
GET /api/v1/sync/sets/{id}/rasters?cursor=…&pageSize=…
Authorization: Bearer <token>
```

```json
{
  "setRevision": 4,
  "settings": { },
  "maps": [
    {
      "id": "…", "caveId": "…", "caveAreaId": "…", "title": "…", "mapType": "planView",
      "sortOrder": 0, "documentId": "…", "pictureWidth": 4000, "pictureHeight": 3000,
      "pictureMirrored": false, "pictureTurn": 90, "revision": "…",
      "pins": [ { "id": "…", "placeId": "…", "x": 120, "y": 80, "revision": "…" } ]
    }
  ],
  "tombstones": [ { "id": "…", "deletedAt": "…" } ],
  "pinTombstones": [ { "id": "…", "mapId": "…", "deletedAt": "…" } ],
  "nextCursor": "…",
  "hasMore": false
}
```

- Paged exactly as the feature stream is (section 3): an opaque cursor of its own — a cursor of
  another stream is refused `400 sync.cursor_invalid` — `409 sync.cursor_stale` when the set moved,
  `pageSize` clamped to `pageSizeMax`, both kinds of tombstone riding the same cursor.
- **Which maps.** A map is on a page when its cave is one the feature stream would hand this account
  right now — inside the set, readable, and **not withheld**. A map of a withheld cave is **absent, not
  stubbed**, exactly as a photograph on that cave is.
- **Which pins.** `pins` lists the map's live pins, **leaving out every pin of a place the feature
  stream would not hand this account**. On a picture that may one day be laid on the ground a pixel
  is a position, so no route shows a pin of a place its reader may not be given.
- **When a map comes again.** When it changes, when any of its pins is added, moved or removed, when
  its picture is turned in the browser (which changes `pictureTurn`), or when its cave changes. `revision` is the map's own revision, the value an upload sends back as
  `baseRevision`; each pin has its own. A pin added or removed does not move the map's own `revision`.
- **`mapType`** is `planView`, `projectedProfile`, `extendedProfile` or `other` (field mapping, section 10).
- **The picture** is `documentId`, a document of the documents stream: it is listed there as a link of
  the document to the cave with the role `map2d`, and its bytes are fetched like any photograph's — a
  reader who may not be shown where the picture was taken is handed it whole without its location data.
- **`pictureWidth`** and **`pictureHeight`** are the pixel size of the map's current picture as
  stored — the frame `x` and `y` are measured in. They follow a new picture revision, and the map
  comes again when it gets one. Null only when the picture's size could not be read.
- **Renditions are drawn upright**, by the picture's own orientation code and then by any turn
  recorded in the browser, and the code may mirror the picture. **`pictureMirrored`** and
  **`pictureTurn`** say exactly how a rendition is drawn from the stored frame, in a fixed order:
  **flip left to right first** when `pictureMirrored` is `true` (orientation codes 2, 4, 5 and 7),
  **then turn clockwise** by `pictureTurn` — `0`, `90`, `180` or `270`, the code's own turn plus the
  recorded turns. Both are null exactly when `pictureWidth` is. The code is the one the server's
  renderer obeys, read when the picture was stored — a scanner's TIFF states it in its own tag and
  carries no EXIF, and a HEIF or AVIF picture is decoded upright already (code 1) — so a device
  never reads the orientation out of the file itself. A pin at (x, y) in a picture `W × H`:
  - after the flip (only when mirrored): x′ = W − 1 − x, y′ = y; otherwise x′ = x, y′ = y;
  - then the turn: `0` → (x′, y′); `90` → (H − 1 − y′, x′); `180` → (W − 1 − x′, H − 1 − y′);
    `270` → (y′, W − 1 − x′);
  - the turned frame is `W × H` for `0` and `180`, `H × W` for `90` and `270`. A rendition keeps that
    frame's aspect ratio, its longer side fitted to the size asked for, so scale the turned point by
    `renditionWidth / turnedWidth` (the same factor vertically).

  *Worked example.* A phone's portrait photograph of a plan, stored 4000 × 3000 with code 6:
  `pictureMirrored: false`, `pictureTurn: 90`. The pin at (120, 80) turns to
  (3000 − 1 − 80, 120) = (2919, 120) in the 3000 × 4000 upright frame; the 1200 rendition is
  900 × 1200, the factor 1200 / 4000 = 0.3, and the pin is drawn at (875.7, 36). Turned once more in
  the browser, the same map comes again with `pictureTurn: 180` and the pin at
  (4000 − 1 − 120, 3000 − 1 − 80) = (3879, 2919), in a 1200 × 900 rendition at (1163.7, 875.7).

  Pins stay in the stored frame whatever the turn: a pin sent up is still `x`/`y` in the picture as
  stored. A device that keeps renditions keeps the `pictureMirrored`/`pictureTurn` it fetched them
  under, and fetches them again when the map comes with different ones.
- **Tombstones** name maps removed after the cursor, in caves the set hands this account. A removed
  map takes its pins with it; they are not named again.
- **`pinTombstones`** name pins removed after the cursor (from the start, on a first read) from maps
  this page's rules hand the account, each with the map it was on — and only a pin the account would
  be given were it live: one of a place the feature stream hands it now. The removal of a pin of a
  withheld place is silent, as the pin itself is. The map also comes again without the pin, so a
  device that ignores the array works as before.
- **What a device does with the pins.** Remove a pin that a `pinTombstones` entry names; **keep any
  other pin that is merely missing** from `pins` — it is withheld, or now stands on a place the
  account may not be given, and the device's own copy is not wrong for that.

### 15.2 The `rasterMaps` and `rasterPins` sections of an upload

An upload may carry `rasterMaps` and `rasterPins` arrays, each answered in an array of the same name in
the per-item shape of `rows`. They are applied in the fixed order of the upload — `rows`, `people`,
`documents`, **`rasterMaps`**, `tripRecordings`, `tripPoints`, `documentLinks`, **`rasterPins`** — so a map
sent beside its cave finds it, and a pin sent beside its map and its place finds both.

**A map** — `{ id, baseRevision, deleted, caveId, caveAreaId?, title, mapType, sortOrder?, fileName?,
sha256?, sizeBytes?, stagedUploadId?, clientUpdatedAt? }`. `sha256` describes staged bytes: it is
**required with `stagedUploadId`** (an upload that stages bytes for a map without it is refused whole
with `400`) and **ignored without it**, so an edit that brings no new picture may leave it out:

- A **new** map (no `baseRevision`) brings its picture as bytes staged beforehand (section 11.3,
  `stageForSync`), named by `stagedUploadId` and stated by `sha256`. The picture has to be an image. It
  becomes a document owned by the uploader and bound to the set's club, and is hung on the cave with the
  role `map2d` under the map's title, which is how the browser lists it among the cave's maps.
- An **edit** is arbitrated on `baseRevision` as a feature is. It replaces `title`, `mapType`,
  `sortOrder` and `caveAreaId` as sent (an absent `caveAreaId` clears it). Staged bytes of another hash
  than the current picture's give the picture a **new revision**; the earlier one is kept.
- **`caveId` cannot change.** A map stays with the cave it was made for; send a removal and a new map.
- **`deleted: true`** removes the map and, with it, its picture's document; its pins go with it.
- Writing needs the right to change the cave, asked before anything is said about the bytes.

**A pin** — `{ id, rasterMapId, baseRevision, deleted, placeId, x, y }`:

- `placeId` is a place or other feature **inside the map's cave** that this account may read; one that
  does not exist, one it may not read and one in another cave are answered alike.
- `x` and `y` are whole pixels, 0 to 1 000 000.
- A map carries **one live pin per place**: a second pin of the same place on the map is refused.
- A pin belongs to its map for good: the same identifier under another map is refused as taken.

**Taking an upload back** removes the maps and pins it created — with a map, its picture's document and
the `map2d` link, which the documents stream tells as a removed link — puts back what it deleted (a map
with its picture), and returns each changed field to its earlier value while it still holds what the
upload wrote. A new picture steps back to the revision before it, which stays in the document's list.
The undo's line names `rasterMapId` or `rasterPinId`.

**From an account under review** (section 14) maps and pins are held like every other row: answered
`held` with a `reviewId`, written only when a reviewer accepts them, a new map's staged picture kept while
it waits. A reviewer who may not be given a pinned place's position is shown the held pin without its
`x` and `y`.

### 15.3 Announcement

`features` in `GET /sync/capabilities` names **`rasters`** on every installation running this build: the
rasters stream and the two upload sections are served. A device that does not see the name does not
send the sections.
