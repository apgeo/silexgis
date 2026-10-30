# Georeferenced raster maps

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/georeferenced-maps.md)

[← Feature reference](README.md) · Related: [Map workspace](map-workspace.md) ·
[Geodata](geodata.md)

---

Geological sheets, old topographic maps, tourist maps, scanned cave maps — anything that is a
picture with a position — can be overlaid on the map.

**Cadastre → Geodata → Raster maps.**

---

## Uploading

Drag **georeferenced GeoTIFFs** in. They are converted to Cloud-Optimized GeoTIFF in the
background: *Queued → Processing… → Ready* (or *Failed*).

Each raster map carries:

| Field | |
|---|---|
| **Kind** | Geological · Topographic · Tourist · Cave map · Other |
| **Default opacity** | 0–1, the opacity the layer starts at |
| **Attribution** | Who the sheet belongs to |

A raster map can be **linked to a cave**, which is how a scanned survey sheet ends up beside
the cave it draws.

## Filtering the list

On the **Raster maps** tab of the Geodata screen, **Conditions…** opens the condition editor on
the georeferenced-map world — *Map kind*, name, owner, visibility, dates and *Position* — with
**Apply a saved filter…** and **Save as filter…** beside it, exactly as for the geodata files and
the gallery. The conditions are kept in the page's address, so a link carries them. A map linked to
a cave whose position you may not see exactly is left out of the list and of its count, whatever
the conditions say.

A scanned cave plan or profile is drawn in the survey's own frame rather than on a map grid — a
profile cannot be laid on the ground at all — so it is usually better shown **beside the 3D survey** than on the map: there it needs
no georeferencing, only a few stations pointed out on it, and a tracked party is drawn on it too.
See [Scanned maps beside the model](surveys-and-models.md#scanned-maps-beside-the-model).

## Using them on the map

Ready rasters appear in the layer tree under **Georeferenced maps** / **Raster maps**. Each
has its own opacity control.

The **swipe** tool — *Compare raster with the map beneath* — puts a divider on the map so you
can drag the raster back and forth over the base map. That is the tool for "does this 1950s
geological sheet actually line up".

## In the 3D view

Rasters can be draped on the globe, with one caveat the scene states itself: **on the globe
each sheet is flattened to a single picture**, so it softens under close zoom. The flat map
reads the file itself.

If you are reading fine detail off a sheet, use the 2D map.

---

Related: [Map workspace](map-workspace.md) · [3D view](3d-view.md)
