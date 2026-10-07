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
[3D scene](3d-view.md). Walls are either a `.stl` you upload or, where the survey itself holds
enough to build them, [made from the `.lox` or `.3d`](#walls-from-a-lox-or-3d).

### The current model

A cave keeps every model ever uploaded — uploads are never overwritten, so a corrected re-export
is a second model beside the first. One of them carries the **Current** mark, per kind: the
current **line plot** (`.lox` / `.3d`) is what the map's extracted centerline and the
[measurements](measurements-and-statistics.md) read; the current **walls** (`.stl`) are what the
3D scene draws — and where a cave has no current `.stl`, the scene draws the walls built from its
current line plot instead. The **first** model of a kind is current and stays so until you choose
another — a second upload may be a corrected re-export or a survey of one side passage, and only
you know which — so use **Make current** on the model that should take over, once it has finished
processing. For a line plot the choice also makes its centerline the cave's shape on the map, and
making a survey's centerline the default does the same from the other side: the map and the
figures always describe the same survey. Deleting the current model passes the mark to the newest
one left.

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

### Comparing two surveys

A cave that was surveyed twice keeps both line plots, and the survey viewer can show them
together. Open one of them — **View in 3D**, beside the map, or in a window of its own — and,
when the cave has another `.lox` or `.3d`, **Compare with…** appears above the model. Choose the
other survey and how the two are shown. You can change between the two ways while comparing, and
**Stop comparing** returns to the one survey you opened.

- **Overlaid** draws both surveys in one view, each in a colour of its own — blue for the survey
  you opened, orange for the one it is compared with — under a key that names which is which. A
  passage that was resurveyed lies on its earlier self, so what was added or moved is what stands
  out in one colour alone. Where the two coincide exactly only one colour can show, and it is the
  orange of the survey being compared with, which is drawn over the one you opened: hide that part
  of it to see the blue beneath.
- **Side by side** gives each survey a view of its own, under its name — one above the other
  where there is no room across. **Link views**, on to begin with, makes turning, tilting, zooming
  or moving one model do the same to the other; turn it off to look at each on its own.

**Showing and hiding parts.** Under the models is a list for each survey of the named surveys it
is made of, with a tick box each and **Show all**. With **Sync** on, ticking or unticking a part
does the same to the part of the same name in the other survey; a part only one of them has is
shown or hidden on its own. With it off, each list acts on its own survey only. It works the same
overlaid and side by side, and what you hid stays hidden when you change between them. The list is
the first level of a survey's structure that offers a choice — a survey exported as a single piece
is listed as that one piece.

**Two surveys in different coordinates can only be compared side by side.** The viewer draws every
file where its own numbers put it. Two surveys measured from the same fixed point, or exported in
the same grid, land on one another. If one is in metres from a point of its own and the other in a
national grid — or each from a different point — they are drawn hundreds of kilometres apart, and a
picture of that is not a comparison of anything. So before laying one over the other the
application checks that the two lie in the same place; if they do not, it says so and offers them
side by side, where each survey is framed on its own and nothing has to line up. The check cannot
see a small misplacement: two surveys measured from points a few metres apart are overlaid, and are
off by those few metres. For an overlay you can trust, export both from the same fixed point or in
the same coordinate system.

Comparing is a way of looking. Nothing about it is saved, and it does not change which model is
current.

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
for the selected cave only. The model list shows the converted mesh's size in megabytes beside
its triangle count, and the scene names the size while it fetches, so a fifty-megabyte mesh on a
metered connection is a choice rather than a surprise.

---

## Walls from a `.lox` or `.3d`

You do not have to export an `.stl` to see walls. When a line plot is read, walls are built for
that same model out of what the file itself holds, and out of nothing else:

- **A `.lox` with scraps** — the wall surfaces Therion modelled from your drawings — is drawn with
  exactly those surfaces.
- **A `.lox` without scraps, or a `.3d`**, gets a tube along every leg whose **passage dimensions**
  (left, right, up, down) were measured at both of its ends. Each measured station becomes a ring
  through its four measured points — left and right laid level across the passage, up and down
  plumb above and below the station — and consecutive rings are joined. On a leg steeper than 60°
  the ring is laid across the leg instead, so a pitch is not drawn as a ribbon.

**A leg nobody measured gets no walls.** There is no default passage size. A leg with no
dimensions, or with dimensions at one end only, stays a bare line; a single distance that was not
taken is drawn as no distance at all rather than guessed. Splays get no tube either — they are
measurements *of* the wall, not passages. A survey with neither scraps nor dimensions simply has
no walls: that is not an error, and the model is still **Ready**.

The model list shows the triangle count and the size on the plot's own row, with the note *"Walls
built from this survey's own scraps and passage dimensions"*. A very large system is drawn with
four-sided rings instead of eight-sided ones; one too large even for that — more than two million
triangles — gets no built walls, and an exported `.stl` is then the way to show it.

**Which walls the 3D scene draws.** An uploaded `.stl` marked **Current** still wins: it is walls
somebody made on purpose. Without one, the scene draws the walls of the **current line plot**;
failing that, the newest model of the cave that has walls at all.

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

> **Only one kind is read, and only for what it says about the compilation.** A compilation
> log is opened for the loop errors it reports (see *Survey closure* below); everything else is
> kept as bytes and never interpreted. That is the whole purpose: in ten years the `.lox` may be
> unreadable and the `.th` will not be. Nothing here is ever written back to.

The file's contents are checked against what its name claims — a `.svx` that is not a `.svx`
is refused rather than archived under a lie.

Removing a source from the cave's archive keeps the stored file itself.

### Survey closure

When a compilation log is archived, the installation reads what the compiler printed about that
run and shows it on the cave page — under the compiler's own headings, so a figure here and a
figure in your own log are visibly the same number.

For each closed loop the compiler reported: its **relative error** (`REL-ERR`, a ratio) and its
**absolute error** (`ABS-ERR`, a distance in metres), the loop's length, how many stations it
runs through, the per-axis components, and the station chain itself. **Both error measures are
shown together and neither is presented as the other** — they disagree about which loop is
worst, and the table always says which of them its current order uses.

Beside the table: the compiler and its version, the compiler's release date, when this
installation read the log, and which revision of the archived file was read. A corrected log
uploaded as a new revision of the same file is read again, and the previous run's figures are
cleared rather than left standing under the new revision.

The wording distinguishes cases that look alike and are not: a log nobody could open, a
compilation that stopped part-way, a survey with no closed loops, and a log that printed no loop
table at all.

Closure figures follow the survey record they belong to: a reader who may not be told a cave's
exact position is not shown them, on the map, in the panel or in the cave's history.

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
