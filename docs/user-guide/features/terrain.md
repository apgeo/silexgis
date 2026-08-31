# Terrain

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

## What it costs to have

Terrain is built by an administrator, one rectangle at a time. Free 30-metre elevation
coverage is obtained by the application, or your own rasters are supplied. Each build is
baked into a tile pyramid served as static files by the same installation.

Two things worth knowing as a non-administrator:

1. **Building tiles needs an extra service** that a plain installation does not run. If yours
   does not, builds stop at that step and the Terrain page explains exactly that, along with
   the one line to add to the deployment's environment file.
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

For the whole build workflow, see [Building terrain](../admin/terrain-builds.md).
