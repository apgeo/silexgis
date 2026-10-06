# 3D view

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/3d-view.md)

[← Feature reference](README.md) · Related: [Terrain](terrain.md) ·
[Surveys, centerlines and 3D models](surveys-and-models.md)

---

The 3D view draws your installation's base layers on a globe, with cave surveys sitting in
the ground at the depths they were surveyed at.

It opens from the rail (**3D view**), from the map (**Show the 3D scene beside the map**), or
in a window of its own. The page is downloaded only when you open it, so it costs nothing to
people who never use it.

## Requirements and honesty about them

The 3D view needs **WebGL 2**. A browser or graphics driver without it gets an explanation
and a link to the 2D map, rather than a dead black canvas.

**No vendor service is contacted.** No third-party terrain, no third-party imagery, no
third-party geocoding. The 3D engine is served by your own installation and the basemap is
whichever base layers you configured — so an air-gapped installation needs a base layer it
can actually reach.

If the browser releases the view's graphics resources twice in quick succession, the view
stops and offers a reload rather than sitting there broken.

---

## The ground

Out of the box the globe is a **smooth sphere**. That needs no elevation server and downloads
nothing.

If your installation has built terrain, the globe has real relief. See
[Terrain](terrain.md) for what that involves, and
[Building terrain](../admin/terrain-builds.md) for how an administrator does it.

When terrain is configured but unreachable or malformed, the scene says so explicitly —
naming which of *unreachable*, *not a terrain tile set*, or *served with the wrong
compression* it hit — and either falls back to a pyramid the installation holds, or draws a
smooth globe. It does not pretend.

### Choosing between builds

An installation can hold more than one terrain build at a time — a coarse one over the whole
region and a finer one over the massif you work in, say. The scene draws the one the
installation marks as its default. To draw another, open the layer panel and look under
**Ground**: each build is listed by its level, the date it finished and the area it covers
(as width × height in km), with the configured elevation model first when there is one.

Switching is a real reload of the ground, and the surveys move with it: each build has its own
height datum, and the scene re-hangs every centerline, wall mesh and track from the surface it
is now drawing. A build you chose that cannot be read is refused with the same notice a
configured source gets — the scene does not quietly draw a different one under a control that
still names the one you picked. The choice lasts for the session; it is not saved and not in the
link.

### When finer ground covers where you are

When the camera comes to rest over an area that a finer build covers than the one drawn — or
over any build at all while the globe is the smooth sphere — a small notice offers it: *Finer
terrain covers this area — draw it?* **Draw it** makes the switch above; **Not now** puts that
build away for the rest of the session. The scene never switches by itself: a swap reloads the
ground while you are moving and changes the heights everything stands on.

---

## Seeing the cave through the ground

A survey is under a hillside, which is a problem for looking at it. Two answers:

**Show the cave through it.** The survey is drawn over the ground and stays visible from
every angle. How deep a passage is shows in the colour of the line — there is a depth legend,
*Depth below the top of the cave*.

**Cut it away.** The ground above the cave is cut away, so you look at the survey down an
opening in the surface.

The cutaway is honest about its own limits. If the opening is edge-on from your angle it
tells you to tilt down towards the cave. If your camera is below the surface it tells you
there is no ground left between you and the cave and to rise back above it. If the browser
cannot do the cutaway at all, or no survey is loaded to cut around, it says so and falls back
to showing the cave through the ground.

---

## Walls of the selected cave

If a cave has an uploaded **`.stl` wall model**, the 3D view can draw it in place, under the
terrain, beside that cave's centerlines. A cave without one is drawn with the walls
[built from its `.lox` or `.3d`](surveys-and-models.md#walls-from-a-lox-or-3d), where that survey
holds scraps or measured passage dimensions to build them from.

- By default it is loaded **only for the cave you select**. Under the **Cave walls** switch in
  the layer list you choose between that — *Selected cave* — and *Every cave in view*, described
  below.
- Switching it off in the layer list genuinely lets go of it — it does not sit in graphics
  memory pretending to be off.
- The panel tells you what stage it is at: looking for a model, loading, drawn, still
  converting, none uploaded, or failed with a reason.
- While the walls are fetched the scene says how big they are — *loading the walls — 54.7 MB* —
  so a fifty-megabyte model and a three-hundred-kilobyte one are different waits rather than the
  same silence. Once drawn, the size and the triangle count are both shown. A model converted
  before the server began measuring sizes is described by its triangle count alone.

See [Surveys, centerlines and 3D models](surveys-and-models.md#cave-walls-stl) for uploading
and for the coordinate declaration a `.stl` needs.

## Walls of every cave in view

Choosing **Every cave in view** under the **Cave walls** switch draws the walls of the caves the
camera is looking at, not only of the one you selected. A wall model is downloaded whole and
held in graphics memory for as long as it is drawn — usually a few hundred kilobytes, tens of
megabytes for a large system — so this mode works within limits and tells you when it reached
one:

- **Zoom in first.** Below a set zoom (14 unless your administrator changed it) nothing is
  loaded and the scene says *Zoom in to see the walls of the caves in view*. A wide view is a
  whole district's caves, each of which would be a speck that cost its full size.
- **Nearest first, up to a limit.** The caves nearest the middle of the view are taken first,
  until either a number of caves (12) or a total download size (64 MB) would be passed. The
  cave you have selected is always taken first, wherever in the view it is.
- **It never shows a subset silently.** The panel, and a line over the scene, say how many
  caves' walls are drawn out of how many are in view, and what kept the rest out — *Walls of 3
  of 7 caves in view — the rest would exceed 64 MB*. Move or zoom toward the caves you are
  missing and they take their turn.
- **What leaves the view is let go.** Walls of caves you pan away from are released, not hidden,
  and switching the walls off — or back to *Selected cave* — releases all of them.
- A cave whose exact location is closed to you is not drawn and is not counted.

Each cave's walls sit on that cave's own centerlines, exactly as in the other mode. The three
limits are settings of the installation; see `SILEXGIS__Map__MeshesInView…` in the
[installation guide](../../INSTALL.md#configuration-reference).

---

## The camera

| Control | Does |
|---|---|
| **Frame the cave in view** | Fits the selected cave |
| **Look straight down (plan)** | The plan view |
| **View from the north / south / east / west** | Fixed elevations |
| **Remove perspective** | Orthographic projection, so distances read the same near and far |
| **Save image** | Downloads what the scene is showing as a picture |

**Save image** downloads the view as a PNG named with the date and time it was taken
(`silexgis-3d-20261006-140509.png`), at the full resolution of your screen. The credits of the
basemap, of any overlay that is switched on and of the elevation model are written along the
bottom edge of the picture: the licences of those sources ask for the credit to travel with their
tiles, and on the page it is drawn beside the scene rather than in it. The buttons, the name
labels and the notices over the scene are not part of the picture. If the picture cannot be made
the scene says so instead of saving an empty file.

## Centerlines with no depths

A centerline surveyed in plan, with no altitudes recorded, is drawn **on the surface** and
the scene says how many are in that state. It is not drawn at depth zero as if the cave were
flat — that would be a statement about the cave that nobody made.

## Raster overlays in 3D

Georeferenced raster sheets can be draped on the globe. Note the difference from the flat
map: **on the globe each sheet is flattened to a single picture**, so it softens under close
zoom. The flat map reads the file itself. If you are examining detail on a geological sheet,
use the 2D map.

## Tracks from imported files

Files imported on the geodata screen — a GPX track, a KML outline — can be shown in the 3D
view too. Open the layer panel and tick the file under **Imported files**; each file has its
own fade. The choice is shared with the flat map: a file switched on in one view is on in the
other, drawn in the same colour.

On the globe a file is its lines — tracks and outlines; its waypoints are not drawn. A track
that recorded altitudes is drawn at them, on the same ground as the surveys, when the
installation has terrain. A track that recorded none, or whose altitudes are all zero, is
**laid on the ground**: it follows the hillside rather than sitting at sea level underneath
it. Clicking a track selects nothing — it has no page of its own.

---

Related: [Terrain](terrain.md) · [Map workspace](map-workspace.md) ·
[Surveys and models](surveys-and-models.md)
