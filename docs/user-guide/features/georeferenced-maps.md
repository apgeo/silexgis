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

Drag a sheet in. Accepted:

- **GeoTIFF** — georeferenced, as most GIS software writes it;
- **PDF** — a map published as a document, georeferenced or not;
- **PNG or JPEG** — a scan or a photograph of a paper sheet.

Each is converted to Cloud-Optimized GeoTIFF in the background: *Queued → Processing… → Ready*
(or *Failed*).

A sheet that carries its own place — a GeoTIFF, or a PDF exported with georeferencing — is ready
as it is; a georeferenced PDF is cut to its map frame, so the legend and the margins stay off the
map. A sheet that carries **no place** — a scan, a plain PDF, a GeoTIFF that turns out to hold no
position — is drawn and then **waits to be placed** instead of failing: the list marks it
*Needs placement*, with **Place on map…**.

For a PDF, the page is drawn once at a resolution the installation chooses for the sheet's scale.
**Redraw…** draws a sheet again with another page, other layers of the document, or another
resolution, without uploading it a second time; a placement made by hand is kept.

## Placing a sheet on the map

**Place on map…** (or **Adjust placement…** on a placed sheet) opens the placement editor: the
sheet beside a map with the background the workspace uses. Every change shows the server's own
warp of the sheet over that map, at an opacity you set, with how well the points fit. Three ways:

- **Corners** — drag the sheet's four corners to where they belong. *Perspective* puts each corner
  exactly where you dropped it; *Keep parallel* keeps opposite sides parallel and shows how far each
  corner ended up from your mark. **Type the corners** takes their longitude and latitude instead.
- **Two points** — click a point on the sheet, then the same point on the map, twice. The sheet is
  moved, turned and scaled without being stretched.
- **Control points** — as many pairs as the sheet needs: a point on the sheet and the same place on
  the map, clicked or typed. Coordinates can be typed in WGS 84, in the installation's working
  system or in the national grids it lists. Choose the **Transformation**:

  | | |
  |---|---|
  | **Similarity** | Moves, turns and scales the sheet without stretching it |
  | **Affine** | Also stretches and slants it; opposite sides stay parallel |
  | **Perspective** | For a sheet photographed at an angle |
  | **Polynomial, 2nd order** | Bends the sheet gently — old or unevenly stretched paper |
  | **Polynomial, 3rd order** | Bends it more freely; needs many well-spread points |
  | **Thin-plate spline** | Passes exactly through every point; the residual shown for each is that point left out in turn |

  Each point's **residual** — how far the fit leaves it from where you put it — is listed, and the
  points that fit worst are marked. Drag either end of a pair to move it.

More that the editor does:

- **Use pinned stations.** Where survey stations have been pointed out on the sheet — as for a
  [scanned map beside the model](surveys-and-models.md#scanned-maps-beside-the-model) — they are
  offered as ready-made control points, the survey supplying where each one is.
- **Outline.** **Draw outline** around the part of the sheet that is the map; the rest — legend,
  title block, margins — is left off the map. **Use the map frame** takes a georeferenced PDF's
  own frame.
- **The document's own placement** can be looked at in the editor, and taken over as a starting
  point you then correct. Removing your placement later returns to the document's.
- **Export placement / Import placement** — a placement is exchanged as a **IIIF Georeference
  Annotation**, the open format other IIIF georeferencing tools read and write, with the points and
  the outline. An annotation made elsewhere, even on a picture of another resolution, lands on the
  same spots.

After saving, the editor waits for the placed map to be ready and offers to show it on the map.

**A sheet linked to a cave tells where that cave is.** The editor warns that sharing the sheet's
original document shares the cave's location.

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
