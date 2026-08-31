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
