# Work areas

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/work-areas.md)

[← Feature reference](README.md) · Related: [Trips](trips.md) ·
[Surface features](surface-features.md)

---

**Work areas** sits at the top level of the rail, beside the map — because it is a way of
looking at the ground, not a register of what is in it, and it is where most readers start a
season.

---

## What a work area actually is

A stretch of country a club works: a massif, a karst zone, a valley system.

Technically it is **a feature of the *work area* type** — not a tag, not a list you add things
to. That distinction matters in practice:

> A tag is installation-wide free text. Renaming or deleting the one that meant "we work here"
> would empty the board that lists them, silently and permanently, and nothing would fail. A
> **type** cannot be renamed out from under the software.

So *being a work area is a fact about the record*, not about whichever list somebody happened
to add it to.

Because it is an ordinary feature, it carries a name, a description, a shape, tags,
[links](links.md), history and permissions like anything else — and it is filtered by exactly
the same access rule as every other feature. **There is no second access path here.**

## Levels

The levels below one — a valley inside a massif, a sector inside a valley — are **not a second
kind of thing**. They are work areas sitting inside another through the ordinary
[containment hierarchy](surface-features.md#hierarchy-parents-and-children).

Which means **a sub-area is a work area in its own right the moment you open it**, with its own
sub-areas beneath it.

## Creating one

Draw an area on the map and record it as a work area — a feature of the work-area type.

If none exist yet, the page says so: *"No work area has been marked yet. Draw an area on the
map and record it as a work area."*

**An area may exist before its boundary does.** A work area with no shape drawn yet is still
listed on the board; the overview simply leaves it off the map rather than inventing an
outline for it.

---

## The overview page

A map of its own — not the workspace map — showing **every area at one level**, told apart by
colour and named on the map, with the level beneath each one a click away.

| Control | |
|---|---|
| **All areas** / **At this level** | Browse the whole set, or just the current level |
| **Breadcrumb** | The chain from the top down to what you have opened |
| **On the map** | Opens the area in the main map workspace |
| **See all** | |
| **Areas inside: n** | Told *before* the shapes for that level are fetched, so you know there is another level to open |

Names are drawn with a halo, because they sit over aerial imagery as often as over a plain
background.

### One thing that looks like a bug and is not

**An area whose parent you cannot read appears at the top level.**

The server states a parent only when you may read that parent too — so an area nested under
something you cannot see has to appear *somewhere*. Dropped instead, it would vanish from the
overview entirely: visible to the server, invisible to its reader.

So two people can legitimately see the same area at different levels of the tree, and both
views are correct.

### If there are more than the page holds

*"Not every work area is shown: this installation holds more than this view lists."* The cap is
stated rather than left to be inferred from the count.

---

## Where work areas are used

**On a trip.** Work areas are the first of a trip's
**[What this trip did](trips.md#what-this-trip-did)** fields — the ground a trip worked in. A
work area is shown there **with what contains it**, which is what tells two sectors of the same
name apart.

**On a camp.** A camp has a working area of its own, drawn on its map beside the trips you may
read.

**On the dashboard.** Work areas are one of the boards the dashboard can carry.

That is what makes a season legible afterwards: which ground was touched, how often, and by
whom — as far as the reader is allowed to know.

---

Related: [Trips](trips.md) · [Camps](camps.md) · [Map workspace](map-workspace.md) ·
[Surface features](surface-features.md)
