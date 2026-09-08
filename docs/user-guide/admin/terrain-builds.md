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
obtained and prepared is still on disk under it — but **that particular build cannot be taken
further**. You start a new one once the service is running.

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
belongs in a directory on the server. The form will not let you start a build while uploads
are still in flight — it names the files that have not arrived yet.

**A directory on the server.** One the operator has listed as readable, or one below it.
Everything in it that looks like an elevation raster is read. If no directories are
configured it says so.

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

### 5. Heights

**Heights are measured from:** *Above sea level* (orthometric) or *Above the ellipsoid*.

> **Getting it wrong lifts or drops the whole surface by tens of metres.**

For ellipsoidal heights, give the **geoid height** — how far the local sea-level surface sits
above the ellipsoid there.

### 6. Start

**Start build.** It is queued.

---

## Watching a build

Status: **Queued · Running · Finished · Failed.**

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

---

## Managing builds

The **Builds** list shows state, which one is **being drawn**, area, depth, when it was
submitted, and **size on disk** — *"everything the build left behind: the published tiles and
the converted rasters kept beside them."*

| Action | |
|---|---|
| **Draw this** | The scene now draws this build |
| **Stop drawing** | The scene draws bare ground again |
| **Delete** | Removes the build and the disk it is keeping |

Two refusals worth knowing:

- **You cannot delete the build the scene is drawing.** Choose other terrain, or stop drawing
  it first.
- **You cannot delete a running build.** Its files are being written right now; it can be
  removed once it has stopped, whichever way it stops.

---

## Pictures drawn from a build

A finished build's elevation can also be turned into **pictures of the ground** — shaded relief,
steepness, facing, ruggedness, topographic position, roughness, or a colour relief. They are
computed once by the installation, on the same worker that makes builds, and then everybody with
permission to read terrain sees them in the map's layer list under **Ground pictures**: switch one
on, and it draws beneath the caves.

Each picture belongs to the build it was drawn from. **When you activate a different build, every
picture drawn from the old one is marked out of date** and says so beside its name in the layer
list. It is still there and still draws — an out-of-date shaded relief is often better than none —
but you can see at a glance that the ground beneath it has been replaced, which is the one thing
that would otherwise look like a fault in the cave data.

Two notes on what this is and is not:

- **Asking for a picture is currently done through the interface's programming interface, not from
  a page.** The Terrain page does not yet have a form for it. Once a picture exists, everything a
  reader does with it — switching it on, its transparency, its size, the out-of-date mark — is in
  the layer list.
- **Some things cannot be computed here and are not offered under a borrowed name.** Geomorphons,
  curvature, and flow direction, flow accumulation and the wetness index are not produced by the
  elevation library this installation uses, so they are absent rather than approximated by something
  that resembles them.

The files sit under the build itself, so **deleting a build deletes its pictures with it**, and
their size is reported beside them in the layer list.

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

---

## From the command line instead

The whole thing can also be done in one documented step from the command line, without the
application's Terrain page. See the deployment documentation.

---

Related: [Terrain](../features/terrain.md) · [3D view](../features/3d-view.md)
