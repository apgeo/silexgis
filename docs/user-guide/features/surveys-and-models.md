# Surveys, centerlines and 3D models

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/surveys-and-models.md)

[← Feature reference](README.md) · Related: [3D view](3d-view.md) ·
[Measurements and statistics](measurements-and-statistics.md)

---

SilexGIS displays surveys; it does not compile them. You bring the output of Therion or
Survex, and the application draws it, measures it, projects it on the map, and keeps the
sources you made it from.

Everything here lives on a **cave's page**.

---

## 3D survey models

**Upload model.** Accepted: **Therion `.lox`**, **Survex `.3d`**, or **cave walls as a binary
`.stl`** — up to 100 MB.

State: *Waiting its turn → In progress → Ready*, or *Could not be processed*.

A model is drawn in the **survey viewer** (CaveView.js) and, for walls, in the
[3D scene](3d-view.md).

### Reading a survey into rows

A compiled survey is not left as a file the browser draws. It is **read into its stations and
shots**, which is what makes the [statistics](measurements-and-statistics.md) possible. The
plot can be viewed while that is happening; the list updates itself when the reading is done.

### Where a survey sits in the world

This is the part people get wrong, so the forms are explicit about it.

- **A `.lox` file has no field for a coordinate system**, so it never says where it is. You
  must say what its numbers mean, or the survey cannot be placed.
- **A `.3d` file states its own** only when the survey was compiled with one. If it does,
  that is used and what you say is ignored.

### Coordinate systems come from the installation, not the internet

When you give an EPSG code — for a `.stl`, or for a survey whose file does not state its own —
the definition is resolved **from the coordinate-system database that ships with the
application**, not fetched from a public web service.

Two things follow:

- **An installation with no route out still georeferences surveys correctly.** Nothing makes an
  outbound request at the moment a survey is read.
- **National grids work.** Romanian Stereo70 (EPSG:31700) is the obvious case: it is not
  hard-coded in the viewer, and on an installation that had to fetch definitions it would lose
  its georeferencing or fail to load at all.

If you are used to surveys quietly losing their position on a self-hosted system, this is why
that does not happen here.

### Precision lost before arrival

If the coordinates in a file were too large for the numbers it stores them in, the survey
arrives coarser than it was surveyed. The application detects this and says so:
*"The file lost precision before it arrived."* Re-exporting about a local origin recovers it.

---

## Cave walls (`.stl`)

An `.stl` file is bare triangles. Nothing in it says which coordinate system its numbers are
in — read as the neighbouring zone it would land kilometres away. So you declare it:

**Metres from a point** — the usual export. The numbers are metres east, north and up from
one point. Give that point's **longitude and latitude**, and the **altitude of the file's
zero level**.

> The form pre-fills the origin from **this cave's main entrance**, which is where a local
> export usually starts. If no entrance position is recorded, or if the position shown to
> *your* account is deliberately approximate because the cave's location is protected, it
> says so and asks you to enter the true point.

**A projected coordinate system** — the numbers are eastings and northings in a national or
UTM grid. Give the **EPSG code**.

Also note: the heights in these files are measured from the export's own origin, not from sea
level.

Once converted, the walls are drawn in the [3D scene](3d-view.md#walls-of-the-selected-cave)
for the selected cave only.

---

## Centerlines

A cave's line work, projected onto the map surface and drawn at depth in 3D.

**Upload centerline**, or let one be **extracted** from a compiled survey — the *Source*
column says which.

Each centerline shows its **length** and its number of **paths**. One can be made the cave's
**default shape**, which is what other things use when they need "the shape of this cave".

On the map, centerlines are the expensive layer and have their own detail and budget
controls — see [Map workspace](map-workspace.md#centerline-detail).

A centerline with no surveyed altitudes is drawn **on the surface** in 3D, and the scene says
how many are in that state — rather than drawing it at depth zero as if the cave were flat.

---

## Survey sources

The material the compiled surveys were made from. **Archive a source**, up to 100 MB:

| Kind | |
|---|---|
| **Therion source** | `.th`, `.th2` |
| **Therion `.thconfig`** | |
| **Compilation log** | `.log` |
| **Survex source** | `.svx` |
| **Survey app export** | a `.zip` |

Each carries a size, a revision and a description, and can be downloaded.

> **Nothing here is read or interpreted.** They are kept so the survey can be compiled again
> when the tools that produced the export have moved on. That is the whole purpose: in ten
> years the `.lox` may be unreadable and the `.th` will not be.

The file's contents are checked against what its name claims — a `.svx` that is not a `.svx`
is refused rather than archived under a lie.

Removing a source from the cave's archive keeps the stored file itself.

---

## What the survey then tells you

Reading a survey into stations and shots is what feeds:

- **Survey statistics** on the cave page — length, plan length, vertical extent, greatest
  span, verticality, sinuosity, highest and lowest points, and the computed figures against
  the declared ones,
- the **passage orientation rose** and inclination,
- **closest approach** between two caves,
- centerlines on the map and in 3D.

All of that is on [Measurements and statistics](measurements-and-statistics.md).

---

Related: [3D view](3d-view.md) · [Caves and entrances](caves-and-entrances.md) ·
[Measurements and statistics](measurements-and-statistics.md)
