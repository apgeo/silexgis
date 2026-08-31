# Workflow: import a season of GPS waypoints

[← Workflows](README.md) · Reference: [Geodata](../features/geodata.md) ·
[Detection rules](../admin/vocabularies.md#configuration--detection-rules)

---

A GPS unit emptied at the end of a season is the classic hard case: two hundred waypoints,
half of them named the way your club names things (`P. Ursilor`, `Aven 3`, `Izbuc`,
`Doline SE`), half of them called `WPT047`, and a handful of tracks.

Drawing that file on the map is only half the job. The application's import review turns the
waypoints into actual caves, entrances and surface features — and proposes what each one is
from your club's own naming habits.

---

## Step 1 — Upload the file

**Geodata → Vector files**, then drag the file in (or click to choose it).

Accepted: **GPX, KML, KMZ, GeoJSON, zipped shapefile, WKT, and CSV with coordinate columns.**

The file is imported in the background. Its status goes *Queued → Importing… → Imported*. At
that point it is a layer you can switch on in the map's layer tree — its geometry is on the
map, but nothing has entered the registry yet.

## Step 2 — Open the review

From the geodata list, open the file's **Review and import into the registry**.

The review is a workspace, not a dialog:

- a **map preview** of the candidates,
- a **table** of every candidate with what it would become,
- a **What each rule claims** panel showing which detection rule caught what,
- and the option panels described below.

**The review survives closing the tab.** You can leave it half-decided and come back.

## Step 3 — Set the options

### Rules and naming

- **Rule set** — which set of detection rules to apply. Sets can belong to the installation,
  to a caving group, or to you. See [Detection rules](../admin/vocabularies.md#configuration--detection-rules).
- **Languages** — only terms in these languages take part. Terms marked as belonging to no
  language always do.
- **Name prefix** — prepended to everything created.

A rule recognises a term in a waypoint's name (or description) and proposes what it becomes.
`P. Ursilor` becomes a cave named *Ursilor*, with the term taken back out of the name. When
two rules claim one candidate, **the one earlier in the set wins**, and the review says which
other rules also claimed it.

### What gets created

- **Look for something already there within** *n* metres — each candidate then says whether
  the registry already holds something nearby. Only objects whose exact position you may see
  are measured against; a protected cave you cannot locate is not silently used as a match.
- **GPS altitude** — keep what the file says, leave it empty, or decide per candidate.
  Nothing is taken from an elevation model: a public grid beside a cliff is not an
  improvement on a recorded number.
- **Mark everything created as a protected location** — if this is a sensitive area, set it
  once here rather than editing fifty records afterwards.

### Points no rule recognised

This is the panel that decides how much work the `WPT047` half of the file costs you:

- **Leave them for review, one at a time** (the cautious default),
- **Propose a surface feature** of a type you pick,
- **Propose a cave entrance** of a type you pick.

### Tracks and routes

Leave them in the file, or import them as a line feature of a kind you choose.

### Which field is which

Usually *Work it out*. Override if the file puts the name in a description field, or carries
your identification code somewhere unusual.

## Step 4 — Work the table

The table has one row per candidate, with columns: **Name · Becomes · Type · Rule ·
Position · Already there · Decision**.

Narrow it while you work: search names, filter by rule, filter by kind, filter to points /
tracks / areas, or show **only ones with something nearby** — which is the fastest way to
find the duplicates.

Each row's **decision** is one of:

| Decision | Effect |
|---|---|
| **Create** | A new cave, entrance or surface feature is made |
| **Already there** | The candidate is recognised as an existing record — *"It is this one"*, or *"Second entrance of X"* |
| **Skip** | Nothing happens for this row |

Select rows individually, all on the page, all across every page, or invert.

> **Large files.** A review reads a bounded number of rows. If the file is bigger, you get
> *"the first N of M rows are shown"* — confirm those, then open the review again for the
> rest.

## Step 5 — Confirm

Either **Create *n* objects** for the rows you selected, or **Create everything the rules
claimed** if you trust the mapping wholesale.

**Nothing is created until you press this.** Afterwards you get a report: what was created,
what was recognised as already there, what was skipped, and — if any rows failed — why, so
you can fix and confirm those on their own.

## Step 6 — If the mapping was wrong, undo it

**Geodata → Imports** lists every confirmed import: the file it came from, how it was done
(reviewed, or without review), when it was confirmed, and what it created.

**Undo** deletes all the objects that import created, in one press. Including the ones it
hung on caves that were already there.

That is the safety net that makes it reasonable to try an aggressive rule set and see what
happens.

---

## Getting the rules right for your club

The shipped rule set knows the common Romanian and English karst terms. Your club almost
certainly writes some things its own way.

**Configuration → Detection rules.** Everybody with something to import keeps their own
sets; only promoting a set to what a caving group or the whole installation inherits is an
administrator's act.

A rule has: a name, whether it is on, what it compares by (contains / whole word / starts
with / regular expression), the terms, which language they belong to, what it proposes (a
cave, an entrance, a surface feature) and of which type, whether to read the name and/or the
description, and whether to take the term back out of the name (and from where).

A set can be **saved as a file** and **loaded from a file** — which is how you hand your
club's conventions to another club.

---

## Exporting back out

The reverse direction is on the list pages and the map: caves, features and geodata files
export as **GeoJSON, GPX, KML, CSV or zipped shapefile**.

---

Next: [Photographs to places](photographs-to-places.md) ·
Reference: [Geodata](../features/geodata.md)
