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

A model can also **learn from known entrances**. Its author then names no scores and no weights:
each run counts how the cave entrances already recorded are spread over the evidence, and tests
the map it comes to against entrances it was not shown. That kind is described under
[Models that learn from known entrances](#models-that-learn-from-known-entrances); the rest of
this page applies to it except where that section says otherwise.

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

Under **How the layers are weighed**, one of four methods. The first two are weighed by judgment
and are described here. **Frequency ratio** and **Weights of evidence** take no weights from you at
all — see [Models that learn from known entrances](#models-that-learn-from-known-entrances).

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
- A model that learns from known entrances is tagged **Learns from known entrances** instead, and
  the list shows its layers with no weights: it has none until it is run.

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
| **State** | And, once ready, a line such as *"8412 of 9105 cells scored · mean 0.47"*. A map learnt from known entrances adds *"learnt from 31 entrances, tested on 13 · AUC 0.842 / 0.731"* — see [The two curves and the AUC](#the-two-curves-and-the-auc) |
| **Date** | When the map was computed |
| **Asked for by** | *You*, or the member who ran it |

**Cells scored** is how much of the area the map covers. It falls short of the whole wherever
some layer had no value — see [Blank cells](#blank-cells). A map with far fewer cells scored than
the area holds is mostly blank, and the mean is then the mean of a small part of it.

**Show on the map** opens the map over the area with that map switched on. The bin deletes the
map; only whoever asked for it, or a full administrator, can. On a map learnt from known
entrances, **Weights and curves** opens [what the run counted](#reading-the-weight-tables) under
the row.

### When a run fails

| The row says | What happened |
|---|---|
| *Some of the model's evidence is not available here* — followed by the layers and reasons | See the next table |
| *No part of the area had a value from every layer.* | Every cell was missing at least one layer: typically a rock-type layer over ground with no geological units, or an area outside the terrain |
| *The account that asked for it may no longer read what the run needs.* | Between asking and running, the account lost the terrain permission or sight of the area |
| *The area can no longer be covered at that cell size.* | The area's outline was changed, or removed, after the run was asked for |
| *The model as it was run is not one that can be run.* | Open the model, save it again, and run it again |
| *Too few known entrances in the area, among those the account may place, to keep some back and learn from the rest.* | A model that learns from known entrances found fewer than ten cells holding an entrance you may place. See [Why few entrances make the numbers unreliable](#why-few-entrances-make-the-numbers-unreliable) |
| *The known entrances leave nothing to tell apart in this area.* | A model that learns from known entrances had no ground without an entrance to compare with, or its map could not be tested. Use a smaller cell or a larger area |
| *The map could not be computed.* | Anything else, including a run the worker gave up on. Ask again |

The reasons given for evidence that is not available:

| Reason | Meaning | Who can fix it |
|---|---|---|
| `no_terrain` | The installation draws no terrain build | An administrator, on the [Terrain page](../admin/terrain-builds.md) |
| `no_layer` | That picture of the ground has not been computed for the terrain in use, or is out of date | An administrator, under [Derived pictures](../admin/terrain-builds.md#derived-pictures-of-the-ground) |
| `slope_unit` | Steepness exists only as a percentage; a model scores it in degrees | An administrator: ask for a Steepness picture in degrees |
| `too_many_features` | More than 50,000 features of one kind lie in reach of the area — or, for a model that learns from known entrances, more than 50,000 entrances lie in the rectangle around it | Run over a smaller area |

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

For a model weighed by judgment, each cell's value is the model's scores for that cell, combined
by the model's weights: a number from 0 to 1. That is all. (What a map learnt from known entrances
holds is under [The map of a model that learns](#the-map-of-a-model-that-learns).) Under every map
the key says:

> A weighing of evidence as its model describes it, not a finding: nothing here has been checked on
> the ground. For use inside the group only.

### What the map never claims

- **It is not a probability.** 0.8 does not mean an eight-in-ten chance of a cave. On a map
  weighed by judgment it means the model's author scored that ground highly; on a map learnt from
  known entrances it means the ground ranks high *on that map*.
- **A map weighed by judgment has not been tested against known caves.** It comes with no figure
  for how well it finds them. A map learnt from known entrances comes with two — and they are
  narrower than they look: see [What the AUC does not mean](#what-the-auc-does-not-mean).
- **It does not discover anything.** A model weighed by judgment can only return its author's
  judgment, applied evenly over the area. Its use is in applying that judgment consistently to
  ground nobody has walked, and in making the judgment explicit enough to argue about. A model
  that learns can only return the pattern of the entrances already recorded — which is the pattern
  of where people have looked as much as of where caves are.
- **Two maps from two models cannot be compared value for value.** 0.6 under one model and 0.6
  under another are not the same statement.
- **It is no finer than its coarsest evidence.** A 10 m cell over coarser elevation and a regional
  geology map is still a coarse answer about a regional outline.
- **It is not a reason to skip the ground.** High-scoring ground with no cave and low-scoring ground
  with one are both ordinary.

---

## Models that learn from known entrances

A model weighed by judgment says what its author believes. A model that **learns** asks the
registry instead: *over this area, in which kinds of ground are the known cave entrances commoner
than the ground itself?* It draws the map that follows from the answer, and then checks that map
against entrances it kept back.

Two methods do this. Both are counting, worked out in one pass: nothing is fitted or tuned, so the
same area, entrances and settings give the same figures on every run.

| Method | Each class of a layer gets | Each cell gets |
|---|---|---|
| **Frequency ratio** | One number: the class's share of the entrances over its share of the ground | The sum of the ratios of the classes it falls in, one per layer — an index |
| **Weights of evidence** | Two weights, their contrast, and how sure that contrast is | A probability worked out from the weights |

The frequency ratio is the easier to read and to explain. Weights of evidence says more — above all
how far each figure can be trusted — and is the one to prefer when the tables themselves are what
you are after. Both sets of columns are shown on every such map; the method decides only how the
map is drawn.

### Writing one

In the editor, choose **Frequency ratio** or **Weights of evidence** under **How the layers are
weighed**. The editor then asks for less:

- **A measured layer is cut into classes, and that is all.** Each row is only *Up to* a value.
  There is no score to fill in, because what each class is worth is what a run counts. There is no
  ramp either: entrances are counted per class, and a ramp has none. A layer you had scored by a
  ramp is shown as its table of classes, and going back to a method weighed by judgment brings the
  ramp back.
- **Landform and Rock type need nothing**: *"Every class of this layer is counted; there is
  nothing to fill in."*
- **No weights and no comparisons**: *"The weights of this model are counted from known entrances
  when it is run, and are shown with each map."*
- **Cave-entrance density is not offered**, and a model that already weighs it cannot be saved as
  one that learns. That layer is made from the very entrances the model counts, the kept-back ones
  included: the model would be shown the entrances it is then tested on.

Two fields appear:

| Field | |
|---|---|
| **Entrances kept back for testing (%)** | Between 10 and 50; 30 unless you change it. This share is never shown to the model |
| **Seed of the draw** | A whole number, 0 or above; 1 unless you change it. It decides *which* entrances are kept back |

> The entrances kept back are never shown to the model; how well its map finds them is its
> prediction rate. The same area, entrances, share and seed keep back the same ones on every run.

The seed is there to make the draw repeatable, not to be tuned. Anything that changes which cells
hold an entrance changes the draw as well: another cell size, a newly recorded entrance, a
different account's rights.

**Choose the classes with care; they are the model.** A model that learns has no opinion about
slope, only about the classes you cut slope into. A few wide classes can blur a real difference;
many narrow ones leave each with a handful of entrances and a figure that means little — see
[Why few entrances make the numbers unreliable](#why-few-entrances-make-the-numbers-unreliable).

Like every model, one that learns names no place and holds no entrance. Sharing it shares a list of
layers and class bounds, and nothing about where anything is.

### What a run counts

1. **The ground.** The cells inside the area for which *every* layer of the model has a value. A
   cell missing one layer is left out of the counting as well as off the map, so every layer's
   table is a table of the same ground.
2. **The entrances.** Every cave entrance *you* may read and place exactly that lies on that
   ground. One you may not place is left out, never moved somewhere approximate. One outside the
   area, or on a cell some layer has no value for, is not used at all.
3. **Occupied cells.** What is counted is cells, not entrances. A cell either holds an entrance or
   does not, and two entrances in one cell are one occupied cell.
4. **The split.** The seed parts the occupied cells into those the model **learns from** and those
   **kept back**. The tables are counted from the first part only.
5. **The tables, the map and the two curves**, described below.

A run needs at least **ten occupied cells**. That is the floor below which nothing can be kept
back — not a number at which the results become worth reading.

### Frequency ratio

For one class of one layer:

> **frequency ratio** = the class's share of the occupied cells ÷ the class's share of the ground

| Ratio | Reads as |
|---|---|
| **1** | The class holds entrances in proportion to its size. It says nothing either way |
| **Above 1** | It holds more than its share; 2 is twice what its size alone would give |
| **Below 1** | It holds fewer than its share |
| **0** | No entrance the model learnt from lies in it. That is *not* "no caves there" |

An invented example: an area of 10,000 cells, 40 of them occupied by entrances the model learns
from, and a slope layer cut into three classes.

| Class | Cells | With an entrance | Share of the ground | Share of the entrances | Frequency ratio |
|---|---|---|---|---|---|
| below 10 | 6,000 | 12 | 60 % | 30 % | 0.50 |
| 10 up to 25 | 3,000 | 20 | 30 % | 50 % | 1.67 |
| 25 and above | 1,000 | 8 | 10 % | 20 % | 2.00 |

The steepest class is a tenth of the ground and holds a fifth of the entrances, so its ratio is 2.

A cell's value on the map is the **sum** of the ratios of the classes it falls in, one per layer.
A cell that is average on each of three layers comes to 3. It is an index: it puts the ground in
order, and is not a probability, a count or a density.

### Weights of evidence

The same counts, asked a sharper question: *how much likelier is a cell of this class among the
occupied cells than among the rest?*

| Column | What it is | How to read it |
|---|---|---|
| **W+** | The natural logarithm of: the share of occupied cells that are in the class, over the share of unoccupied cells that are | Above 0, entrances are commoner in the class than outside it; below 0, rarer; 0, no difference |
| **W−** | The same for the ground *outside* the class | Usually of the opposite sign to W+, and smaller |
| **Contrast** | C = W+ − W− | How strongly the class goes with the entrances, in one number. 0 is not at all; the sign says which way |
| **s(C)** | The standard deviation of the contrast, from the four counts behind it | It is large whenever one of those counts is small |
| **C / s(C)** | The contrast in units of its own standard deviation — the *studentised* contrast | *"Above about 2 the class tells the entrances apart from the ground with some confidence."* Near 0 the contrast could well be chance |

The same example:

| Class | Frequency ratio | W+ | W− | Contrast | s(C) | C / s(C) |
|---|---|---|---|---|---|---|
| below 10 | 0.50 | −0.695 | 0.563 | −1.258 | 0.346 | −3.64 |
| 10 up to 25 | 1.67 | 0.514 | −0.338 | 0.851 | 0.317 | 2.69 |
| 25 and above | 2.00 | 0.697 | −0.118 | 0.815 | 0.397 | 2.06 |

**Read the last column first.** Gentle ground is clearly poorer in entrances (−3.64). The steepest
class has the highest ratio and the weakest support of the three: it rests on eight cells, and its
C / s(C) is only just above 2. A large contrast with a small C / s(C) is a pattern in a handful of
cells.

**A cell's value on the map is a probability.** It starts from the chance that a cell picked blind
is occupied — the occupied share of the ground — and is moved up or down by the W+ of the cell's
class on each layer. With a single layer it is exactly the share of the class's cells that are
occupied: 8 in 1,000 for the steepest class above.

Two cautions about that probability:

- **It is the chance that a cell of this size holds an entrance like the ones the model learnt
  from.** It is small everywhere, because entrances are rare, and it changes with the cell size. It
  is not the chance of finding a cave by walking there.
- **Adding the weights of several layers assumes the layers say independent things.** Layers that
  repeat each other — slope, ruggedness and roughness — are counted more than once, and the
  probabilities come out too high. The page measures this for you:

  > The probabilities add up to 71.3 occupied cells against the 40 learnt from. A sum far above
  > means the layers repeat each other and the probabilities are too high; the ranking is still
  > usable.

  Layers that are independent give back the number learnt from. When the sum is far above it, read
  the map as an order of the ground and nothing more, or take out one of the layers that repeat
  each other.

### Reading the weight tables

**Weights and curves**, on a finished map's row, opens what the run counted. First come a few
lines:

- *"Learnt from 31 entrances in 29 cells; 13 entrances in 12 cells kept back."*
- *"30 % kept back, seed 1 · 8412 cells of ground weighed"*
- how many of the entrances used were location-protected, when any were;
- what 0 and 1 on the map stand for (see [The map of a model that learns](#the-map-of-a-model-that-learns));
- for weights of evidence, what the probabilities add up to.

Then the two curves, and one table for each layer with a row for each class.

- **Cells** and **With an entrance** are the two counts every other column follows from. Look at
  them before anything else on the row.
- **adjusted**, beside a class's name, means one of the four counts behind its weights was nought —
  usually a class no entrance lies in. Its weight would be infinite, and a class that holds no
  known cave is not thereby proven to hold none; so a half is added to each of the four counts, for
  that class only. The weights on an adjusted row stand for *very few*, and are not a measurement.
  The frequency ratio needs no such help: it is simply 0.
- **A dash (–)** means there was nothing to count. A class no cell of the ground falls in has
  dashes throughout. A class that *every* cell falls in has a ratio of 1, a W+ of 0 and dashes
  after them: there is no ground outside it to compare with.
- **Classes of a measured layer are named by their bounds** — *10 up to 25*. The bounds belong to
  the model, so a reader who may read the map but not its model sees *Class 1*, *Class 2* instead.
  Landforms and rock types keep their names.

Under the tables the page repeats the rule for reading them:

> A frequency ratio above 1 and a positive W+ mean entrances are commoner in the class than on the
> ground as a whole. Cells are counted, not entrances: two entrances in one cell count once.

### The map of a model that learns

A probability of a few thousandths and a sum of ratios with no ceiling cannot be drawn on a scale
from 0 to 1 as they are. So the map is **stretched**: its least favourable cell is drawn as 0, its
most favourable as 1, and the rest in between, in the same order. **Weights and curves** says what
the two ends stand for:

> On the map, 0 stands for a probability of 0.000412 and 1 for 0.0187.

Under such a map the key adds:

> Its colours run from the least to the most favourable ground of this one map; they rank the
> ground and cannot be set beside another map's.

What follows:

- **The darkest ground is the best of this map, not good ground.** On a map where nothing stands
  out, something is still drawn dark blue.
- **Two such maps cannot be compared colour for colour** — not two areas, not two models, not the
  same model with another seed.
- **The mean on the map's row** is the mean of the stretched values, and says little.
- A map whose every cell came to the same value ranks nothing, and is drawn as 0 throughout.

[Blank cells](#blank-cells) are as on any other map.

### The two curves and the AUC

How good is the map? Put its cells in order from the most favourable down, and walk down the list.
At each step note two things: how much of the ground has been taken so far (across), and how many
of a set of occupied cells have been found on it (up). That is a **rate curve**, and the page draws
two of them.

| Curve | Drawn against | What it says |
|---|---|---|
| **Success rate** | The occupied cells the model learnt from | How well the map fits what it was shown. It always flatters |
| **Prediction rate** | The occupied cells kept back | How well the map finds entrances it never saw. *"The prediction rate is the honest one."* |
| **Chance** (dashed) | — | What a map that knew nothing would draw: a fifth of the ground holds a fifth of the entrances |

To read a point: where the prediction-rate curve passes 20 % across and 60 % up, the most
favourable fifth of the ground holds three fifths of the entrances that were kept back.

The **AUC** — the area under the curve — puts a whole curve into one number between 0 and 1.

| AUC | |
|---|---|
| **About 0.5** | No better than chance |
| **Towards 1** | The entrances sit on the ground the map ranks highest. 1 itself is out of reach |
| **Below 0.5** | Worse than chance: the map ranks those entrances *low*. With few kept back this happens by accident |

Loosely, it is the chance that an occupied cell picked at random ranks above a cell of ground
picked at random. The row of a finished map gives both: *"AUC 0.842 / 0.731"* is the success rate,
then the prediction rate.

- **A gap between the two is expected; a wide one is a warning.** A success rate far above the
  prediction rate means the model has fitted the particular entrances it was given — narrow classes
  and few entrances do this — and not something that holds for the next entrance.
- **The curves are made of straight steps.** A map made from classes has few different values and
  many cells of each; cells of equal value are taken together, as one step.
- **No point of a curve stands for an entrance.** Each curve is at most 101 points, every one a sum
  over a share of the ground.

### What the AUC does not mean

- **It is not the share of caves the map will find, and not an accuracy.** 0.8 does not mean the
  map is right four times in five.
- **It is tested on known entrances, and known entrances are where people have looked.** The
  entrances kept back come from the same register as the ones learnt from: the same valleys, the
  same paths, the same clubs' ground. A map that has learnt *where cavers go* does well on the
  prediction rate. A high AUC cannot tell "this finds caves" from "this finds past effort".
- **Ground without a known entrance is not ground without a cave.** The methods count every
  unoccupied cell as empty. Nothing here records ground that was searched and found empty, so
  nothing here can be tested against it.
- **It is one draw.** Another seed keeps back other entrances and gives another AUC. Copy the
  model, change the seed and run it again a few times: if the figure moves by a tenth from one seed
  to the next, that spread is how well you know it.
- **It belongs to this area, this cell size and these entrances.** It says nothing about the same
  model somewhere else. An area that takes in a lot of plainly unfavourable ground — the plain
  beside the karst — raises the AUC without the map being any better where it matters.
- **It belongs to the account that ran it.** Two members who may place different entrances get
  different tables and different figures from the same model, and neither is the wrong one.
- **It ranks; it does not locate.** A good curve says the favourable fifth of an area holds most of
  the entrances. It does not say where in that fifth.

### Why few entrances make the numbers unreliable

Every figure on these pages is a ratio of counts, and the counts of entrances are small.

- **One entrance moves a class a long way.** Take a class covering a tenth of the ground, in a run
  that learns from 12 occupied cells. With one of them in the class its frequency ratio is 0.83;
  with two, 1.67; with three, 2.5. One more survey weekend turns *poorer than average* into *twice
  the average*. **s(C)** and **C / s(C)** exist to say exactly this — which is the reason to read
  the weights-of-evidence columns even on a frequency-ratio map.
- **A class with no entrance is not measured at all.** Its ratio is 0 and its weights are
  *adjusted*: a stand-in, not a finding.
- **Every class needs entrances of its own.** Thirty occupied cells over a table of ten classes is
  three to a class on average, and each further layer divides the same thirty again.
- **The test is smaller still.** With ten occupied cells and 30 % kept back, three cells test the
  map. Their curve has at most three steps, and three cells can come to an AUC of 0.85 or of 0.35
  by chance alone.
- **Only the entrances you may place are counted.** An account that may place fewer learns from
  fewer, and gets rougher tables for the same area.
- **The cell size changes the count.** A larger cell merges neighbouring entrances into one
  occupied cell.

The floor of ten occupied cells is where the arithmetic becomes possible. There is no number at
which it becomes safe: read **Cells**, **With an entrance** and **C / s(C)** on every row, and
treat a row counted from a handful of cells as saying little, whatever its ratio.

With few entrances such a model is still good for two things: seeing which classes plainly hold
entrances and which plainly do not, and setting what the register says beside what a model weighed
by judgment assumes. It is not good for telling apart two patches of ground that both rank high.

---

## Who can see what

### Models

Exactly whom the author chose under **Shared with**: only the author, a caving group's members, or
everyone signed in. A model holds no place, so sharing one discloses nothing about where anything
is.

Running a model does not widen that audience. A reader who may open a map but not its model sees
the map and its colours, and no model name, layers or weights; the author of the run and full
administrators always see the recipe the map was made from. On a map learnt from known entrances
the counted tables are part of the result, so such a reader does see them: which layers were
counted and each class's figures, with the classes numbered — but not the model's name, nor the
bounds it cut the classes at.

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

A map weighed by judgment from the shape of the ground alone was computed from no recorded
feature, so rule 4 does not apply to it: everybody who may read terrain and place the area sees it.
A map **learnt from known entrances** is always under rule 4, whatever its layers — see
[Maps learnt from known entrances](#maps-learnt-from-known-entrances).

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

### Maps learnt from known entrances

A map from a model that learns is **always** tagged **Uses recorded positions**, even when every
layer it weighs comes from the shape of the ground. The entrances it counted are positions, and a
map of where entrances are common says where they are.

So rule 4 always applies to it, and the features it asks about include **every entrance the run
learnt from and every entrance it kept back**, along with anything recorded that went into its
layers. Protected or not: the question is whether you may read each one and see its exact position
today.

| | |
|---|---|
| **What a run reads** | Only entrances the account that asked may read and place exactly, with its rights as they are when the run starts. One it may not place is left out before anything is counted |
| **What a run records** | The account it ran for, whether that account was a full administrator, how many entrances it learnt from and how many it kept back, and how many of them were location-protected. The entrances themselves are kept by the server, only to ask rule 4, and are never shown |
| **What is governed** | The map, the line of figures on its row, the weight tables and the two curves — together. Nobody sees the curves of a map they may not open |
| **What never appears** | An entrance's position or cell, a figure for a single entrance, or a list of entrances. Every table row is a class; every point of a curve is a share of the ground |

**Why the tables and curves are held as closely as the map.** They are sums over the same
entrances. A weight table says which kinds of ground the entrances are on; over a small area, a
class with one occupied cell among a few cells of ground comes close to pointing at it. Somebody
who may place every entrance could have counted the same table from what they can already see.
Somebody who may not would be learning about a protected position from a row of figures.

What follows, beyond what holds for [every map made from positions](#maps):

- **Losing sight of one entrance loses the whole result** — whichever side of the split it was on,
  and for the member who ran it as for anybody else.
- **A member who may place fewer entrances gets a model learnt from fewer.** A colleague's richer
  map does not appear for them, and nothing says it exists. Their own run gives a map they are
  entitled to, with its own tables and its own AUC.
- **The figures of two accounts are not two measurements of the same thing.** Before comparing
  maps or AUCs with a colleague, check you both learnt from the same number of entrances.
- When any entrance the run used was location-protected, the page says so, as a count:

  > Location-protected entrances among those used: 4. You see this because you may place all of
  > them. Do not pass it on.

- **A screenshot of a weight table or of the curves is passed on like a screenshot of the map.**
  It carries the same entrances.

### What is never done with a map

- It is **not exported**, as a file or in any other form.
- It is **not on a shared view's link**, a public page or a published trip.
- It is **not shown to anybody without an account**.
- It is **not carried in a saved view**.

---

Further reading on the two methods that learn: G. F. Bonham-Carter, *Geographic Information
Systems for Geoscientists* (1994), chapter 9, for weights of evidence; C.-J. F. Chung and
A. G. Fabbri, "Validation of spatial prediction models for landslide hazard mapping", *Natural
Hazards* 30 (2003), for the success-rate and prediction-rate curves.

Related: [Karst analyses](karst-analyses.md) · [Geology](geology.md) · [Terrain](terrain.md) ·
[Pictures of the ground, for administrators](../admin/terrain-builds.md#derived-pictures-of-the-ground) ·
[Location protection](../admin/location-protection.md) · [Map workspace](map-workspace.md)
