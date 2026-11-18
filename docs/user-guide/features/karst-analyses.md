# Karst analyses: areas, patterns and passage networks

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/karst-analyses.md)

[← Feature reference](README.md) · Related:
[Measurements and statistics](measurements-and-statistics.md) ·
[Map workspace](map-workspace.md) · [Surface features](surface-features.md)

---

Three cards on an area's page and two panels on a cave's page answer questions about karst as a
whole: how thickly caves sit in an area, whether they are scattered or bunched, how deep a system
runs, and what shape a cave's passages make. They share one rule with the rest of the
application: **every figure is counted over what you may see, and a figure that cannot be
measured is left blank and says why.** Two accounts can legitimately read different totals for
the same area.

These figures are for signed-in accounts. They are not part of a shared view's link.

---

## Karst-area statistics

On the page of a **karst area**, a **massif** or a **hill**: **Karst area statistics**.

The card opens with its basis, because it is easy to misread without it:

> Counted over the caves declared to be in this area

A cave belongs to the area because somebody filed it there, not because its position falls
inside the outline. The two can disagree, so the card says how many caves you can place fall
inside the outline without being filed in the area. That number is a hint about how far the
filing has drifted from the map. It is not part of any figure.

| Figure | What it is |
|---|---|
| **Caves**, **Entrances** | Counted over the caves filed in the area that you may read |
| **Area (km²)** | The outline's ground area |
| **Caves per km²** | Caves against that area |
| **Surveyed passage (m)**, **Surveyed metres per km²** | The surveyed length of those caves, and per square kilometre |
| **Caves with a surveyed length** | *n of m*: the caves that carry a length at all |
| **Mapped depressions**, **Depression cover (%)** | Sinkholes drawn as outlines inside the area, and the share of the ground they cover |
| **Karstification index** | A single reading from *very low* to *very high*, see below |
| **Deepest caves**, **Longest caves** | The top of each list |
| **Rock type** | How the area's caves divide between rock types, with **Not recorded** as a row of its own |

The card appears only for those three kinds. A building or a marker has an outline too, but
caves per square kilometre of a building is not a reading anybody wants.

### The karstification index

The index is the mean of two readings, each placed on a scale from nought to one against a stated
reference:

- **Cave density** against five caves per square kilometre. Above that an area scores one rather
  than being ranked against other dense areas.
- **Depression cover** against a tenth of the ground under mapped depressions.

The card shows each component as *"value against a reference of …"*, so two areas can be
compared by their parts as well as by the class. **A reading that is missing is left out of the
mean, not counted as nought**, and the card says so (*"not recorded, so it is left out of the
index rather than counted as nought"*). Most registries have caves long before they have
sinkholes drawn: scoring an area with no sinkholes mapped as having none would rank it below an
area that really has none. An area that cannot be measured at all reads **not measurable** and
is drawn grey on the map.

The same classes fill the areas of the **Karstification index** layer on the
[map](map-workspace.md#karst-layers); clicking an area there opens this card.

### What it never does

- It never counts a cave you may not read, so it never tells you that one is missing.
- It never replaces the filing with the outline: caves inside the outline but filed elsewhere
  are reported as a drift, not added to the totals.
- It never treats an unrecorded length or depression outline as nought.

---

## Density, spacing and alignment

On the same pages, **Density, spacing and alignment**. It asks how the area's entrances are
arranged rather than how many there are.

You choose a **cell size** from a short row of choices. The finest is this installation's
**location-protection grid**, because a count of caves in cells smaller than the rounding that
protects their positions would show the rounding, not the caves. The card says which size is in
use and how many entrances the grid counted.

| Reading | What it tells you |
|---|---|
| **Clark–Evans index** | Below one, entrances sit closer together than chance would put them; above one, they are more evenly spaced; one is indistinguishable from chance. Its *z* and *p* are given beside it. |
| **Ripley's L** | The same question asked at many distances, drawn with a **band** made by random scatters of the same number of entrances over the same area. Where the line leaves the band, the arrangement differs from chance at that distance. The band's number of scatters and its seed are printed so the picture can be reproduced. |
| **Alignment** | The compass bearings between pairs of entrances within a stated range of distances, as a rose. A strong direction often follows a joint or a fault, but the card draws the bearings and proposes nothing. |
| **Hot and cold spots** and **Moran's I** | Whether dense cells sit beside dense ones. Read over the cells already drawn, so they cannot show detail the grid does not have. |

The sentences the card prints are doing real work:

- **Protected positions are counted, but not used for spacing.** The grid counts every entrance
  you may see on the protection grid. Spacing and alignment use **only the entrances you may place
  exactly**: a spacing measured between rounded coordinates measures the rounding. The card says
  how many of each there are. Where too few can be placed exactly, it says *"too few for a spacing
  to mean anything"* and draws nothing.
- **"Random" is not "nothing there".** *"That is a failure to find structure, not a demonstration
  that there is none."*
- **A striking cell is not a finding.** The hot and cold score is taken at every cell at once and
  is not corrected for that, so a scattering of striking cells over a large window is what chance
  alone produces. *"Read the shading, not the individual cell."*
- **No simulation, no band.** If no random scatters were run, the card says no significance was
  tested rather than drawing an empty band.

The map's **Cave density (grid)** layer is the same grid, drawn across whatever you are looking
at; see [Map workspace](map-workspace.md#karst-layers).

---

## Depth and height of a system

On the page of a **cave system**, and on the karst area, massif and hill pages: **Depth and
height of the system**. It is the figure set of
[Depth and height across entrances](measurements-and-statistics.md#depth-and-height-across-entrances)
taken across the member caves, filed under the page's feature.

| Figure | What it is |
|---|---|
| **Member caves measured**, and how many of them have a survey carrying altitudes | The population behind every figure below |
| **Highest entrance of the system**, **Lowest entrance of the system**, **Height between the system's entrances** | Over every entrance of every member cave |
| **System depth below its highest entrance** | From the highest entrance of any member down to the lowest point any member's survey reaches |
| **Height above its lowest entrance** | From the lowest entrance up to the highest surveyed point |
| **Resurgence level under this area** and **Potential depth to the resurgence level** | The lowest spring cave entrance under the area you may place, and the highest entrance less that level |

Membership is the filing, as for the area statistics. Three things are said on the card rather
than left to be noticed:

- *"A cave closed to you adds nothing here, not even to the count."* An entrance or a survey you
  may not place exactly takes no part, and nothing on the card shows that one was left out. If no
  member can be placed, the card says there is nothing to measure.
- A member drawn in plan with no altitudes adds nothing to depth or height. If none carries
  altitudes, the card says so and shows a dash, never a nought.
- If a depth comes from a shape-based reduction of a drawn line and not from survey legs, the
  card calls the figures **approximate**. If more caves are filed than one answer measures, it
  says the figures describe the first of them.

---

## Passage network

On a cave's page: **Passage network**. It counts the cave's survey as a network of stations and
passage legs, the way karst studies do, so that a maze and a branching cave can be told apart by
numbers.

| Group | Figures |
|---|---|
| **The network as surveyed** | **Stations in the network**; **Passage legs** (a passage surveyed twice counts once); **Separate pieces**; **Junctions** (three or more passages meet); **Dead ends**; **Independent loops**, which is the number of genuinely different ways round the network, and nought for a cave reached by one way only |
| **With its straight runs contracted** | **Junctions and dead ends**; **Connections between them**, one per run of passage; and the three ratios **Loopiness (alpha)**, **Connections per junction (beta)** and **Connectedness (gamma)**. A beta below one is a branching cave; above one it closes back on itself |
| **How passages meet** | The **average number of passages** at a junction or dead end; the spread of that figure; and whether junctions join junctions |
| **Runs of passage** | The number of **branches**, how many of them **return to where they started**, and the average, shortest and longest branch length |
| **How the network runs** | **Evenness of branch lengths** and **of passage bearings** (nought to one), **Wandering** (a run's length against the straight line between its ends), **Reliance on one junction**, and **Passages closing back on each other** |

A loop that returns to where it started, and several runs between the same two places, are each
counted as connections of their own. The pattern suggestion below counts loops and junctions the
same way, so the two panels never disagree about how many loops a cave has.

The panel ends with how complete the reading was:

- *"Every leg in the survey file joined the station network, and no two stations shared a
  position. The figures above cover the whole of it."*
- Otherwise it says how many legs could not be attached and how many stations were merged, and
  that the figures were computed over what remained.
- If it is not recorded what the reading left out, it says that the figures may cover less than
  the file contains.

A cave can hold several uploaded surveys, and the panel names the one it measured from
(*"Measured from the survey …"*). Figures taken from two different surveys are not the same
measurement. The panel does not appear for a cave you may read but not place, because a
network is made of the cave's own stations.

---

## What the shape suggests

Below the other statistics on a cave's page: **What the shape suggests**. It proposes one of
four patterns from the measurements: **Vadose branchwork**, **Water-table cave**, **Phreatic
loops** or **Angular maze**. Two other outcomes are honest answers rather than failures: **Too
little measured to try**, and **No pattern came out ahead**.

The rules read how often the network closes on itself, how much of it is dead ends, whether its
passages leave a junction already joined, how few directions the passages follow, how steep or
level the passage is, how the profile climbs and descends, and whether the cross-section is wider
than it is tall.

> The pattern below is read off the measurements by rules whose thresholds are chosen
> conventions, not measurements. It is a proposal about this survey, not a statement about the
> cave.

That is why **every rule is shown, with what it did**: *Fired*, *Did not fire* or *Nothing to
read*, the figure it read, the pattern it counts towards and its weight. A rule that looked and
stayed silent is shown as such, because it says as much as one that fired. The panel also lists
what each pattern scored and how many of the rules that could be assessed fired.

Caveats are printed where they apply, among them: the figures were approximated from the stored
line instead of the surveyor's own flags; reading the survey lost legs or merged stations; the
line carries no altitudes, so the rules about the profile had nothing to read; no station
measured a complete cross-section; or the passage network was never measured.

**What it never does:** it does not state that the cave *is* a pattern, and it does not appear
for a cave whose answer is refused to you, such as one you may read but not place exactly.

---

## The whole registry

The registry-wide screens under **Statistics** — distributions, correlations, regions and
categories — are described in
[The whole registry, explored](measurements-and-statistics.md#the-whole-registry-explored).

## Where caves might be

Also under **Statistics**: **Cave potential**, where a model weighs the shape of the ground, the
rock and what has been recorded into a map of more and less favourable ground over an area — by
its author's judgment, or by counting where the known entrances are. It has its own page:
[Cave potential](cave-potential.md).

---

Related: [Measurements and statistics](measurements-and-statistics.md) ·
[Map workspace](map-workspace.md) · [Terrain](terrain.md) ·
[Caves and entrances](caves-and-entrances.md) · [Cave potential](cave-potential.md)
