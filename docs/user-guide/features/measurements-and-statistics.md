# Measurements and statistics

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/measurements-and-statistics.md)

[← Feature reference](README.md) · Related:
[Surveys and models](surveys-and-models.md) · [Trips](trips.md)

---

The application computes a good deal, and it is unusually careful about saying what a figure
is computed *from* and when it refuses to compute one at all. That refusal is the useful
part: a blank is a statement that nothing said, and a nought would be a statement that
something did.

---

## What a survey measures

On a cave's page, **Survey statistics** — measured from the cave's line work.

| Figure | |
|---|---|
| **Length surveyed** | Total |
| **Length in plan** | Projected to horizontal |
| **Vertical extent** | |
| **Greatest span** | The largest straight-line distance |
| **Verticality / Horizontality** | |
| **Linearity / Sinuosity** | How much the passages wander |
| **Segments / Paths** | |
| **Highest point / Lowest point** | |
| **Length to depth** | |

Plus **sinuosity of the longest passages**, each with its length and its straight-line
distance.

### Where the figures come from, stated

Every set of figures says its own basis:

- *"Measured from the compiled survey, using the surveyor's own flags to decide which shots
  are passage. This is what these figures are defined as."*
- *"Approximated from the stored centerline, because this cave has no compiled survey
  carrying per-shot flags. The shape of the line work stands in for the surveyor's flags."*
- *"Measured from the survey «X»"* — because a cave may hold several uploaded surveys, and
  figures taken from two different ones are not the same measurement.

### No altitudes means no vertical figures

*"This line work carries no altitudes — a plan drawing of a cave is not a flat cave."* Every
vertical figure is left blank rather than shown as nought.

### Computed against declared

The cave's own **morphometry** section holds figures somebody typed in. The statistics show
both:

- *"n computed; nothing written in the record to compare it with."*
- *"n computed, m in the record, a difference of d."*

And when they disagree, a note: **neither is automatically the wrong one.** A typed-in
morphometry is frequently older than the survey; a survey can also be a partial one. The
application flags the disagreement and leaves the judgement to you.

---

## Passage orientation

A **rose diagram** of passage trends over sectors, north up, weighted either **by length** or
**by the number of survey legs**. Each trend is drawn on both sides of the diagram, because a
passage running one way runs the other way too. It reports the **mean trend**.

Alongside it, **inclination** — the steepness of survey legs, with a linear or logarithmic
count axis, and a **mean steepness from level** that ignores whether a leg climbs or
descends.

Two refusals worth knowing:

- **Steepness cannot be worked out** if the line work carries no altitudes. Drawing it anyway
  would show the cave as level everywhere, which is a statement nobody made.
- **No direction can be worked out** if every surveyed leg drops straight down, climbs
  straight up, or is flagged as passage already surveyed on another line.

---

## Measured shape (morphometry)

For a feature drawn as an outline — a doline, a karst depression, a working area:

**Area · Perimeter · Circularity · Long axis · Short axis · Elongation · Long-axis bearing ·
Centre**

Two notes the panel makes itself:

- Figures are **measured in metres in the installation's working coordinate system**, not in
  the degrees the outline is stored in.
- **The long-axis bearing is an alignment, not a direction**: it runs 0°–180°, so 170° and
  350° are the same alignment.

If the outline crosses itself it has no measurable area or shape, and you are told to redraw
it rather than given a number.

---

## Closest approach

How close two caves come to each other. Pick a second cave from the first's page.

You get the **shortest distance**, and its **horizontal** and **vertical** components, and a
**bearing**. The line is drawn on the map (in plan) and in the 3D view (running through the
rock at the depths its two ends were surveyed at).

> The horizontal and vertical figures are **the two parts of that same shortest line** — not
> the shortest distance across and the shortest distance down, which are different pairs of
> points.

It refuses in three cases, each named:

- **You may not place both caves.** A distance between two caves places each of them from the
  other, so it is given only to a reader who may see exactly where both are.
- **At least one has no line work** to measure from.
- **At least one was drawn in plan with no depths recorded**, so there is no honest distance
  in three dimensions.

---

## Rock overhead

How much rock lies over a cave's passages, along their length. On the cave's page.

The chart runs **distance along the passage** across the bottom and the **thickness of rock
overhead** up the side, with the rock shaded between. Press a point on the curve and the
reading is marked where it was taken — on the map and in the 3D view — and named beneath the
chart: the distance, the thickness, and which piece of line work answered.

> **Distance along is not a walk from the entrance.** A survey is a network, not a route. The
> axis is cumulative length in the order the line work records it.

**A break in the curve is a place nobody has measured the ground for**, not a place where the
passage reaches the surface. Where elevation data does not cover the passage, there is no line
— and the figures beneath the chart (least, greatest, average) are worked out over the covered
readings only, with the number of them shown, so a partly covered cave is not reported as
shallower than it is.

It says so plainly rather than drawing nothing, in four different cases: the cave has no line
work; the survey was drawn in plan with no depths, so there is nothing to subtract from; no
elevation data has been prepared on this installation at all; or some has, and none of it
reaches this cave. Those are different problems for different people.

A long cave is sampled coarsely rather than answered slowly: readings are spread at an even
interval, never closer than 2 m and never more than 400 of them.

You need permission to read elevation data as well as permission to see exactly where the cave
is. Ask an administrator if the panel does not appear.

---

## Karst distributions

Over the caves on a cave-list page: **Distributions**.

| View | |
|---|---|
| **Length** | Histogram of surveyed length |
| **Length vs depth** | Scatter, with a fit reporting slope and R² |
| **Rank–size** | With a power-law fit and its exponent |
| **By type** | Counts |

The scope note is the important part: *"Computed from the n of m caves on this page that
carry a surveyed length. **Caves without one are absent rather than counted as zero.**"*

---

## Trip statistics

A person, a cave and a caving group each get their totals:

**Trips · People · Places · First visits · Hours underground · Surveyed · Rope · Survey
stations · Trips with an incident · Photographs**

Read the two caveats it prints:

- **"Counted over the trips you may read."** Somebody with different access sees different
  totals for the same subject, **and both are right**.
- **"Hours cover the n of m times somebody went where entry and exit times were written
  down."**

Nothing is stored. Hours are worked out from the times recorded, over however many days the
trip really ran. A **first visit** is simply the earliest trip that took somebody somewhere —
so typing up an older trip from the archive *corrects* the figures rather than leaving a
stale flag behind.

Any of the three can be **saved as a spreadsheet**, carrying exactly what the screen carried
and saying whose totals they are — because a file gets forwarded and read months later.

> **You cannot ask for one person's trips by name.** That is a question about a person,
> assembled out of records the asker may never be allowed to read. Your own trips are under
> *Trip logs → My trips*, worked out from whoever is signed in.

---

Related: [Surveys and models](surveys-and-models.md) · [Trips](trips.md) ·
[Caves and entrances](caves-and-entrances.md)
