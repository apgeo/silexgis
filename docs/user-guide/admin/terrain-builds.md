# Building terrain

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/terrain-builds.md)

[← Administration](README.md) · Related: [Terrain](../features/terrain.md) ·
[3D view](../features/3d-view.md)

---

**Administration → Terrain.** Turning elevation data into the ground the
[3D scene](../features/3d-view.md) draws.

> *"Draw a rectangle, say where its elevation data comes from, and the installation builds the
> ground the 3D scene draws."*

Entirely optional. Without it the globe is a smooth sphere, which needs no elevation server
and downloads nothing.

---

## Before you start: the extra service

**Turning rasters into tiles needs a service of its own, which a plain installation does not
run.**

Obtaining elevation data and preparing it work as they are. Baking does not. If your
installation does not run that service, a build **stops at the bake step**, and the Terrain
page then shows you exactly what to do — the one line to add to the environment file beside
the deployment's compose files, and the command to run from that directory.

Note what it also says: everything before that step **still happened**, and what the build
obtained and prepared is still on disk under it. Once the service is running, press **Run again**
on the stopped build: it carries on from the bake, without obtaining or preparing anything a second
time. Nothing re-queues it on its own — only a person knows the service is now there.

---

## Making a build

### 1. Draw the rectangle

**Draw rectangle**, drag it on the map. It reports the extent (west, south, east, north) and
the area in square degrees.

There is a maximum. A rectangle larger than the installation builds in one go is refused —
build it as several smaller areas.

### 2. Say where the elevation data comes from

Three sources, and you can combine them:

**Obtain coverage for this rectangle.** Thirty-metre elevation data covering the whole land
surface, fetched by the application. *"Sea returns nothing, which is a normal answer rather
than a failure."*

**Rasters from this computer.** Drop elevation rasters in, up to 512 MB each. Anything larger
belongs in a directory on the server. Each file is sent the moment you choose it, and the form will
not let you start a build while uploads are still in flight — it names the files that have not
arrived yet. A raster you sent and then left out of every build is deleted after a day (the
installation can set another time); one that any build names — queued, running, finished or
stopped — is kept as long as that build is.

**A directory on the server.** One the operator has listed as readable, or one below it.
Everything in it that looks like an elevation raster is read. If no directories are
configured it says so. Only a **Full Administrator** may name one.

The rasters in a server directory are **read where they are, never copied and never written to** —
a national survey already on the machine is not worth holding twice, and nothing the build does,
including deleting it, touches that directory. What the build keeps instead is a list of what it
found there the first time. If it is run again later and a raster has since been added, removed or
changed, it stops rather than carrying on over rasters it was not made from: put the directory back
as it was and run it again, or start a new build, which reads the directory as it now is.

### 3. Credit

**Who the data belongs to.**

> *"It is written into the published surface, so a build must never carry a credit that is not
> its own."*

Required if you supplied rasters yourself. Also record the **licence**.

### 4. Depth

How fine the tile pyramid goes. The form describes each band rather than making you guess:

| Band | |
|---|---|
| **Coarse** | A broad shape of the landscape. Quick, small on disk, but a hillside reads as one slope |
| **Coverage** (≈ level 13) | As much detail as the thirty-metre coverage actually holds. **The working default** |
| **Fine** | Finer than obtained coverage. Worth asking for only where rasters of a few metres cover the rectangle |
| **Survey** | What half-metre survey data earns |

> **Each level roughly quadruples both the tiles written and the time taken.**

If you ask for more than your sources hold, the form says **"Nothing here can fill those
levels"** — deeper levels are neither refused nor silently dropped, they simply add disk for
no detail.

**What you got is reported afterwards.** The tile maker stops descending where the sources run out
of detail, whatever depth was asked for — thirty-metre coverage gives out around level 13 even when
16 was requested. Once a build has been checked, the **Depth** column of the Builds list shows the
deepest level its tiles really reach, and when that is less than what you asked for the request is
shown beside it (*13 (asked for 16)*). A build that has not been checked yet shows the depth asked
for.

### 5. Heights

**Heights are measured from:** *Above sea level* (orthometric) or *Above the ellipsoid*.

> **Getting it wrong lifts or drops the whole surface by tens of metres.**

For ellipsoidal heights, give the **geoid height** — how far the local sea-level surface sits
above the ellipsoid there.

### 6. Start

**Start build.** It is queued.

Beside the button, a note says what decides how fine the result can be:

> *"Memory decides how fine the terrain can be. The tile maker uses half of its own container's
> memory — 8 GB unless this installation sets SILEXGIS_TERRAIN_WORKER_MEMORY — and when that is not
> enough it does not fail: it makes coarser terrain than was asked for. A build that ran short stops
> and says so, and the setting can then be raised."*

---

## Adding a raster to a build you already have

A new survey over ground that is already baked does not have to cost the whole region again. When
you start a build, you can name a **finished build to extend**. The new build starts from a copy
of that build's tiles, the tile-maker adds your new rasters to the copy without re-meshing what is
already there, and the result is checked and published as a build of its own.

**What it costs.** The new rasters' tiles, and a copy of the base's pyramid on disk. For one fine
island over a baked region that is minutes rather than hours: the price follows what you add, not
the size of the region.

**The depth must be the base's.** The addition meshes only the new rasters; it cannot re-mesh the
tiles already there at a different depth, so an extension asked for at another depth is refused
rather than half done. The heights are measured the way the base's are, for the same reason.
Coverage is not obtained again unless you ask for it — the base already holds it — so an
extension has to name at least one raster, or ask for coverage of new ground.

**The base is kept.** It stays in the list, drawable and unchanged; the extension is a separate
build, at a separate address, carrying the base's sources and credit as its own together with
whatever you added. You can delete the base once the extension has finished — not before, because
until then the extension still reads the base's tiles.

**A base has to be whole.** Only a build that finished its check and whose tiles are still where
terrain is served from can be extended; one that is still running, stopped before its check, or
has had its tiles removed is refused, and the message says which.

---

## Watching a build

Status: **Queued · Running · Finished · Failed** — and, for a build somebody called off, **Being
cancelled** until it stops and **Cancelled** after, rather than a red failure.

Phases, in order:

| Phase | |
|---|---|
| *Waiting its turn* | Nothing has started |
| **Obtain** | Fetching coverage |
| **Prepare** | Converting rasters |
| **Bake** | Making tiles — the step that needs the extra service |
| **Check** | Reading the tiles back and verifying they are whole |
| **Publish** | |

A build's detail page shows what it was **made from**, and **what the tool said** — the log,
which is where a failure explains itself.

### Calling a build off, and running it again

**Call off**, on a queued or running build, asks behind a confirmation: *"What it has already
obtained and prepared is kept, so it can be run again later from where it stopped."* A queued build
stops at once. A running one stops at the first point it can — between two files, or, while tiles
are being made, once the tile maker has said it stopped — and is shown as *Being cancelled* until
then. Whatever tiles that bake had written are thrown away, so nothing half-made is left to be taken
for a finished surface. A build that has started publishing finishes; a called-off build is never
published.

**Run again**, on any stopped build — called off, or stopped for any other reason — puts it back on
the queue, and it carries on from the first step whose work is not already on disk: a build that
stopped at the bake makes tiles and nothing else. A bake that went wrong had its tiles thrown away,
so it is baked again. Run again is refused, with a sentence saying why, while another build of the
same rectangle is unfinished, for a build that extended a build since deleted, and — briefly — for a
build called off while making tiles whose tile maker has not yet confirmed it stopped; that answer
arrives as soon as the tile-making service is running.

Both need the same right that starts a build: **execute** on terrain.

### Two limits a bake can reach

**Memory.** When the tile maker runs short of memory it does not fail — it makes coarser terrain
than the depth you chose, which nothing on the map would ever point out. The application reads the
tool's report after every bake, so such a build is not published: it stops with *The tile maker ran
short of memory, so the terrain would have been coarser than asked for*, its tiles are discarded,
and the page gives the one line to add to the environment file and the command that applies it.
Then press **Run again**.

**Time.** One bake may run for twelve hours unless the installation allows longer. A bake that
reaches the limit — one that stopped making progress, or a large area that simply needed longer —
is stopped, its tiles are discarded, and the build says how long it ran against which limit:
*Time limit reached after 12 h (limit 12 h)*, with the setting that raises it. The setting is in
seconds; the question mark beside it — *How long is that in seconds?* — lists the usual lengths:

| | Seconds |
|---|---|
| 1 hour | 3600 |
| 12 hours (the default) | 43200 |
| 1 day | 86400 |
| 2 days | 172800 |
| 1 week (the longest allowed) | 604800 |

Set it, restart the application, and press **Run again**; what the build obtained and prepared is
kept.

---

## Managing builds

The **Builds** list shows state, which one is **being drawn**, area, depth, when it was
submitted, and **size on disk** — *"everything the build left behind on disk: the published tiles and
the converted rasters kept beside them."* Beneath that figure the list gives the **tiles alone**
(*1.1 MB of tiles*), which is all a viewer is served; the difference is what the converted rasters
and working files cost, kept so that the build can be run again. Deleting the build gives back the
whole size on disk, tiles included. Builds checked before the tiles-alone figure was recorded show only the larger one.

| Action | |
|---|---|
| **Draw this** | The scene now draws this build |
| **Stop drawing** | The scene draws bare ground again |
| **Call off** | Stops a queued or running build, keeping what it obtained and prepared |
| **Run again** | Puts a stopped build back on the queue, carrying on from where it stopped |
| **Delete** | Removes the build and the disk it is keeping |

Two refusals worth knowing:

- **You cannot delete the build the scene is drawing.** Choose other terrain, or stop drawing
  it first.
- **You cannot delete a running build.** Its files are being written right now; it can be
  removed once it has stopped, whichever way it stops — **Call off** stops it.

One state worth knowing about, because nothing refuses it: **if the installation's configuration
names a terrain address of its own** (`SILEXGIS__Terrain__Url`), that address wins over whichever
build is chosen here. The build is recorded as being drawn and the scene draws the configured
pyramid instead. The Terrain page then shows a warning above the list naming the overridden build
and the setting; the chosen build is drawn once that setting is removed and the application
restarted.

**The page also warns when the terrain being drawn carries no credit.** Elevation data almost
always has to be credited — Copernicus and most other free models require it — and the scene shows
whatever credit the drawn terrain carries, so terrain with none is drawn with nothing beside it and
nothing refuses that. For a terrain address named in the configuration, the warning names the
setting that states the credit (`SILEXGIS__Terrain__Attribution`); add it and restart. For a build,
the credit comes from the sources it was made from, each of which states one when the build is
started: the application refuses a source with no credit, so a build made here always has one, and
the warning appears only for a build recorded some other way. A credit cannot be added to a
finished build — start one over the same ground with its sources credited, and choose that one.

---

## Derived pictures of the ground

A finished build's elevation can also be turned into **pictures of the ground** — shaded relief,
steepness, facing, ruggedness, topographic position, roughness, a colour relief, or contour lines.
They are computed once by the installation, on the same worker that makes builds, and then
everybody with permission to read terrain sees them in the map's layer list under **Ground
pictures**: switch one on, and it draws beneath the caves.

### Asking for one

The **Derived pictures** card on the Terrain page, below the builds. Asking needs the right to
**execute** on terrain — the same right that starts a build. Without it the card is still shown,
greyed out, with a line saying which right is missing; the register beneath it stays readable.

| Field | |
|---|---|
| **Build** | Which finished build to draw from. Only builds that ran to the end are offered, and the form opens on the one the scene is drawing — a picture of any other ground would be out of date the moment it was finished |
| **Picture** | Which kind. Each kind shows only the settings it reads |
| **Name** | What the layer list calls it. It follows the kind until you type one of your own |

The settings, by kind:

- **Shaded relief**: one light or four. Under a single light every slope facing away from it is
  flat black, and a doline in that shadow is invisible; four lights keep it readable. For one
  light, where it stands — clockwise from north, with the north-west as the convention — and how
  high. **Height exaggeration** multiplies every height first; past a few times a gentle landscape
  is a wall of saturated pixels, and the form refuses more than a hundred.
- **Steepness** and **facing**: the slope arithmetic (Horn, for real and noisy elevation, or
  Zevenbergen–Thorne, sharper on clean surfaces) and, for steepness, degrees or percent. A steepness picture drawn as a percentage is coloured
  across 0 to 200 per cent (100 per cent is a 45-degree slope; steeper ground is drawn at the end
  of the ramp), and one drawn in degrees across 0 to 90.
- **Ruggedness**: Riley's definition, which published ruggedness classes are stated against, or
  Wilson's, which gives different numbers — the two are never comparable.
- **Coloured relief**: a colour at each height, in metres. At least two stops, no two at the same
  height; the ground between two heights is painted with the blend.
- **Contour lines**: how many metres of height lie between one line and the next — from 1 to 1000,
  and 20 unless you say otherwise — counted from sea level, with every fifth line drawn heavier.
  The lines are a picture like the others, clear between the lines, and not data: nothing can be
  measured along one, selected or exported, and no line carries its height. The picture is drawn
  four times as fine as the elevation (about seven metres a dot from one-arc-second data), so the
  lines stay smooth until the map is zoomed in past about 1:4,000; closer than that they show their
  steps. A picture computed before 2026-10-21 was drawn twice as fine — request it again for the
  smoother one.
- Every kind but contour lines: whether the outermost cells are computed from the neighbours they
  have. Off, every raster of a build meets its neighbour with a hairline gap through the picture.

**Request picture** queues it. Asking again for a picture the installation already holds — same
build, same kind, same settings — answers with the one that exists rather than computing it twice.

### How steepness and facing are computed

Both are measured **on the ground, not on the grid of degrees the elevation is held in**: a degree
of longitude is narrower than a degree of latitude — by nearly a third at Carpathian latitudes —
so every row of a raster is computed with the two distances a degree really spans at that row's
latitude, and a hillside facing east reads as steep as the same hillside facing north. Nothing is
reprojected or resampled on the way, so the picture sits exactly on the elevation it was drawn
from, and shaded relief is lit using the same two distances.

### The register

The **Register of derived pictures** lists every picture the installation holds: its name and
kind, the **parameters** it was computed with (only those its kind reads), the **build** it came
from, its **state** (*Queued · Computing · Ready · Failed* — a failure explains itself in the
worker's own words), what it keeps **on disk**, and when it was requested. The list re-reads itself
while anything is still being computed.

Each picture belongs to the build it was drawn from. **When you activate a different build, every
picture drawn from the old one is marked "Out of date"** — in the register and beside its name in
the layer list. It is still there and still draws — an out-of-date shaded relief is often better
than none — but you can see at a glance that the ground beneath it has been replaced, which is the
one thing that would otherwise look like a fault in the cave data.

A picture is marked the same way when **the way its kind is computed has since been corrected**:
shaded relief, steepness and facing computed before they were measured on the ground are. The hint
under the badge says which of the two reasons it is. For this one the remedy is to **ask for the
same picture again** — same build, same kind, same settings — which computes it afresh in place of
the old one.

**Delete**, behind a confirmation that names what it frees, removes the picture and its rasters.
It needs the right to **delete** on terrain. The files sit under the build itself, so **deleting a
build deletes its pictures with it**.

**Some things cannot be computed here and are not offered under a borrowed name.** Geomorphons,
curvature, and flow direction, flow accumulation and the wetness index are not produced by the
elevation library this installation uses, so they are absent rather than approximated by something
that resembles them.

---

## Common problems

| Message | Means |
|---|---|
| *That area is already being built* | Wait for it |
| *A build has to be made from something* | Obtain coverage, or supply rasters |
| *That file is not an elevation raster this installation reads* | Wrong format |
| *An elevation tile says where it is only by what it is called* | An `.hgt` must be named like `N45E024.hgt` for the square whose corner is 45°N 24°E |
| *That rectangle is not a rectangle on the Earth* | Draw it again |
| *That build has no surface to draw* | Its tiles were never read back and found whole, or they are no longer where terrain is served from |
| *Obtaining the elevation cell … failed: the connection … could not be made or was lost* (or *did not finish answering within the time allowed*) | The server could not reach the elevation data, or the link dropped or stalled. Check outbound access, then **Run again**; cells already fetched are kept |
| *An extension is built to the depth of the build it extends* | Ask for the base's level, or start a fresh build |
| *Another build that extends this one has not finished and still reads its tiles* | Wait for that build to stop, whichever way it stops, before deleting this one |
| *That build has already stopped, so there is nothing to call off* | It finished or failed on its own first |
| *Only a build that has stopped can be run again* | It is still queued or running, or it finished |
| *That build was called off while its tiles were being made, and the tile maker has not yet said it stopped* | Start the tile-making service; it answers at once, and the build can then be run again |

---

## From the command line instead

The whole thing can also be done in one documented step from the command line, without the
application's Terrain page. See the deployment documentation.

---

Related: [Terrain](../features/terrain.md) · [3D view](../features/3d-view.md)
