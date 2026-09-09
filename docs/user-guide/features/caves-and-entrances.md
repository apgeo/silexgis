# Caves and entrances

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/caves-and-entrances.md)

[← Feature reference](README.md) · Workflow: [Record a new cave](../workflows/record-a-cave.md)

---

The cave record is the densest thing in the application. This page walks its whole surface.

---

## The cave list

**Cadastre → Caves.** A server-side table: search by name or toponym, filter by type, sort
by any column, page through. Columns include name, code, type, region, surveyed length,
depth, entrance count and visibility.

From here: **New cave**, **Edit**, **Delete**, and **Export** (GeoJSON, GPX, KML, CSV,
zipped shapefile).

There is also a **Distributions** view over the caves on the page — length histogram, length
against depth, rank–size with a power-law fit, and a breakdown by type. See
[Measurements and statistics](measurements-and-statistics.md#karst-distributions).

## The cave form

Seven sections. Only the name is required.

### Identification
Name · Other toponyms · Identification code · Cave type · Description · Website

### Localization
Region · Hydrographic basin · Valley · Tributary river · Closest address ·
Land registry number · Location notes

### Geology
Rock type · Rock age

### Morphometry
Surveyed length · Estimated length · Real extension · Projected extension ·
Positive depth · Negative depth · Potential depth · Altitude · Volume · Area ·
Ramification index · Cave age

> These are the *declared* figures — what somebody typed in. The application also **computes**
> figures from an uploaded survey and shows the two side by side, including when they
> disagree. See [Measurements and statistics](measurements-and-statistics.md).

### Status
Exploration status (unknown / ongoing / finished / abandoned) · Protection class ·
Show cave · Show-cave length

### Discovery
Discovery date · Discoverer

### Access
**Visibility** — private / caving group / authenticated users / public.
**Protected location** — *"Exact coordinates and precise-location fields are hidden from
users without explicit permission."* See [Location protection](../admin/location-protection.md).

---

## Entrances

A cave has one or more entrances, each a record of its own with its own position.

| Field | Notes |
|---|---|
| **Coordinates** | Longitude and latitude. Draw on the map or type them |
| **Altitude** | Metres |
| **Entrance type** | From a taxonomy your installation owns |
| **Position quality** | Unknown · GPS · From map · Estimated |
| **Surveyed** | When the position was taken |
| **Main** | The principal entrance; several things default to it |

**Position quality is worth being honest about.** It is the difference between "walk here"
and "search this slope", and it is the one field nobody can reconstruct later.

Add an entrance from the cave's page, from the map's *New entrance here* tool, or by
right-clicking the map.

---

## What a cave's page grows

Beyond the form, the page accumulates sections as you feed it:

| Section | See |
|---|---|
| **Entrances** | Above |
| **Centerlines** | [Surveys and models](surveys-and-models.md#centerlines) |
| **3D survey models** | [Surveys and models](surveys-and-models.md) |
| **Survey sources** | [Surveys and models](surveys-and-models.md#survey-sources) |
| **Survey statistics** | [Measurements](measurements-and-statistics.md#what-a-survey-measures) |
| **Photographs and documents** | [Photographs](photographs.md) · [Documents](documents-and-cabinets.md) |
| **Tags** | Free-form, filterable on tables and map layers |
| **Links** | [Links between records](links.md) |
| **Trips** | The trips that named this cave, newest first — filled automatically |
| **Permissions** | [Permissions](../admin/permissions.md) |
| **Share links** | [Sharing](sharing-and-public-pages.md) |
| **Printed codes** | [QR codes](sharing-and-public-pages.md#printed-qr-codes) |
| **History** | [History and audit](history-and-audit.md) |

The summary line counts them: *n entrances · n centerlines · n 3D models · n attachments ·
n trip logs.*

### The Trips section

You do not add trips to a cave. A cave's **Trips** section lists the trips that named it
through their *What this trip did* fields, newest first, counted by the same rule that decides
what the list may show. If a trip named this cave but you cannot read the trip, it is not
there — and the count reflects that.

---

## A cave is a feature

Under the surface a cave is one kind of [feature](surface-features.md), which is why:

- a cave can be **contained** by something (a karst area) and can contain things (its
  entrances), with breadcrumbs to match,
- containment **inherits location protection downwards**,
- a cave can be **linked** to anything at all by a named relation.

---

## Sharing caves in the interchange format

Alongside the map formats, the cave list's **Export** menu offers **KarstLink (JSON-LD)** — a
description of the caves in the shared speleological vocabulary published by the UIS, so somebody
using different software can read them. It exports the caves the list is currently showing, with the
same search, type and tag filters applied.

**It asks one question first, and it matters.** If any of the caves in the export has a
protected location, the dialog says how many, and asks what the file should
say about them. There are three answers:

- **Include the cave without a position** — everything else about the cave travels, no coordinates
  at all.
- **Leave those caves out of the file entirely.**
- **Include them at the approximate position** — the same rounded position the map shows to people
  who may not see the exact one.

**The exact position is not offered, to anybody.** Not to an administrator, and not to the person who
recorded the cave. A file goes on existing after the permissions that produced it have changed, so
this application will not write a protected position into one.

**The file says what was decided.** Each cave states which of the three was applied and, where a
position was rounded, how far it may be from the truth; a header says how many caves are in the file
and how many were deliberately left out — so nobody counting the caves in the file mistakes it for a
count of the register.

Tick **Remember my answer** and you will not be asked again on this browser; a second menu item lets
you change it, and if the server refuses a request the dialog comes back rather than failing quietly.

Every interchange export is recorded in the [history and audit trail](history-and-audit.md), together
with which caves were exported and how their positions were treated.

---

## Numbers in other registers

A cave often has an entry elsewhere — in [Grottocenter](https://grottocenter.org), the international
community cave database, or in a national cadastre. The cave page has a small section for recording
those numbers, so the link travels with the cave and appears in an interchange export.

If your administrator has turned the Grottocenter lookup on, a button there asks Grottocenter whether
it already knows this cave. **Only the cave's name is sent** — never its position — and only the
number you accept is kept. Most installations will see "your administrator has not set this up", and
nothing leaves the installation in that state.

**One caution.** A Grottocenter entry publishes coordinates. So for a cave whose location is
protected here, the interchange export deliberately leaves the external link out: publishing it would
hand over the position the file is withholding.

---

## Deleting

**Delete this cave?** — and note that on features generally, *contained features are deleted
with it*. Everything is recorded in the [history and audit trail](history-and-audit.md).

---

Workflow: [Record a new cave](../workflows/record-a-cave.md) ·
Related: [Surface features](surface-features.md) ·
[Measurements and statistics](measurements-and-statistics.md)
