# 3D view

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
terrain, beside that cave's centerlines.

- It is loaded **only for the cave you select**.
- Switching it off in the layer list genuinely lets go of it — it does not sit in graphics
  memory pretending to be off.
- The panel tells you what stage it is at: looking for a model, loading, drawn (with the
  triangle count), still converting, none uploaded, or failed with a reason.

See [Surveys, centerlines and 3D models](surveys-and-models.md#cave-walls-stl) for uploading
and for the coordinate declaration a `.stl` needs.

---

## The camera

| Control | Does |
|---|---|
| **Frame the cave in view** | Fits the selected cave |
| **Look straight down (plan)** | The plan view |
| **View from the north / south / east / west** | Fixed elevations |
| **Remove perspective** | Orthographic projection, so distances read the same near and far |

## Centerlines with no depths

A centerline surveyed in plan, with no altitudes recorded, is drawn **on the surface** and
the scene says how many are in that state. It is not drawn at depth zero as if the cave were
flat — that would be a statement about the cave that nobody made.

## Raster overlays in 3D

Georeferenced raster sheets can be draped on the globe. Note the difference from the flat
map: **on the globe each sheet is flattened to a single picture**, so it softens under close
zoom. The flat map reads the file itself. If you are examining detail on a geological sheet,
use the 2D map.

---

Related: [Terrain](terrain.md) · [Map workspace](map-workspace.md) ·
[Surveys and models](surveys-and-models.md)
