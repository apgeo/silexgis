# Cave potential: models, evidence and favourability maps

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/cave-potential.md)

[← Feature reference](README.md) · Related: [Karst analyses](karst-analyses.md) ·
[Geology](geology.md) · [Terrain](terrain.md) ·
[Location protection](../admin/location-protection.md)

---

A **cave-potential model** is a written-down judgment: *which evidence says a piece of ground
could hold a cave, and how much each kind of evidence counts.* Run over an area, it becomes a
**favourability map** — every cell of the area coloured from *less favourable* to *more
favourable*.

The page says of itself:

> A model weighs evidence — the shape of the ground, the rock, what has been recorded — into a map
> of where caves are more or less likely. The maps are a weighing of that evidence and not a
> finding, and are for use inside the group only.

Everything is under **Statistics → Cave potential**: a list of **Models** and a list of **Maps
computed**. The maps are then drawn from the [map](map-workspace.md)'s layers panel.

| You want to | You need |
|---|---|
| Write, copy or share a model | An account that may keep saved views and filters |
| Run a model, or read any map | Permission to read **terrain** |
| Read one particular map | The rules under [Who can see what](#who-can-see-what) |

---

## The evidence layers

A model is built from **evidence layers**: surfaces the application works out itself, one value
per cell. The list is fixed by the application. A model weighs each layer at most once.

### From the shape of the ground

These read the installation's elevation and the [pictures of the ground](terrain.md#pictures-of-the-ground-on-the-map)
computed from it. They hold no position of anything anybody recorded.

| Layer | A cell holds | Read from |
|---|---|---|
| **Elevation** | The height of the ground, in metres | The terrain the installation draws |
| **Slope** | Steepness, in degrees | The **Steepness** picture, computed in degrees |
| **Aspect** | Which way the ground faces, as a compass bearing 0–360° | The **Facing** picture |
| **Topographic position** | How far the cell stands above or below the ground around it, in metres | The **Topographic position** picture |
| **Ruggedness** | How much heights differ between a cell and its neighbours, in metres | The **Ruggedness** picture |
| **Roughness** | The largest height difference around the cell, in metres | The **Roughness** picture |
| **Landform** | One of ten shapes of the ground: flat, peak, ridge, shoulder, spur, slope, hollow, footslope, valley, pit | The **Landform classes** picture |

**A model does not compute these pictures.** An administrator asks for them on the
[Terrain page](../admin/terrain-builds.md#derived-pictures-of-the-ground); a run then reads the
newest one of each kind that is ready and not out of date. Elevation needs no picture, only a
terrain build. If a picture a model weighs has not been computed, the run fails and says which
(see [When a run fails](#when-a-run-fails)).

Two things follow from reading whatever picture is there:

- The **Landform** layer means what the picture's *search distance* made it mean: the same hillside
  is a slope at 100 m and a shoulder at 1 km. A model that scores landforms was written with one
  scale in mind; check the picture on the map before trusting the run.
- **Ruggedness** has two published definitions that give different numbers. A table of classes
  written against one is wrong against the other.

### From what has been recorded

These are made from features in the registry. A surface made from them shows where they are, which
is why a map weighed from any of them is governed by [location protection](#maps).

| Layer | A cell holds | Made from |
|---|---|---|
| **Distance to faults** | Metres to the nearest fault | Every **Fracture line**, and every [structural measurement](geology.md#structural-measurements) whose measured plane is **Fault plane** |
| **Rock type** | The lithology class of the ground | The [geological unit](geology.md#geological-units) the cell's centre falls in |
| **Distance to sinkholes** | Metres to the nearest sinkhole | Every **Sinkhole**, drawn as a point or as an outline. A cell inside an outline is at 0 m |
| **Cave-entrance density** | Entrances per km², smoothed | Every cave entrance |

How each is worked out, where it matters to what you write:

- **Distances stop at 5,000 m.** A run looks 5 km past the edge of the area for faults and
  sinkholes, so one just outside still counts for the cells along that edge. Nothing farther is
  read, so no cell claims to be farther than that: a cell with no fault within 5 km holds 5,000 m,
  and so does every cell of an area where no fault has been recorded at all.
- **Rock type is empty where nothing is mapped.** A cell under no geological unit, or only under a
  unit whose lithology is not in [the list](geology.md#the-lithology-list), has no rock type. Where
  units overlap, the smaller outline wins, so a unit mapped in detail shows through the regional one
  around it.
- **Density is smoothed widely on purpose.** Each entrance is spread over a bell-shaped
  neighbourhood two cells wide and never narrower than 500 m. Drawn more tightly it would be a dot
  on every known entrance, and a model weighing it would only say *caves are where the caves are*.
- **Only what you may place goes in.** See [Running a model](#running-a-model).

### What every evidence layer cannot say

- **An empty surface is not evidence of absence.** No sinkhole within 5 km may mean there are
  none, or that nobody has recorded them. The application cannot tell the two apart and does not
  try.
- **Entrance density follows the cavers as much as the caves.** It is highest where people have
  looked. Weighing it heavily draws a map of past effort.
- **Layers are not independent.** Slope, ruggedness and roughness largely repeat each other; a
  model weighing all three counts steep ground three times.

---

## Writing a model

**New model** opens the editor. A model has a **name**, an optional **description**, whom it is
**shared with**, its **evidence layers** — each with how it is scored — and how the layers are
weighed against each other.

A model names kinds of evidence and numbers. It never names a place: no area, no feature, no
coordinate. Where it is run is chosen when it is run.

### Scoring a layer

A layer's raw values — metres, degrees, a rock class — are first turned into **scores between 0
and 1**: 0 is the least favourable ground, 1 the most. There are three ways, and each layer
offers the ones that make sense for it.

| Scored by | For | How it works |
|---|---|---|
| **A table of classes** | Any measured layer | Between 2 and 20 rows. Each row reads *Up to* a value and gives a score; the last row is *Everything above*. A value exactly on a bound belongs to the class that starts there |
| **A ramp between two values** | Any measured layer except **Aspect** | The *value that scores 0* and the *value that scores 1*. Scores rise in a straight line between them and stay flat beyond either end |
| **A score per class** | **Landform** and **Rock type** | A score for each class. A class left empty scores 0 |

**A ramp can run either way.** Put the larger value first when smaller values are the favourable
ones. The editor's own example:

> from 500 m down to 0 m scores ground on a fault as 1

**Aspect takes a table and never a ramp.** A bearing of 359° and one of 1° are neighbours; a ramp
from 0 to 360 would score due north as both the least and the most favourable ground. Its class
bounds lie between 0 and 360.

**A class you do not score is scored 0**, not left out. Leaving *Sandstone* empty in a rock-type
layer says sandstone is unfavourable; it does not say *no opinion*.

### Weighing the layers

Under **How the layers are weighed**, one of two methods.

**Weights given outright.** Each layer gets a number above zero. Only the proportions matter:
2, 1, 1 and 50, 25, 25 are the same model. The list shows each layer's share as a percentage.

**Pairwise comparisons.** Instead of naming weights, you compare the layers two at a time, and the
weights follow from the comparisons. This is the *analytic hierarchy process* (AHP), described
next.

### Pairwise comparisons and the consistency ratio

Naming five weights that sum to something sensible is hard. Saying which of two things matters
more, and roughly by how much, is easier. With pairwise comparisons the editor asks that question
once for every pair of layers — *Slope compared with Rock type* — on a fixed scale:

| You choose | Customarily read as |
|---|---|
| **Equally important** | The two count the same |
| **3 × more important** | Moderately more |
| **5 × more important** | Strongly more |
| **7 × more important** | Very strongly more |
| **9 × more important** | Extremely more — the scale's limit |
| 2, 4, 6, 8 × | Between two of the above |
| **N × less important** | The same judgments the other way round |

You only ever answer each pair once. If slope is 3 × more important than rock type, rock type is
taken as a third as important as slope; the two can never be entered contradicting each other.

From the full set of answers the application works out the one set of weights that fits them best
and shows it under **Weights**, as it does for weights given outright. Three layers need three
comparisons, five need ten, eight need twenty-eight.

**The consistency ratio** is shown beside the weights. It measures one thing: *how far your
answers contradict each other*.

Suppose you say slope is 3 × more important than rock type, and rock type is 3 × more important
than distance to faults. Followed through, slope should be about 9 × more important than distance
to faults. If you then answer that pair with 2 ×, the three answers cannot all hold at once. Some
contradiction is normal — nobody judges in exact multiples — and the ratio says how much there is,
against what filling the answers in at random would give:

| Ratio | What the editor says |
|---|---|
| **0** | The answers agree perfectly. Always the case with one or two layers, where there is nothing to contradict |
| **0.1 or less** | *"The comparisons agree with each other well enough (0.1 or less)."* |
| **Above 0.1** | *"Above 0.1 the comparisons contradict each other more than is customary; consider revising them. The model can still be saved and run."* |

**Above 0.1 is a warning, never a refusal.** The model saves and runs. A rule that refused it would
only teach people to nudge numbers until the warning went away.

What the ratio does **not** say:

- **It does not say the judgments are right.** A set of answers can agree with itself perfectly
  and be wrong about caves. Consistency is about the answers, not about the ground.
- **It does not say which answer to change.** Look for the pair that does not follow from the
  others — usually the one answered last, or the one between the two layers you find hardest to
  compare.
- **It is not a quality score for the map.**

### Sharing, copying, changing

| **Shared with** | Who can read the model, run it and copy it |
|---|---|
| **Only me** | You |
| **Members of** a caving group | That group's members |
| **Everyone signed in** | Every account |

- **Only the author can change or delete a model** (and a full administrator). Somebody it is
  shared with can run it and **copy** it. The copy is theirs and private — *"Copied. The copy is
  yours and private."* — and changing it leaves the original as its author wrote it.
- **Sharing a model shares a way of weighing ground and nothing about where anything is.** It holds
  no place.
- **Deleting a model keeps its maps**: *"Delete this model? Maps already computed from it are
  kept."*
- A model that weighs any layer [from what has been recorded](#from-what-has-been-recorded) is
  tagged **Uses recorded positions** in the list, and the editor and the run form each say what
  that means for its maps.

---

## Running a model

**Run** on a model's row asks for two things.

| Field | |
|---|---|
| **Area** | Type the name of an area. Any feature that is an outline will do — a karst area, a massif, a work area — provided you may read it and see its exact outline |
| **Cell size (m)** | Between 5 and 1,000 m; 50 unless you change it. The map has one value for each square of this size |

**A large area needs a larger cell.** A run holds at most one million cells, counted over the
rectangle around the area. Asked for more, it is refused with the limit and nothing is queued.
A cell finer than the installation's elevation adds no detail: it repeats the same terrain values
over several cells.

The run is then **Queued**, and computed by the installation's terrain worker — the one that makes
terrain builds and pictures of the ground, one job at a time, so it may wait behind them. The row
under **Maps computed** moves through **Queued**, **Computing** and **Ready** (or **Failed**)
without the page being reloaded.

What a run does, and what it holds on to:

- **It reads as you.** Features go into the evidence only if *you* may read them and see their exact
  position, with your rights as they are when the run starts — not when you asked. A feature you may
  not place is left out before anything is computed; it is not drawn somewhere approximate. Two
  members running the same model over the same area can therefore get different maps, and both are
  correct for what each may see.
- **It keeps the model as it was run.** The map records the model's layers, scoring and weights at
  that moment. Changing or deleting the model afterwards does not change the map, and a map never
  silently means something its model no longer says.
- **It records what it was made from**: the account it ran for, every recorded feature that went
  into its evidence, the elevation build and the computed pictures of the ground (with their
  settings) it read, and the date it was computed. The page shows the account and the date; the
  list of features is never shown, and is what decides [who may read the map](#maps). When the
  elevation build a map was made from is no longer the active one, the map says so, and running the
  model again reads the current ground.
- **It is a dated picture, not a live layer.** New entrances, a redrawn geological unit or a
  recomputed picture of the ground do not change a map already made. Run the model again; the two
  maps then sit side by side, told apart by their dates.

### Reading a finished map's row

| Column | |
|---|---|
| **Model** | The model's name when it was run, with **Uses recorded positions** where that applies |
| **Area** | The area, linked to its page |
| **Cell size** | As asked |
| **State** | And, once ready, a line such as *"8412 of 9105 cells scored · mean 0.47"* |
| **Date** | When the map was computed |
| **Asked for by** | *You*, or the member who ran it |

**Cells scored** is how much of the area the map covers. It falls short of the whole wherever
some layer had no value — see [Blank cells](#blank-cells). A map with far fewer cells scored than
the area holds is mostly blank, and the mean is then the mean of a small part of it.

**Show on the map** opens the map over the area with that map switched on. The bin deletes the
map; only whoever asked for it, or a full administrator, can.

### When a run fails

| The row says | What happened |
|---|---|
| *Some of the model's evidence is not available here* — followed by the layers and reasons | See the next table |
| *No part of the area had a value from every layer.* | Every cell was missing at least one layer: typically a rock-type layer over ground with no geological units, or an area outside the terrain |
| *The account that asked for it may no longer read what the run needs.* | Between asking and running, the account lost the terrain permission or sight of the area |
| *The area can no longer be covered at that cell size.* | The area's outline was changed, or removed, after the run was asked for |
| *The model as it was run is not one that can be run.* | Open the model, save it again, and run it again |
| *The map could not be computed.* | Anything else, including a run the worker gave up on. Ask again |

The reasons given for evidence that is not available:

| Reason | Meaning | Who can fix it |
|---|---|---|
| `no_terrain` | The installation draws no terrain build | An administrator, on the [Terrain page](../admin/terrain-builds.md) |
| `no_layer` | That picture of the ground has not been computed for the terrain in use, or is out of date | An administrator, under [Derived pictures](../admin/terrain-builds.md#derived-pictures-of-the-ground) |
| `slope_unit` | Steepness exists only as a percentage; a model scores it in degrees | An administrator: ask for a Steepness picture in degrees |
| `too_many_features` | More than 50,000 features of one kind lie in reach of the area | Run over a smaller area |

A failed run leaves no map. It is not tried again on its own.

---

## The map

In the map's **layers panel**, under **Cave potential**, every map you may read is listed by its
model's name, with its area, cell size and date. Tick one to draw it.

> Maps of where the evidence a model weighs is most favourable, each over one area on the date
> shown.

- It is drawn **beneath** the caves, entrances and outlines, and partly see-through, so they are
  read against it and not hidden by it.
- The key runs from pale yellow, **Less favourable**, to dark blue, **More favourable**. It is one
  family of colours going from light to dark, deliberately not green to red: this is not a map of
  *safe* and *danger*.
- The same model run twice over one area gives two rows. **Only the date tells them apart.**
- Which maps are switched on is not kept in a [saved view](saved-views-and-windows.md#saved-views),
  and a shared view's link never draws one.

### Blank cells

A cell is left clear, with the base map showing through, in two cases:

- **Outside the area.** The map's edge is the area's edge, whatever evidence lies beyond it.
- **Where any one layer of the model had no value** — off the edge of the elevation, or under no
  mapped geological unit for a model that weighs rock type.

A cell missing one layer is **left blank rather than scored from the others**. Scoring it from the
rest would quietly rescale their weights, and two neighbouring cells would then be coloured by two
different models with nothing on the map to say so. **Blank is not *unfavourable*.** It means the
model had no answer there.

### What the map claims

Each cell's value is the model's scores for that cell, combined by the model's weights: a number
from 0 to 1. That is all. Under every map the key says:

> A weighing of evidence as its model describes it, not a finding: nothing here has been checked on
> the ground. For use inside the group only.

### What the map never claims

- **It is not a probability.** 0.8 does not mean an eight-in-ten chance of a cave. It means the
  model's author scored that ground highly.
- **It has not been tested against known caves.** Nothing on this page measures how well a model
  finds caves that are already known, and no map comes with an accuracy figure.
- **It does not discover anything.** A model can only return its author's judgment, applied evenly
  over the area. Its use is in applying that judgment consistently to ground nobody has walked, and
  in making the judgment explicit enough to argue about.
- **Two maps from two models cannot be compared value for value.** 0.6 under one model and 0.6
  under another are not the same statement.
- **It is no finer than its coarsest evidence.** A 10 m cell over coarser elevation and a regional
  geology map is still a coarse answer about a regional outline.
- **It is not a reason to skip the ground.** High-scoring ground with no cave and low-scoring ground
  with one are both ordinary.

---

## Who can see what

### Models

Exactly whom the author chose under **Shared with**: only the author, a caving group's members, or
everyone signed in. A model holds no place, so sharing one discloses nothing about where anything
is.

Running a model does not widen that audience. A reader who may open a map but not its model sees
the map and its colours, and no model name, layers or weights; the author of the run and full
administrators always see the recipe the map was made from.

### Maps

**Nobody chooses who sees a map — not its author, not an administrator.** There is no *shared
with* on a map. Whether you may read one is asked afresh every time you look, from what you may
see today. You may read a map when all of these hold:

| | Rule |
|---|---|
| 1 | You have permission to read **terrain** |
| 2 | You may read **the area** it was run over and see its exact outline — the map draws that outline |
| 3 | The run is **finished**. A queued, computing or failed run is shown only to whoever asked for it and to full administrators |
| 4 | For a map tagged **Uses recorded positions**: you may, today, read and see the exact position of **every** recorded feature the run was computed from |

A map weighed from the shape of the ground alone was computed from no recorded feature, so rule 4
does not apply to it: everybody who may read terrain and place the area sees it.

Why rule 4 is that strict: a map weighed from recorded positions shows where they are. The ground
nearest a sinkhole scores highest; the ground densest in entrances stands out. Somebody who passes
rule 4 could have made the same map from what they can already place, so reading it tells them
nothing new. Somebody who does not would be learning a protected position from a colour.

What follows from it:

- **Every feature counts, not only the protected ones.** Protection can be switched on after a map
  was made; the question is asked of the whole list as things stand now.
- **The member who ran it is asked the same question as everybody else.** A member who has since
  lost sight of a protected cave loses the maps made from it too — their own included.
- **A map made from a feature that has since been deleted** can be read only by a full
  administrator, until it is deleted and the model run again.
- **A map you may not read is not shown as locked. It is not shown at all**, and asking for it by
  its address answers exactly as for a map that does not exist. Nothing tells you a map was made
  over ground you cannot see.
- **Two members can see different lists** on the same page, and neither list says how many maps the
  other holds.
- **If a colleague's map does not appear for you, run the model yourself.** Your run reads only
  what you may place, so the map you get is one you are entitled to — made from less.

Under a map made from recorded positions the key adds:

> Made from recorded positions, some of which may be protected; you see it because you may place
> all of them. Do not pass it on.

**That last sentence is the part the application cannot enforce.** A screenshot of such a map,
sent to somebody who could not have opened it, hands them the positions it was made from.

### What is never done with a map

- It is **not exported**, as a file or in any other form.
- It is **not on a shared view's link**, a public page or a published trip.
- It is **not shown to anybody without an account**.
- It is **not carried in a saved view**.

---

Related: [Karst analyses](karst-analyses.md) · [Geology](geology.md) · [Terrain](terrain.md) ·
[Pictures of the ground, for administrators](../admin/terrain-builds.md#derived-pictures-of-the-ground) ·
[Location protection](../admin/location-protection.md) · [Map workspace](map-workspace.md)
