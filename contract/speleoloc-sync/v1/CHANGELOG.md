# Changes to the mobile sync contract

This file is written for one reader: somebody whose build pins a contract version and needs to
know whether it still works. Every entry therefore answers that question in its first line, and
the detail comes after.

## How a change reaches you

A device declares the version it was written against — it sends `contractVersion` on every upload,
and reads the server's own from `GET /api/v1/sync/capabilities`. Two different kinds of change can
happen to this protocol, and they reach a pinned build in two very different ways.

**A version bump breaks pinned builds, and is the only thing that does.** The number goes up only
when a client written against the previous number would get something wrong: a field that changed
meaning, a field that stopped being sent, a rule that started being enforced. When it goes up,
uploads from a build pinned to the old number are refused whole, with `409` and
`sync.contract_unsupported`, and the answer names the version the server does speak. Nothing is
half-applied. The device keeps its rows and its user sees a message telling them to update.

**Everything else is additive and reaches you silently, which is the point.** A new optional field,
a new row kind, a new named capability — a build that has never heard of it carries on unaffected,
because a client of this protocol ignores fields it does not recognise. That rule is what makes an
addition safe, and a client that rejects unknown fields converts every future addition into an
outage of its own making. Additions that a client can *use* are announced in the `features` list
from `capabilities`, so a device can ask what this installation supports rather than guessing from
its version number.

So: read the version to know whether you still work, and read `features` to know what you can do.

## What every entry states

- the date, in UTC;
- whether the contract version changed, and to what;
- **whether a build pinned to the previous version keeps working** — the line that matters;
- which recorded exchanges under this directory were written or re-written, because a difference
  in those files is the change, and everything else is description of it;
- any `sync.*` or `qr.*` code that appeared, changed status, or stopped being sent. A code is
  visible to a client even when the version does not move, and the register of them is
  `docs/speleoloc-sync/04-errors.md`.

---

## 2026-10-26 — documents, review, people, trips and raster maps recorded

**Contract version: still 1. A build pinned to version 1 keeps working.** Nothing here changes a
field, a status or a rule a version-1 build relies on; everything in it is reached only by a build
that asks for it, and is announced in `features`.

What a build can now ask for, each named in `features` while this installation serves it:
`documents` (the documents stream and the address route), `documentUpload` (the `documents` and
`documentLinks` sections of an upload), `reviews` (the decisions on held rows), `people` (the people
directory and the upload's `people` section), `trips` (the trips stream and the `tripRecordings` /
`tripPoints` sections) and `rasters` (the rasters stream and the `rasterMaps` / `rasterPins`
sections). A device declares what it understands in `clientFeatures` on an upload; the one value
that changes an answer is `held`.

The one change a version-1 build can meet without asking: **an account an administrator has marked
for review** is refused on upload with `409` and `sync.client_update_required` unless its device
declares `clientFeatures: ["held"]`. The batch is refused whole, exactly like the version refusal,
and nothing is written. A build that cannot be told a row is waiting for a reviewer is not allowed to
believe it was written. Accounts that are not marked are answered as before, with no new member.

Recorded exchanges written in this entry: `17-documents-first-page`, `18-files-links`,
`19-upload-document-and-link`, `20-upload-held`, `21-reviews-first-page`,
`22-reviews-current-withheld`, `23-people`, `24-trips-first-page`, `25-rasters-first-page`, and
three under `16-errors/`: `client-update-required`, `feature-disabled` and `file-bytes-missing`. The
last is a `200` whose row is `rejected`, and has no `status.txt`. Fixtures `07`–`16` are unchanged,
byte for byte. Replay the new ones when your build starts using the parts they show; until then they
are description, not a requirement.

Two placeholders are new in the recordings: `<sha256>` (the hash of a generated picture the suite
uploaded) and `<my-label>` (the name the suite gave the caller's account). `<token>`, already
applied, now appears in a recording for the first time.

Codes: `sync.client_update_required`, `sync.feature_disabled` and `sync.file_bytes_missing` are
recorded as bytes for the first time; with the rest of the second set of parts' codes they are in
`docs/speleoloc-sync/04-errors.md`, each with its status and what a client should do.

## 2026-08-29 — contract version 1

**Contract version: 1. Nothing is pinned to an earlier version, because there is no earlier
version.** This is the first published shape of the protocol, assembled and cross-checked as a
whole. No installation has served an earlier one and no build can be pinned to one.

The surface as of this entry:

- **Reading.** `GET /api/v1/sync/sets/{id}/download`, a page at a time, resumed with an opaque
  cursor. Deletions arrive as tombstones. A position the caller may not see exactly is absent from
  the payload entirely rather than delivered approximate, and its absence is not reported as a
  deletion.
- **Writing.** `POST /api/v1/sync/sets/{id}/upload`. The device's own identifier becomes the
  server's; a batch identifier replayed returns the first answer and writes nothing; a row whose
  `baseRevision` is stale is refused with the server's current row alongside it, so a device can
  merge without a second round-trip.
- **Capabilities.** `GET /api/v1/sync/capabilities` answers `contractVersion`, `pageSizeMax`,
  `uploadRowsMax` and `features`, which is `["download", "upload"]`. The two limits are what this
  installation is configured to allow and differ between installations; the version and the feature
  list are statements about the server's code.
- **Landing.** `GET /api/v1/public/qr/{code}` resolves a code printed on a cave label without a
  token, and says only which installation it resolves at.

Recorded exchanges written in this entry: `16-errors/cursor-invalid`, `16-errors/cursor-stale`,
`16-errors/contract-unsupported` — the first recordings of a refusal rather than an answer. They
exist because a refusal is as much of the contract as an answer is, and because the shape of a
refused batch is not the shape of a batch whose rows were individually refused: there are no
per-row results in it at all, so a client that reads `rows` without checking the status crashes on
the one exchange it is most likely to meet in the field.

A refusal is recorded with a `status.txt` beside its body, holding the status line. A successful
exchange has no such file; its status is 200 by construction.

Codes: none changed. The complete register, with the status, the condition and what a client
should do about each, is in `docs/speleoloc-sync/04-errors.md`.

`manifest.json` was added in this entry. It is how a copy of this directory in another repository
tells whether it is current: a digest per file, and one roll-up over all of them to compare first.
It covers every file here except itself — **this changelog included**, because an entry can change
without any recording changing, and that is precisely the case a copy would otherwise report itself
current through. Taking it is a second pass over the directory rather than part of the recording
run, so an entry is written first and hashed with everything else; `README.md` gives the two
commands.

## 2026-10-16 — the 401 and the 429 carry a code

**Contract version: 1, unchanged. A build pinned to it keeps working.** Two refusals that arrived
as problem documents with no `code` now carry one, and nothing else about them moved: a client that
branched on the status first, as `docs/speleoloc-sync/04-errors.md` told it to, behaves exactly as
before.

- `401` now carries `auth.unauthenticated`. Same status, same `title`, same `type`; the action is
  still `re-auth`.
- `429`, from the rate limiter in front of the QR landing route, now carries `rate.limited`. Same
  status and `title`; the action is still `retry` after a wait.

Recorded exchanges written in this entry: none — neither refusal is recorded here.

Codes: `auth.unauthenticated` and `rate.limited` appeared. They are written by the server itself
when an answer would otherwise leave without a code, so every refusal of this API now carries one.

## 2026-10-23 — a cave row names its type

**Contract version: 1, unchanged. A build pinned to it keeps working.** A row of kind `cave` now
carries `featureTypeCode: "cave"` where it carried `null`, and a `propertiesSchemaVersion` once its
property document has been checked against that type's schema. Both are fields a client already
receives on every other row; a client that reads a row by its `kind` first, as this protocol has
always asked, sees no difference.

Why: every cave now carries one feature type, bound to the cave kind, so that a club can give caves
fields of its own that are validated and filterable like any other kind's. The schema of that type
declares the keys a device writes on a cave (`speleolocCaveLocalIndex`, `speleolocSchemaVersion`)
with the types the cave-place schema already gives them, and stays open to keys it does not list.

One thing a client must not do: send `featureTypeCode: "cave"` on a row of kind `generic`. That row
is refused with `sync.type_unknown`, the same as any type this installation does not have for
generic rows; caves arrive as caves and are given their type on creation.

Recorded exchanges re-written in this entry: `07-download-first-page`,
`10-download-protected-withheld` — the cave row in each now shows the two fields above.

Codes: none changed.

## 2026-10-24 — a locked account can no longer refresh, and a server restart no longer signs devices out

**Contract version: 1, unchanged. A build pinned to it keeps working.** No sync route, field or
`sync.*` / `qr.*` code changed. Two changes on the sign-in side reach a device; both are described in
`docs/speleoloc-sync/03-auth.md` §5 and §6.

- **A locked account's refresh is refused.** `POST /connect/token` answers `400 invalid_grant` with
  the description "The account is locked." while the lock lasts. Before, a lock stopped new sign-ins
  only and an existing session kept renewing itself. A lock revokes nothing: the same refresh token
  works again once it lifts, so a client should keep its tokens and retry later rather than asking
  for a password. A client that treats every `invalid_grant` as "sign in again" still works; it just
  asks for a password it did not need.
- **Restarting the server, or recreating its container, no longer ends sessions.** The token signing
  and encryption keys now persist in the installation's keys directory and roll over without
  invalidating what they issued. No client change is needed; a device simply stops being signed out
  by server maintenance.

Recorded exchanges written in this entry: none — neither change is a sync exchange.

Codes: no API problem code appeared or changed. The new refusal is an OAuth `invalid_grant`, told
apart from the others only by its `error_description`.

## 2026-10-24 — photos and documents are listed for a phone, and fetched by name

**Contract version: 1, unchanged. A build pinned to it keeps working.** Everything below is new and
additive: two new routes, one new value in `features`, and no change to any existing route, field or
recorded exchange. A build that has never heard of them carries on unaffected.

- **`features` gains `documents`.** `GET /api/v1/sync/capabilities` answers
  `["download", "upload", "documents"]`.
- **`GET /api/v1/sync/sets/{id}/documents`** — a catalogue of the photos and documents hung on the
  set's features, one row per link with its document embedded, paged and resumed exactly as the
  feature download is, with a cursor of its own. Rows never carry a position. Each row's `reach` says
  whether fetching the file itself hands over the stored file, the whole picture with its location data
  removed, pictures of it only, or nothing.
- **`POST /api/v1/sync/files/links`** — signed addresses for the files a phone chose to fetch,
  `"original"` or a picture size, each item decided again against the account's access now. Each
  address carries `sizeBytes` and `sha256`, `null` unless the item is an original handed without its
  location and the installation chose to describe those copies (an administrator setting, off by
  default); then they are the copy's own, exactly the bytes the address serves.
- Which `reach` an account gets follows two administrator settings as well as its access: whether
  location stays in photo files, and — while it does not — whether an account that may have the
  location still gets videos, recordings and pictures that cannot be rewritten. A device reads
  `reach` and does not infer it.

Described in `docs/speleoloc-sync/01-protocol.md` §11 and `02-field-mapping.md` §7.

Recorded exchanges written or re-written in this entry: none (the new routes' recordings come with
the cases numbered from 17). `07`–`16` are byte-for-byte unchanged.

Codes: new on the address route, as per-item refusals in a `200` — `sync.document_unavailable`,
`sync.original_withheld`, `sync.rendition_unavailable`; and `validation.failed` for a malformed
request. The catalogue answers only the reading codes it shares with the feature download
(`sync.set_not_found`, `sync.cursor_invalid`, `sync.cursor_stale`). See `04-errors.md` §3 and §3.1.

## 2026-10-25 — an upload is made of sections; phones get a people directory

**Contract version: 1, unchanged. A build pinned to it keeps working.** An upload of `rows` alone is
answered byte for byte as before; `07`–`16` are byte-for-byte unchanged.

- **The upload request's arrays are all optional** (an upload carries at least one item in some
  array), applied in a fixed order with `rows` first, and `uploadRowsMax` counts items over all of
  them. An optional `clientFeatures` list (at most 16 names) is accepted and kept; no name changes
  anything yet.
- **`people`** — a new optional upload array of the phone's own users
  (`deviceUserId`, `username`, `firstName`, `lastName`, optional `caverId`), answered in a `people`
  array of the per-row shape that is present only when the request carried it.
- **`GET /api/v1/sync/people`** — the people directory (names only) and the current links of the
  phone users this account has sent.
- **`features` is computed per request.** `people` is named while the installation has the directory
  switched on and the account may be told who people are.

Described in `docs/speleoloc-sync/01-protocol.md` §8 (sections) and §12 (People), `02-field-mapping.md`
§8, `04-errors.md` §3.2 and §6.2.

Recorded exchanges written or re-written in this entry: none.

Codes: new — `sync.feature_disabled` (`409` from the directory; per item in `people` when a link is
sent while it is off) and `sync.person_not_listed` (per item in `people`).

## 2026-10-25 — phones send photos and documents

**Contract version: 1, unchanged. A build pinned to it keeps working.** An upload without the new
arrays is answered byte for byte as before; `07`–`16` are byte-for-byte unchanged.

- **Staged bytes.** `POST /api/v1/files/uploads` takes `stageForSync: true`; completing such a
  transfer hashes and types the bytes and keeps them waiting, under the transfer's id, for the
  installation's staging retention (seven days by default). They count against the account's
  storage room until adopted, withdrawn or swept.
- **`documents`** — a new optional upload array of the phone's photos and documents (`id`,
  `baseRevision`, `deleted`, `title`, `description`, `fileName`, `sha256`, `sizeBytes`,
  `stagedUploadId`, `capturedAt`, `createdByDeviceUserId`, `clientUpdatedAt`, `baseVersionId`),
  answered in a `documents` array present only when the request carried it. A new id becomes a
  document under it; new bytes on an existing one become a new revision that keeps the old. An edit
  names `baseVersionId` — the catalogue's `versionId` for the bytes the phone holds — and then says
  by naming a staged upload or not whether it brings new bytes, which a phone holding a copy without
  location data cannot say by hash. A document's revision moves on a new revision or a caption
  written in the web interface too, so such a change is a `conflict` for a phone that read it before.
- **`documentLinks`** — a new optional upload array of where documents hang (`documentId`,
  `target`, `role`, `caption`, `sortOrder`, `deleted`), applied after `documents` and answered in a
  `documentLinks` array, in the order sent, under each link's identifier.
- **The upload's undo** takes these back too: a created document removed, new bytes stepped back to
  the revision before them, a link hung taken down, a link taken down hung again. An undo line
  answers a `documentId`.
- **`features`** names `documentUpload` on every installation running this build.

Described in `docs/speleoloc-sync/01-protocol.md` §11.3, `02-field-mapping.md` §7.1–7.2,
`04-errors.md` §6.2 and §6.4.

Recorded exchanges written or re-written in this entry: none. `manifest.json` lists the new feature
name.

Codes: new — `sync.file_hash_mismatch`, `sync.file_bytes_missing` (per item in `documents`),
`sync.target_not_found` (per item in `documentLinks`); existing codes met in the new arrays:
`sync.document_unavailable`, `sync.location_forbidden`, `sync.row_forbidden`,
`sync.row_delete_forbidden`, `sync.kind_unsupported`, `sync.caving_group_forbidden`.

## 2026-10-25 — phones send and receive trip recordings

**Contract version: 1, unchanged. A build pinned to it keeps working.** An upload without the new
arrays is answered as before; `07`–`16` are byte-for-byte unchanged.

- **`tripRecordings` and `tripPoints`** — two new optional upload arrays: a recording of a trip in a
  cave (the phone's own identifier, cave, title, description, log, start, end, the phone user who
  started it) and its scans (place, time, notes, phone user), applied after `people`, recordings
  before scans. Answered in `tripRecordings` and `tripPoints` arrays of the per-row shape, present
  only when the request carried them. Arbitration and replay are the rows'.
- **`GET /api/v1/sync/sets/{id}/trips`** — the recordings of the caves in the set that this account
  may read, a page at a time with the set's cursor, each with its scans embedded; a scan at a place
  the caller may not be given is left out of the recording, and deletions arrive as tombstones.
- **`features` names `trips`** on every installation running this build.
- What the server makes of a recording — a trip report, made automatically when everything in it
  settles — is not part of the exchange; a phone sees only the recording it sent.

Described in `docs/speleoloc-sync/01-protocol.md` §13 (Trips), `02-field-mapping.md` §9,
`04-errors.md` §3, §6.1 and §6.2.

Recorded exchanges written or re-written in this entry: none.

Codes: new, per item — `sync.cave_not_found` (`tripRecordings`), `sync.recording_not_found` and
`sync.place_not_found` (`tripPoints`). Not sent to phones: `import.recording_already_converted`
answers the browser's archive import.

## 2026-10-26 — an account's uploads can wait for a reviewer

**Contract version: 1, unchanged. A build pinned to it keeps working** for every account that is not
placed under review; `07`–`16` are byte-for-byte unchanged, and no answer to such an account carries
`reviewId`.

- **`clientFeatures: ["held"]`** — a device that lists it understands the row status **`held`**: the
  account's uploads wait for a reviewer, nothing was written, and the row is kept as a proposal under
  `reviewId` (with the revision the row already had). The same content sent again is answered with
  the same `reviewId`; different content replaces the proposal under a new one. An account under
  review whose upload does not list `held` is refused whole with `409 sync.client_update_required`.
- **`GET /api/v1/sync/sets/{id}/reviews`** — the decisions on this account's held rows, a page at a
  time on the set's cursor rules, oldest decision first: `accepted` (with the row's new revision),
  `rejected`, `superseded` or `failed` (with its `code`), the reviewer's note, and for a turned-down
  edit or removal of a feature the server's row as `current` — absent when this account may not be
  given that row's position.
- **`features` names `reviews`** on every installation running this build.

Described in `docs/speleoloc-sync/01-protocol.md` §8 and §14, `04-errors.md` §3, §6.1, §6.2 and §8.

Recorded exchanges written or re-written in this entry: none (the held upload and the reviews stream
are recorded with the next fixtures).

Codes: new — `sync.client_update_required` (409, the whole upload); on the reviews stream, a `failed`
entry's `code` is an upload code or `review.dependency_failed` / `review.proposal_unreadable`.

## 2026-10-26 — phones send and receive raster maps and their pins

**Contract version: 1, unchanged. A build pinned to it keeps working.** An upload without the new
arrays is answered as before; `07`–`16` are byte-for-byte unchanged.

- **`rasterMaps` and `rasterPins`** — two new optional upload arrays: a phone's raster map of a cave
  (the phone's own identifier, cave, optional area, title, one of four map types, order, and its picture
  as bytes staged beforehand) and the places pinned on it by pixel. Maps are applied after `documents`,
  pins last of all, so a pin sent beside its map and its place finds both. Answered in `rasterMaps` and
  `rasterPins` arrays of the per-row shape, present only when the request carried them. Arbitration and
  replay are the rows'. From an account under review both are held as every other section is (the entry
  above), and a decision on them reaches the reviews stream with `entity` `rasterMaps` or `rasterPins`.
- **`GET /api/v1/sync/sets/{id}/rasters`** — the raster maps of the caves in the set that this account is
  handed, a page at a time with a cursor of its own, each with its pins embedded; a pin of a place the
  account is not handed is left out of the map, a map of a withheld cave is absent, and removals arrive as
  tombstones. The picture is the map's `documentId`, listed by the documents stream as a link to the cave
  with the role `map2d`.
- **`features` names `rasters`** on every installation running this build.

Described in `docs/speleoloc-sync/01-protocol.md` §15 (Raster maps), `02-field-mapping.md` §10,
`04-errors.md` §6.2a.

Recorded exchanges written or re-written in this entry: none.

Codes: new, per item — `sync.area_not_found`, `sync.map_cave_fixed` and `sync.raster_not_an_image`
(`rasterMaps`), `sync.raster_map_not_found` and `sync.pin_exists` (`rasterPins`); `sync.cave_not_found`
and `sync.place_not_found` are also met on these sections.

## 2026-10-26 — photos hang on a phone's trip recordings and on their scans

**Contract version: 1, unchanged. A build pinned to it keeps working.** An upload that names only
`feature` targets is answered as before, and a documents page that carries no such link reads as
before; `07`–`16` are byte-for-byte unchanged.

- **`target.kind` gains `tripRecording` and `tripPoint`** in the upload's `documentLinks` and in
  the links of the documents stream. A photo or document hangs on a recording, or on one scan of it,
  the way it hangs on a feature, under Write on the recording. The recording's cave — and for a scan its
  place — must be in the selection and not withheld from the account (`sync.location_forbidden`
  otherwise); a recording or scan the account cannot read is `sync.target_not_found`.
- **The documents stream lists these links** when the recording is readable and its cave handed, and for
  a scan when its place is handed too. A photo on a scan of a withheld place is absent. A link on a
  recording or scan that is removed, or taken down, arrives as a tombstone.
- **Conversion** hangs every photo of a recording and its scans on the trip report made from it.

A reader of the documents stream that treats `target.kind` as a closed set of one value must now
tolerate the two new names; one that only ever sends `feature` is unaffected.

Described in `docs/speleoloc-sync/01-protocol.md` §11 ("Photos on a recording and on a scan"),
`02-field-mapping.md` §7.2, `04-errors.md` §6.2.

Recorded exchanges written or re-written in this entry: none.

Codes: no new ones — `sync.target_not_found`, `sync.location_forbidden`, `sync.row_forbidden` and
`sync.kind_unsupported` are met on recording and scan targets.

## 2026-10-28 — removed pins and scans are named, every turned-down edit carries the server's row, documents keep their phone user

**Contract version: 1, unchanged. A build pinned to it keeps working.** Every change is a member
added to an answer; nothing a version-1 build reads changes meaning, and an upload is answered as
before.

- **`pinTombstones`** on the rasters stream (`{ id, mapId, deletedAt }`) and **`pointTombstones`** on
  the trips stream (`{ id, recordingId, deletedAt }`), riding that stream's one cursor exactly as
  `tombstones` do. Each names a pin (scan) removed from a map (recording) the account is handed, and
  only one the account would be given were it live — its place handed by the feature stream now (or,
  for a scan, no place). The removal of a pin or scan of a withheld place stays silent. `pins` and
  `points` still list every live one except the withheld, so the device rule becomes **remove what a
  tombstone names, keep what is merely missing**. A removed map or recording is its own tombstone and
  takes its children with it. The parent still comes again when a child goes, so a device that
  ignores the new arrays works as before.
- **`current` on the reviews stream for every section that carries an edit or a removal** —
  `documents`, `documentLinks`, `tripRecordings`, `tripPoints`, `rasterMaps` and `rasterPins` beside
  `rows` — shaped exactly as that section's stream delivers the row (a catalogue `document`, a
  catalogue link, a recording with its `points`, one scan, a map with its `pins`, one pin), and absent
  whenever that stream would not hand the account the row now. `people` carries none. When `current`
  is absent, the device re-reads **that section's stream from the beginning** (drops that stream's
  cursor, not the others'). In the served description `current` is now typed as any value, since its
  shape follows `entity`.
- **`partial: true` on an `accepted` edit written only in part** (a reviewer accepted some of its
  fields), with `current` beside it under the same shapers and withholding rule, for every section.
  The member is absent when the whole edit was written. When `current` is absent on a partial
  acceptance, the device re-reads that section's stream from the beginning. A build that ignores
  `partial` keeps the behaviour it had: it stores the revision against its own version.
- **`documents[].createdByDeviceUserId` is kept** on the document a create makes, and the catalogue's
  `document` gains **`createdByDeviceUserId`** (null when none). A photo that names no photographer is
  shown as taken by the caver that phone user is linked to — the catalogue's `photographerCaverId`
  included — resolved when read; a photographer named in the browser always wins.
- **Each map on the rasters stream gains `pictureWidth` and `pictureHeight`**, the pixel size of its
  current picture as stored — the frame pins' `x`/`y` are measured in — null only when the size could
  not be read. A reader handed only renditions scales pins by `renditionWidth / pictureWidth`
  (renditions keep the aspect ratio; they are drawn upright, so a picture stored on its side has its
  axes swapped in them). **A map's `sha256` is required with `stagedUploadId` and ignored without it**:
  an upload staging bytes for a map without the hash is now refused whole with `400` (an edit was
  answered `rejected sync.file_hash_mismatch` before), and a new map with neither is answered on its
  row `rejected sync.file_bytes_missing` (it was a `400`). An edit without new bytes may leave it out,
  as it always could.
- **The byte-staging routes and `GET /api/v1/files/config` are recorded**, and their members, as
  recorded, are part of contract version 1: opening a transfer with `stageForSync: true`, one piece,
  completing, the transfer's status, and the files settings. **`409 upload.chunk_out_of_order` gains
  the integer member `expectedOffset`**, the offset to send the next piece from; `detail` is
  unchanged. Send from `expectedOffset`, or, when that answer was lost, from the `receivedBytes`
  that `GET /api/v1/files/uploads/{id}` reports.
- Two corrections of the text, no behaviour changed: `20-upload-held/request.json` now sends the
  canonical `"positionQuality": "unknown"` (the recording sent `"Unknown"`, which the server also
  accepts — it reads that value without regard to case); and `01-protocol.md` §12.2 no longer says
  `uploadRowsMax` counts four sections — it counts every section, as §8 and `04-errors.md` say.

Described in `docs/speleoloc-sync/01-protocol.md` §11.1, §11.3, §12.2, §13.1, §14.1 and §15.1,
`02-field-mapping.md` §7.1, and `04-errors.md` (the transfer routes' refusals).

Recorded exchanges written in this entry: `26-files-upload-open` (a `201`, with `status.txt`),
`27-files-upload-piece` (the request line only — the body is the file's bytes),
`28-files-upload-complete`, `29-files-upload-status`, `30-files-config` and
`16-errors/chunk-out-of-order`. Re-written: `17-documents-first-page` (`createdByDeviceUserId` on
the document), `20-upload-held` (the request's `positionQuality`), `24-trips-first-page`
(`pointTombstones`, empty) and `25-rasters-first-page` (`pinTombstones`, empty, and the map's
`pictureWidth`/`pictureHeight`). Everything else is
byte-for-byte unchanged.

Codes: none new. `upload.chunk_out_of_order` is recorded as bytes for the first time, now with
`expectedOffset`.

## 2026-10-28 (second entry) — each map says how its renditions are turned and mirrored

**Contract version: 1, unchanged. A build pinned to it keeps working.** Two members are added to each
map of the rasters stream, and a map comes again in one more case; nothing a version-1 build reads
changes meaning.

- **`pictureMirrored`** (boolean) and **`pictureTurn`** (`0`, `90`, `180` or `270`) beside
  `pictureWidth` / `pictureHeight`: how the map's upright renditions are drawn from the stored frame
  the pins are measured in — **flipped left to right first** when `pictureMirrored` is `true`, **then
  turned clockwise** by `pictureTurn`. The turn is the picture's orientation code's own plus every
  turn recorded in the browser. Renditions **do** mirror: the orientation code is applied in full,
  codes 2, 4, 5 and 7 included. Both are null exactly when `pictureWidth` is. The code is the one the
  server's renderer obeys, read when the picture is stored (a scanner's TIFF states it in its own tag,
  not in EXIF), so a device does not read it out of the file. §15.1 gives the point formulas and a
  worked example. A device that drew pins only on renditions whose axes were not swapped can now draw
  every case, and the half-turn case it could not detect is told.
- **A map comes again when its picture is turned in the browser**, since that changes `pictureTurn`
  and every rendition; a device keeping renditions fetches them again when the map comes with a
  different `pictureMirrored` / `pictureTurn`.

Described in `docs/speleoloc-sync/01-protocol.md` §15.1 and `02-field-mapping.md` §10.

Recorded exchanges re-written in this entry: `25-rasters-first-page` (`pictureMirrored` and
`pictureTurn` on the map). Everything else is byte-for-byte unchanged.

Codes: none new.
