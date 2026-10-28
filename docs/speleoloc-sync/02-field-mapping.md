# What a device's records land in

This document is for whoever writes the client side. It says which SilexGIS shapes a cave-navigation
device's records become, which of its fields survive a round trip, and — as importantly — which
values may never be sent at all.

The shapes below exist on the server and can be created and read through the ordinary feature
routes. The transfer that carries them to a device is a separate document — the protocol one, beside
this — and it now moves rows in both directions: a device reads these shapes from a server and
sends its own back. What this document fixes is the vocabulary, because a device that allocates
codes against one set of names cannot be repointed at another later without renumbering everything
it holds.

**A kind's "must sit inside something" column is the rule the upload applies too — when the row is
new.** A new row of a kind that needs a container and names none is refused; a new row of a kind that
does not — a `surface_area`, which is what a selection is rooted in and what a device's general-area
segment is allocated from — is written with nothing above it. A container named on a new row is
always made into a real containment edge, never dropped, because protection and visibility are
inherited along that edge and along nothing else. **On a row that already exists the field is
inert**: an upload does not move a row to another container, and does not refuse one that tries —
see the protocol document's section on partial writes.

---

## 0. The three landings, before the kinds

A device's records land in three different shapes, and the `kind` field on an uploaded row is what
says which:

| Device record | `kind` | What it becomes |
|---|---|---|
| A cave | `cave` | A cave feature. It carries the club's own cave record — a code, a type, and the thirty-odd fields the web interface maintains — and its map point is **not a field anybody writes**: it is a copy of the main entrance's, kept in step by the server |
| An entrance of a cave | `caveEntrance` | An entrance feature contained by its cave. This is the row that carries a real position: a point, with an altitude and a position quality. `isMain` says whether it is the cave's main entrance, which is what the cave's own map point follows |
| Anything else a device holds — an area in a cave, a place in a cave, a named surface area | `generic` | A feature of one of the three kinds in §1, chosen by `featureTypeCode` |

A fourth value exists in the field's type and is **not carried by this contract**: a centerline. A
row naming it is refused with `sync.kind_unsupported`.

**Coordinates are optional on every row, not only on the ones that usually have them.** A cave place
recorded without ever taking a fix is a legitimate row and uploads fine with no geometry; so does an
entrance whose position has not been surveyed yet. Absent is a value, and it is not the same as
withheld — see the protocol document, which describes how a device tells the two apart.

**An upload never nulls a field it did not mention.** A device may send one changed field and leave
the rest of the row alone. That matters most on caves: the club's cave record carries many fields no
device knows about, and a partial write from a phone leaves every one of them exactly as it was.

## 1. The kinds

A device's world has three kinds of object beyond the caves themselves. Each is a SilexGIS
**feature** of a seeded kind, addressed by the kind's stable `code`:

| Device object | SilexGIS kind (`code`) | Category | Geometry accepted | Must sit inside something | Shown to a caller without exact view |
|---|---|---|---|---|---|
| An area inside a cave | `cave_area` | Underground | any | **yes** | **withheld** — no geometry at all |
| A place inside a cave | `cave_place` | Underground | point | **yes** | **withheld** — no geometry at all |
| A named surface area | `surface_area` | Area | any | no | snapped to the protection grid |

Two of these are withheld rather than snapped, which is not the ordinary answer. A snapped point
still says which hillside something is on, and these rows come in numbers: a scatter of places
snapped to the same few grid squares outlines the cave whose position the protection exists to hide.
A surface area is a named grouping with no position of its own and gets the ordinary treatment.

**That last column describes the rest of the API, not the sync download.** On the sync channel every
row whose exact position the caller may not see is absent from the payload, whatever its kind's
setting says — a `surface_area` included. The setting still matters, because these rows are read
through the web interface as well. And this table lists the three kinds a *device* creates: a
download carries whatever is contained in the selection's roots, of every kind the installation has.

**Resolve a kind by its `code`, never by its numeric id.** The ids are assigned by whichever
installation seeded the table first and are not comparable between installations; the codes are
fixed. The kinds are published, with their schemas, by the taxonomy route.

## 2. Where a place hangs

Containment is the only structure the server derives anything from — protection, in particular, is
inherited along containment and along nothing else. So:

- a place whose record names an area inside the cave becomes a child of that `cave_area` node;
- a place whose record names none becomes a child of **the cave itself**.

A `cave_area` and a `cave_place` are refused outright when they would have no container: on
creation, on update, and on a later write that replaces their parent edges with an empty list. All
three refuse with the code `feature.parent_required`. This is a rule about safety and not about
tidiness — a row with no ancestors inherits from nothing, so a place left rootless would hand its
exact in-cave position, which is the cave's position, to every caller who can see the row at all.

## 3. The fields a place carries

A `cave_place` carries the device's identifiers in the feature's `properties` document. The keys are
**flat, top level, and prefixed** — there is no nested `speleoloc` object, and one must never be
introduced. The web interface renders a typed property bag as form fields and skips anything that is
not a primitive, so a nested bag would be stored, validated, synced, and never once shown to the
caver who asked to see these codes.

| Key | JSON type | Title shown | Meaning |
|---|---|---|---|
| `speleolocPci` | string | Place code (PCI) | the device's place code |
| `speleolocQcri` | string | QR code reference (QCRI) | the reference a printed marker carries |
| `speleolocCaveLocalIndex` | string | Cave local index | the place's index within its cave |
| `speleolocGeneralAreaIdentifier` | string | General area identifier | the area segment of the code |
| `speleolocDepthInCave` | number | Depth in cave (m) | metres below the entrance |
| `speleolocSchemaVersion` | integer | Device schema version | the device's own row-shape version |

A `surface_area` carries `speleolocGeneralAreaIdentifier` and `speleolocSchemaVersion` on the same
terms. Its identifier is one segment of every place code allocated beneath it, so it has to survive
a round trip byte for byte or devices re-reading the area renumber their places.

Two notes on the shape:

- The keys are camelCase where older kinds' schemas are snake_case. They mirror the field names the
  device sends, so one name reads the same on both sides of a round trip.
- `speleolocSchemaVersion` is the **device's** row-shape version. It is unrelated to the kind's own
  `propertiesSchemaVersion`, which the server moves when it changes the schema above.

## 4. What may never be sent

**No coordinate, no coordinate-derived value, and no back-projectable measurement may be put in
`properties`.** Not a latitude, not a longitude, not an altitude, and not a pair that reconstructs a
position — a depth beside a bearing, or a distance from a named point.

The reason is mechanical, and is the reason this section exists rather than a note in a review. The
property document is emitted **verbatim to every reader that can see the row at all**, including one
for whom the geometry was just withheld because the cave above it is protected, and no protection
filter touches it on the way out. A coordinate stored beside the codes is therefore the protected
position, published, on the one kind that exists to be withheld.

Since the schemas above set `"additionalProperties": false`, this is enforced rather than asked for:
a write carrying any key not in the tables above is refused with `feature.properties_invalid`. A
field the device grows later is added to the schema first — which is the review this rule needs
anyway.

## 5. Redaction of history

The rule that strips locating values out of change history needed **no entry for these kinds**, and
the reasoning is recorded here so that a later change can find what it would be breaking:

- a `cave_place` and a `cave_area` are generic features, and the existing generic arm of that rule
  already removes geometry from their history entries when the governing root is hidden;
- none of the `speleoloc*` keys above carries anything locating, so there is nothing else in these
  rows for it to redact.

**Both halves have to stay true.** Adding any locating value to `properties` — which §4 forbids for
its own reasons — would also silently defeat this, because history redaction would go on removing
only the geometry while the property document carrying the position went out untouched.

---

## 6. What never travels, in either direction

Some of what a device holds is about the device, and some is about how the device talks to other
machines. None of it belongs on a server, and none of it is carried by this contract.

- **`device_uuid` and `current_user_uuid`.** Facts about one installation of the application on one
  phone. They are not identity and they do not leave it.
- **Beacon health and sensor telemetry.** Hardware state, of interest to the device and nobody else.
- **The local change log.** It is how the device drives its own transfers; it is not data about
  caves. A downloaded row must not produce entries in it — see the standalone rules, which explain
  what happens when it does.
- **FTP profiles and their credentials.** Server addresses and passwords, and a channel this contract
  does not touch.

And two exceptions worth naming, because they look like identity and are not:

- **User identifiers are provenance strings, never accounts.** A device allocates user identifiers
  itself and merges them by username; they are not stable across devices. The server stores what it
  is sent as an opaque provenance label and **never maps one to a SilexGIS account**. Do not expect a
  device user to become a server user, in either direction.
- **The code-strategy configuration rows travel, but not as records.** They are title-keyed rather
  than identified by a uuid, and they move with the sync set as its settings document rather than as
  rows in a batch. The protocol document describes that document and how it is arbitrated.

---

## 7. Files: photos and documents

A device's documentation files — the photos, sketches, sound notes and documents it keeps against a
cave or a place — are, on the server, **a document hung on a feature by a link**. The document is the
file and what is said about it; the link is the fact that it belongs to this cave or this place, with
the role, caption and order it has there. One document may hang on several features, and then it is
one document and several links. The protocol document's section on photos and documents says how a
device reads them; this table is the vocabulary.

| On the server | In a catalogue row | What it is |
|---|---|---|
| The link's identifier | `id` | What a tombstone names when the file stops belonging here |
| The document's identifier | `documentId` | The file across every place it is hung on |
| The link's target | `target` (`kind`: `feature`, `id`) | The cave, entrance, area or place the file belongs to |
| The link's role | `role` | One of `photoEntrance`, `photoInterior`, `photoSurface`, `document`, `map2d`, `surveyData`, `other`, `report` |
| The link's caption and order | `caption`, `sortOrder` | As shown under that feature |
| The headline picture | `isPrimary` | At most one per feature — the picture a list or a card shows |
| The default set | `inDefaultSet` | Chosen by somebody who may change the feature: among "the usual files" a phone offers to fetch |
| The document's title | `document.title` | Its name, as the web interface shows it |
| A photo's caption | `document.description` | The photo's own caption, where it has one; absent for other documents |
| The file's kind | `document.fileKind` | `photo`, `video`, `audio`, `document`, `other` — every server kind that is not one of the first four (a survey file, a raster, a vector layer, a model) is `other` |
| The file's type | `document.mimeType` | As read from the bytes on upload, not from the file name |
| The revision | `document.versionId` | Changes whenever new bytes replace the file; the earlier revision is kept on the server |
| Who took a photo | `document.photographerCaverId` | A caver identifier, where the photo names one — not an account, and not a device user |
| When it was taken | `document.capturedAt` | As read from the file's own metadata, where it could be |

**Never in a catalogue row: a position.** A photo's capture point lives in its file, and on the server
beside it; neither is part of this row, on any reach. Whether the file itself is handed over with its
location inside it is the `reach` member's business, decided by the server's location rules — a device
does not decide it and cannot ask for it.

### 7.1 A document row sent up

What a phone's document row in an upload's `documents` array becomes on the server (the protocol's
*Sending photos and documents* says how it is arbitrated):

| In the row | On the server | Notes |
|---|---|---|
| `id` | The document's identifier | Adopted verbatim on a create |
| `baseRevision` | The document's revision | Compared, never written |
| `deleted` | The document's removal | Needs the right to remove it |
| `title` | The document's title | Blank keeps the name it has; the file name on a create |
| `description` | A photo's caption | Ignored for a file that is not a picture; absent leaves the caption |
| `fileName` | The file's original name | The staged transfer's name when absent; at most 255 characters |
| `baseVersionId` | The revision of the bytes the phone holds (the catalogue's `versionId`) | Compared, never written. With it, no `stagedUploadId` means a description edit and staged bytes of another hash a new revision; a revision that is not the current one is a conflict |
| `sha256` | The current revision's hash | Without `baseVersionId` — only for a phone holding the stored file — the same hash is a description edit and a different one a new revision. May be left out of an edit naming `baseVersionId` and no staged bytes |
| `sizeBytes` | — | Checked against the staged bytes |
| `stagedUploadId` | The staged bytes the revision is made of | Consumed on adoption |
| `capturedAt` | The file's capture time | Only when the file states none |
| `createdByDeviceUserId` | The phone user who added the file | Kept on a create, never moved by an edit; a phone user's identifier, resolved through the phone-user record (section 8), never stored as a caver. Returned on the catalogue's `document`. A photo that names no photographer is credited to the caver that phone user is linked to, resolved when read; a photographer named in the browser always wins |
| `clientUpdatedAt` | — | Informational |

The type, the position, the direction and the capture time inside the file are read from the bytes,
as for every upload; a row never states them.

### 7.2 A link row sent up

What a row in an upload's `documentLinks` array becomes:

| In the row | On the server | Notes |
|---|---|---|
| `documentId` + `target` | The link, found by the pair | A document hangs on a feature once; the link's own identifier comes back in the answer |
| `target.kind` | Which kind of object the link hangs on | `feature`, `tripRecording` or `tripPoint`; the identifier is a feature's, a recording's or a scan's accordingly. A photo on a recording or scan with no `role` is filed as `other` |
| `role` | The link's role | When absent on a new link: a photo on an entrance `photoEntrance`, on a `cave_place` or `cave_area` `photoInterior`, on anything else `other`; any file that is not a photo `document`. Absent on an existing link, the role stands |
| `caption` | The link's caption | The file's name on that feature — not the photo's description. Absent leaves it; empty clears it |
| `sortOrder` | The link's order | Absent leaves it; `0` on a new link |
| `deleted` | The link's removal | The document stays |

The headline mark and the default-set mark are not sent up: they are chosen in the web interface.
A new link hangs the document's current file, and moves with the document when new bytes replace it.

## 8. People: the phone's users

A phone's own users are recorded on the server as **one phone user each**, keyed by the phone's
identifier, and linked to at most one caver. One caver may be several phone users (the same person on
two phones).

| On the phone | In an upload's `people` item | On the server |
|---|---|---|
| `users.uuid` | `deviceUserId` | The phone user's identifier, adopted verbatim |
| `users.username` | `username` | Kept as last sent |
| `users.first_name`, `users.last_name` | `firstName`, `lastName` | Kept as last sent |
| The caver chosen from the directory | `caverId` | The link, unless somebody keeping the roster made one — that one stands |

| In the directory answer | From |
|---|---|
| `people[].id` | The caver's identifier |
| `people[].fullName`, `shortName` | The roster's names (an account holder's own label wins) |
| `people[].hasAccount`, `isMe` | Whether the caver signs in here, and whether it is the caller |
| `links[]` | Each phone user this account has sent, and the caver it stands linked to |

**What never travels here:** a caver's e-mail address, telephone number and roster remarks, and any
caver the directory does not list for the account. The archive import's review uses the same links:
an archive's phone user with a link is reviewed as already mapped — the review's answer carries those
links as `knownLinks`, and the review screen shows each as the phone user's choice — and the
reviewer's choice is written back as the phone user's link (never over one made by somebody keeping
the roster, and never to a caver no longer on the roster).


## 9. Trips: recordings and their scans

A phone's trip recording is kept on the server **as a recording**, under the phone's identifier,
beside any trip report the club makes from it. Its scans are kept as **scans on the recording**, each
naming the place scanned. The protocol document's section on trips says how they are read and sent.

| On the phone | In a `tripRecordings` item / a stream recording | On the server |
|---|---|---|
| `cave_trips.uuid` | `id` | The recording's identifier, adopted verbatim |
| `cave_trips.cave_uuid` | `caveId` | The cave the recording was made in |
| `cave_trips.title`, `description`, `log` | `title`, `description`, `log` | Kept as sent (`title` may be empty) |
| `cave_trips.trip_started_at`, `trip_ended_at` | `startedAt`, `endedAt` | Kept as sent; `endedAt` absent while open |
| `cave_trips.created_by_user_uuid` | `createdByDeviceUserId` | A phone user's identifier, resolved through the phone-user record (section 8), never stored as a caver |
| `cave_trips.updated_at` | `clientUpdatedAt` | Provenance only, never compared |
| — | `revision` | The server's revision of the recording |
| The uploading account | — | The recording's owner |
| The set's caving group | — | The recording's club; visible to that club, or private to the owner when the set names none |

| On the phone | In a `tripPoints` item / a stream scan | On the server |
|---|---|---|
| `cave_trip_points.uuid` | `id` | The scan's identifier, adopted verbatim |
| `cave_trip_points.cave_trip_uuid` | `recordingId` (upload only) | The recording it belongs to, for good |
| `cave_trip_points.cave_place_uuid` | `placeId` | The place scanned — a feature, normally a `cave_place` inside the cave |
| `cave_trip_points.scanned_at` | `scannedAt` | Kept as sent |
| `cave_trip_points.notes` | `notes` | Kept as sent |
| `cave_trip_points.created_by_user_uuid` | `createdByDeviceUserId` | A phone user's identifier |
| — | `revision` | The server's revision of the scan |

**Never in a recording or a scan: a position.** Where a scan was is its place's position, delivered (or
withheld) by the feature stream; a scan of a place a reader is not handed is left out of the recording
that reader receives. `cave_trips.device_uuid` and `cave_trip_points.device_uuid` never travel.

### 9.1 From a recording to a trip report

What the server writes when it makes a trip report from a recording (the protocol document's section
on trips says when it does):

| From the recording | On the trip report |
|---|---|
| `title` (or the start day, `yyyy-MM-dd`, when empty) | The trip's title |
| `startedAt` | The trip's date (its UTC day) |
| `endedAt` | The trip's last day, when it is a later day than the start |
| `description`, `log` | The trip's description and its results |
| `caveId` | The cave the trip is linked to, and the cave every position is anchored to |
| The recording's owner, club and audience | The trip's owner, club and audience |
| Each scan's phone user (else the recording's) | A person on the roster, through the phone-user mapping — never matched by name |
| A scan that settles | One position: the station the scan's place stands for, at `scannedAt`, with the scan's notes (cut to the 2,000 characters a position carries) |
| A scan that does not settle | No position; named, by its identifier, on the trip's attention flag |

The trip is finished, has a closed tracking record against the survey used, and is never announced to
the people on it. The recording keeps everything it said, including every scan the trip left out.

### 9.2 From an archive recording to the same row

An archive import (the browser's import of a SpeleoLoc export) keeps the recording it imports, on the
same row a phone upload of it writes:

| From the archive (`cave_trips`, `cave_trip_points`) | On the recording |
|---|---|
| `cave_trips.uuid` | The recording's identifier — the same one a phone sends |
| — | The cave the import placed it in (the cave of the survey it was reviewed against) |
| `title` (or the start day, when empty), `description`, `log`, `started_at`, `ended_at` | The same |
| The trip's phone user | The phone user who started it |
| The importing account | The owner |
| The trip report's club | The club, with the club audience — when the report is seen by the club (or more widely) and the importer may add trips to that club and bind them to it; otherwise the owner alone, so the recording is never seen more widely than its report |
| — | Marked as made into the trip report the import wrote onto |
| Each scan's `uuid`, place, `scanned_at`, `notes`, phone user | A scan with the same identifier; a place this installation does not hold is left off the scan |

A recording the installation already holds — a phone sent it — is not rewritten from the archive: the
import only marks it as made into the report, and only for an importer who may change that recording.

The same table, read the other way, is how the review reads a recording a phone sent when it is made
into a trip by hand (`POST /speleoloc-imports/recordings/{recordingId}/preview` and `/commit`): the
recording's title, description, log and dates stand in for the archive's, each scan for an archive scan
(its place, moment, notes and phone user — the recording's phone user when the scan names none), and
the document count is zero.

## 10. Raster maps and their pins

A phone's raster map is kept on the server **as a map**, under the phone's identifier, with its picture
as a document hung on the cave as a map. Its pins are kept as **pins on the map**, one per place. The
protocol document's section on raster maps says how they are read and sent.

| On the phone | In a `rasterMaps` item / a stream map | On the server |
|---|---|---|
| `raster_maps.uuid` | `id` | The map's identifier, adopted verbatim |
| `raster_maps.cave_uuid` | `caveId` | The cave the map is of; cannot change |
| `raster_maps.cave_area_uuid` | `caveAreaId` | A feature inside that cave the map covers; absent for the whole cave |
| `raster_maps.title` | `title` | Kept as sent, trimmed |
| `raster_maps.map_type` | `mapType` | See the table below |
| `raster_maps.sort_order` | `sortOrder` | Kept as sent |
| The map's image file | `stagedUploadId`, `sha256`, `sizeBytes`, `fileName` | A document made from the staged bytes, hung on the cave with the map role; `documentId` on the stream, with the stored picture's `pictureWidth` and `pictureHeight`, and `pictureMirrored` / `pictureTurn` — how its upright renditions are drawn from that frame (flip left to right first, then the clockwise turn: the orientation code the renderer obeys plus every turn recorded in the browser). `sha256` is required with `stagedUploadId` and ignored without it |
| `raster_maps.updated_at` | `clientUpdatedAt` | Provenance only, never compared |
| — | `revision` | The server's revision of the map |
| The uploading account | — | The map's owner |

| On the phone (`map type`) | `mapType` | Means |
|---|---|---|
| plane view | `planView` | The cave seen from above |
| projected profile | `projectedProfile` | A profile projected onto a vertical plane |
| extended profile | `extendedProfile` | A profile unrolled along the passages |
| other | `other` | Anything else — a sketch, a detail |

| On the phone | In a `rasterPins` item / a stream pin | On the server |
|---|---|---|
| `cave_place_to_raster_map_definitions` row id | `id` | The pin's identifier, adopted verbatim |
| The map's `uuid` | `rasterMapId` (upload only) | The map it is on, for good |
| `cave_place_uuid` | `placeId` | The place pinned, inside the map's cave |
| `x`, `y` | `x`, `y` | Whole pixels from the left and top edge of the picture |
| — | `revision` | The server's revision of the pin |

The app's raster images are always named `.jpg` whatever their format; the server types the file by its
content, and refuses a picture that is not an image (`sync.raster_not_an_image`).
