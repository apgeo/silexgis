# Terrain

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/terrain.md)

[← Feature reference](README.md) · Related: [3D view](3d-view.md) ·
Administration: [Building terrain](../admin/terrain-builds.md)

---

Terrain is the real relief under the [3D view](3d-view.md). Without it the globe is a smooth
sphere and your caves sit under a featureless surface; with it they sit under actual
hillsides.

**It is entirely optional.** An installation that wants none of it is unaffected, downloads
nothing and contacts no elevation server.

---

## What it means for you as a reader

If your installation has terrain, you will notice:

- the 3D scene has hills,
- the cutaway view is meaningful, because there is something to cut,
- the depths a survey was recorded at read against a real surface.

If it does not, the 3D view still works. It says so plainly rather than failing.

Where the installation holds more than one build — a coarse one over the region and a finer one
over a massif, say — the 3D view lets you choose which one it draws, and offers a finer one when
you stop over ground it covers. See
[Choosing between builds](3d-view.md#choosing-between-builds).

## Pictures of the ground on the map

Terrain is not only for the globe. An administrator can have a finished build turned into
**pictures of the ground** — shaded relief, steepness, facing, ruggedness, topographic position,
roughness, a coloured relief or contour lines — and everybody who may read terrain then finds them
in the flat map's layer list under **Ground pictures**. Switch one on and it is drawn beneath the
caves.

- Each picture is listed by its name, with what kind of picture it is and how much disk it takes
  beside it, so a steepness map and a facing map given the same name can still be told apart. A
  picture still being computed is listed but cannot be switched on yet.
- **Out of date** beside a picture means the ground beneath it has changed since it was computed —
  the installation draws another build now, or the way that kind of picture is worked out has since
  been corrected. It is still drawn, because an old shaded relief is usually better than none; point
  at the badge for which of the two it is. Do not read a disagreement between such a picture and
  the caves as a fault in the cave data.
- **They are pictures, not data.** Nothing can be measured along a contour line, selected or
  exported, and no line carries its height. Contour lines stay smooth until the map is zoomed in
  past about 1:4,000; closer than that they show their steps.

## What it costs to have

Terrain is built by an administrator, one rectangle at a time. Free 30-metre elevation
coverage is obtained by the application, or your own rasters are supplied. Each build is
baked into a tile pyramid served as static files by the same installation.

Two things worth knowing as a non-administrator:

1. **Building tiles needs an extra service** that a plain installation does not run. If yours
   does not, builds stop at that step and the Terrain page explains exactly that, along with
   the one line to add to the deployment's environment file. Once it runs, the stopped build is
   run again from where it stopped, not started over.
2. **Depth is a trade-off.** Each level roughly quadruples both the tiles written and the
   time taken. Level 13 or so is what 30-metre coverage actually holds; asking for more from
   that data adds nothing but disk.

## Attribution

Every build carries a **credit** — who the data belongs to — and it is written into the
published surface. A build must never carry a credit that is not its own. If you are
supplying rasters, you have to say where they came from.

## Heights

A build declares whether its numbers are **above sea level** (orthometric) or **above the
ellipsoid**, and for the latter, the local geoid height. Getting this wrong lifts or drops
the whole surface by tens of metres — which then makes every cave look like it is in the
wrong place vertically.

---

For the whole build workflow, and for asking for pictures of the ground, see
[Building terrain](../admin/terrain-builds.md). Related: [3D view](3d-view.md) ·
[Map workspace](map-workspace.md)
