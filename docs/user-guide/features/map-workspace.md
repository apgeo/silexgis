# Map workspace

[← Feature reference](README.md) · Related: [3D view](3d-view.md) ·
[Saved views and windows](saved-views-and-windows.md)

---

The map is where most sessions start. It is a working surface, not a picture: everything on
it is selectable, most of it is editable, and it is synchronised with the panels beside it.

---

## The layer tree

Layers come in groups.

### Base layers

The background map. What is offered depends on what your installation configured — typically
OpenStreetMap, a satellite layer, and a hillshade or DEM. **Change the base map** switches
between them, and each has its own **opacity** slider.

### Overlay maps

Additional tile layers configured by the installation, drawn over the base.

### Map layers (your data)

| Layer | Shows |
|---|---|
| **Cave entrances** | Clustered at low zoom; individual points as you zoom in |
| **Caves** | The cave records themselves |
| **Surface features** | Everything else in the cadastre |
| **Cave centerlines** | Survey line work projected onto the surface |
| **Imported files** | One layer per geodata file you uploaded |
| **Georeferenced maps** | Raster sheets — see [Georeferenced raster maps](georeferenced-maps.md) |
| **Geotagged photos** | Photographs that carry a position |
| **Entrance heatmap** | Density rather than individual markers |

### Label decluttering

**Thin out crowded labels** makes names that would overlap give way to each other. Markers
are always drawn — only their *labels* compete for space. Useful in a dense karst area,
misleading if you forget you turned it on.

### Centerline detail

Centerlines are the expensive layer, so they have their own controls:

- **Detail from zoom** — at what zoom full survey detail (including splays) appears.
  Below it you get passage outlines, and wall shots appear as you zoom in.
- **Line budget** — a cap on how many paths are drawn.

Leave both empty to follow the installation's defaults. Higher detail costs more to draw. If
centerlines are being withheld you are told: *"n more centerlines not shown at this zoom"*.

---

## Selecting things

**Click** a feature to select it. **Ctrl or Shift + click** adds to the selection.

The **details panel** on the right has two tabs:

- **Selection** — what you have selected. With several selected it shows how many, which of
  them are readable here, and whether the selection is mixed.
- **In view** — everything currently loaded in the visible extent.

Panel actions: **Zoom to selected**, **Zoom to all**, **Clear the selection**, **Tag all**.

The panel itself is configurable. Its sections — Details, Tags, Files, Links, History,
Permissions, Text — can be **reordered** (drag, or Alt + arrow keys), **hidden**
individually, and restored. You can save named **arrangements** and switch between them, set
the **spacing** (density), and choose whether the panel **pushes the map aside** or **floats
over it**.

There are **back and forward** buttons for moving through your selection history.

## Clusters

At low zoom, entrances cluster. Clicking a cluster tells you how many entrances are in that
area and offers **Zoom here**. You are told *"Zoom in to list individual entrances"* rather
than being given a misleading list.

## Approximate positions

A record whose exact position you may not see is drawn approximately, and labelled:
**"Approximate location — exact coordinates are protected"**, or, on a map, the short form
*approx.* A feature whose geometry is withheld entirely says **"Location withheld"**.

This is [location protection](../admin/location-protection.md). The application does not hide
that the record exists; it hides where it is.

---

## Drawing and editing

The edit toolbar:

| Tool | Does |
|---|---|
| **Draw** | Place a new geometry of the chosen feature type |
| **Modify** | Drag vertices. Alt+click deletes one |
| **Move geometry** | Translate the whole shape |
| **Snap to visible features** | Vertices snap to what is drawn |
| **Undo / Redo** | Full history of the editing session |
| **Discard edits** | Throw the session away |

While drawing: click to place points, double-click to finish a line or an area. The toolbar
shows **Finish** and **Point** (remove the last point). On touch, tap a vertex first and then
**Delete vertex**.

**Pick a feature type** before drawing. Types are grouped as **Points, Lines, Areas** and
*Other*, and you can **pin** the types you use often to the toolbar.

There are dedicated tools for **New cave here** and **New entrance here** — click the map and
the form opens with the coordinates filled in. The entrance form asks which cave it belongs
to, its type, and whether to **protect the exact location**.

Unsaved work is counted in the toolbar: *"n unsaved edits"*.

### Measuring

**Measure distance** and **Measure area**. Measurements are geodesic — they account for the
curvature of the Earth rather than measuring on the flat projection.

---

## The context menu

Right-click anywhere on the map:

- **Add feature**
- **New cave here**
- **New entrance here**
- **Copy coordinates**

---

## Other map controls

| Control | Does |
|---|---|
| **Show my location** | Uses the browser's geolocation |
| **Compare raster with the map beneath (swipe)** | A swipe divider for comparing a raster overlay against the base map |
| **Hide map toolbars** | Gets the chrome out of the way for a screenshot or a small screen |
| **Show the 3D scene beside the map** | Splits the workspace — see [3D view](3d-view.md) |
| **Closest approach** | Measures how close two caves come — see [Measurements](measurements-and-statistics.md#closest-approach) |

## Search

The map's search box covers **features, trips and places**: your own records, plus place
names from an external geocoder (Nominatim). Results are grouped — Caves, Features, Trip
logs, Camps, Places, and matches inside documents. Each offers **Zoom to** or **Open**.

See [Search and filters](search-and-filters.md).

---

## On a phone

The map workspace adapts to touch, including full geometry editing by finger. The panel
collapses; **Show panel** / **Hide panel** toggles it. The application also installs to a
home screen.

---

Related: [Saved views, panels and multi-window](saved-views-and-windows.md) ·
[Surface features](surface-features.md) · [Georeferenced raster maps](georeferenced-maps.md)
