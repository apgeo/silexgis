# What a device's records land in

This document is for whoever writes the client side. It says which SilexGIS shapes a cave-navigation
device's records become, which of its fields survive a round trip, and — as importantly — which
values may never be sent at all.

Nothing here moves rows yet. The shapes below exist on the server and can be created and read
through the ordinary feature routes; the transfer that fills them is described separately once it
exists. What this document fixes is the vocabulary, because a device that allocates codes against
one set of names cannot be repointed at another later without renumbering everything it holds.

---

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
