# Map workspace

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/map-workspace.md)

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

A background or an overlay can also be a **map service** (WMS or WMTS) — the way a national
mapping agency usually publishes its sheets — where your installation lists one. Map services are
drawn in this map and in the [3D view](3d-view.md) only (see [Map services](map-services.md)): never on the overview of
[work areas](work-areas.md), and never in a document such as a trip write-up's map.

A background can also be a **vector background** — shapes such as roads, rivers and place names
that your browser draws in a style, rather than finished pictures. It stays sharp at every zoom and
on every screen, its labels stay upright when the map turns, and they are written **in the
language you read the interface in** where the map has a name in it (otherwise in the local name).
Typical ones are the *OpenFreeMap* backgrounds (Liberty, a muted Positron, and Dark) and your
installation's **own basemap**, served from the installation itself — see
[Own basemap](../admin/messaging.md#own-basemap). Vector backgrounds carry **no contours or
relief**: for reading karst, use a topographic background, or put a hillshade overlay over a
vector one.

A background or an overlay can also be a **GeoTIFF file** — an orthophoto, a scanned geological map,
a LiDAR hillshade — that your installation lists; it is placed in its own coordinate system, a
Romanian *Stereo 70* scan included.

Neither a vector background nor a GeoTIFF file is ever copied into a document: a trip write-up's
map is drawn over plain tile backgrounds only. In the [3D view](3d-view.md), a vector background is
shown as the picture background its installation names in its place.

### Map layers (your data)

| Layer | Shows |
|---|---|
| **Cave entrances** | Clustered at low zoom; individual points as you zoom in |
| **Caves** | The cave records themselves |
| **Surface features** | Everything else in the cadastre |
| **Cave centerlines** | Survey line work projected onto the surface |
| **Imported files** | One layer per geodata file you uploaded |
| **Georeferenced maps** | Raster sheets — see [Georeferenced raster maps](georeferenced-maps.md) |
| **Geotagged photos** | Photographs that carry a position. When a view holds more than one answer carries, the layer's row says *"Not every photo is shown here — zoom in to see the rest."* |
| **Entrance heatmap** | Density rather than individual markers |
| **Cave density (grid)** | How many caves lie in each cell of a grid, counted by the server |
| **Karstification index (areas)** | Each karst area, massif and hill in view, filled by how karstified it reads |

### Karst layers

The two karst layers are computed by the server over the caves you may read; the map never
works them out from the markers it happens to have, which are capped per view. Each has a row
under the layer list while it is on, with a key to its colours.

- **Cave density (grid)** shows, cell by cell, **caves per km²**, a **smoothed** density, or
  **hot and cold spots** (cells clearly denser or sparser than the rest of the view). The row
  names the cell size used: never finer than the location grid that protects cave positions,
  and coarser when the view is wide. Colours are relative to the view — the darkest cell is the
  densest *here* — so compare two views by the key's numbers, not by colour. To compare by colour,
  switch the row's **Bands** from **Relative to view** to **Fixed**: the bands are then the same
  everywhere (by default 0–1, 1–5, 5–20 and more than 20 caves per km², set by your
  administrator). Only the colours change, not the counts. A saved view keeps the choice. If the
  view is too wide even for the coarsest cell, the row says so: zoom in.
- **Karstification index (areas)** fills each karst area, massif and hill by its index class, from
  *very low* to *very high*; an area that cannot be measured is grey with a dashed edge. A karst
  area inside a massif is drawn over it. Point at an area for its name and class; click it to
  open the area, whose page has the full statistics. Only areas you may see exactly are drawn;
  if a view holds more than the installation draws at once, the key says some are not shown.

Both take an opacity and are kept in a [saved view](saved-views-and-windows.md). Neither is
drawn in the 3D view or on a shared view's link.

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
centerlines are being withheld you are told: *"n more centerline(s) not shown at this zoom — zoom
in to load them."* With the layer panel closed, the same count appears at the map's lower left —
*"n survey line(s) hidden at this zoom — zoom in or open the layer panel"* — and pressing it opens
the panel at these controls.

Both are kept in this browser only. The same two numbers are under
[Settings › Advanced](account-and-settings.md#survey-lines-on-the-map), which also says how
high your installation lets the line budget go. Both places take the same range, so a number set
in one is never out of range in the other.

---

## Selecting things

**Click** a feature to select it. **Ctrl or Shift + click** adds to the selection.

The **details panel** on the right has two tabs:

- **Selection** — what you have selected. With several selected it shows how many, which of
  them are readable here, and whether the selection is mixed.
- **In view** — everything currently loaded in the visible extent.

Panel actions: **Zoom to selected**, **Zoom to all**, **Clear the selection**. **Tag all** is
shown but not available yet.

A selection can also be filled from a list: **Select all found (n) on the map** on the cave
registry or the feature list puts up to 200 of the list's results into it. See
[Between a list and the map](search-and-filters.md#between-a-list-and-the-map).

In the **In view** tab, the name in each row is a button: **Tab** reaches it and **Enter**
selects it, as a click on the row does.

The panel itself is configurable. Its sections — Details, Tags, Files, Links, History,
Permissions, Text — can be **reordered** (drag, or Alt + arrow keys), **hidden**
individually, and restored. You can save named **arrangements** and switch between them, set
the **spacing** (density), and choose whether the panel **pushes the map aside** or **floats
over it**.

There are **back and forward** buttons for moving through your selection history.

## Clusters

Below a zoom your installation chooses, entrances cluster. Clicking a cluster lists the entrances
in it — *"n entrances in this area"* — with **Zoom here**; an entrance in the list whose position
you may not see exactly is marked *approx.*, and choosing one selects it and moves the map to it.
While the map is clustered, the **In view** tab says *"Zoom in to list individual entrances."*
rather than giving a misleading list.

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
| **List the caves in view** | Opens the cave registry on the map's filter, cut to what the map shows — see [Between a list and the map](search-and-filters.md#between-a-list-and-the-map) |

## Search

The map's search box covers **features, trips and places**: your own records, plus place
names from an external geocoder (Nominatim). Results are grouped — Caves, Features, Trip
logs, Camps, Places, and matches inside documents. Each offers **Zoom to** or **Open**.

Beside it, a selector (*Go to…*) finds a cave, an entrance, a feature, a document, a trip, a
saved view, a photograph or a saved filter by name and takes you to it; a saved filter picked
there becomes the map's filter. Below them sit the quick filters and **List the caves in view**.

See [Search and filters](search-and-filters.md) and
[Filters on the map](search-and-filters.md#filters-on-the-map).

## Panes and arrangements

The workspace is a row of panes around the map: the **layer panel** on the left, the
**details panel** on the right, and — when you open them — the **3D scene** and a cave's
**survey model** beside the map. Drag the thin separators to resize any of them, or put the
keyboard on a separator and use the arrow keys.

**Moving a pane.** The 3D scene and the survey model each have a strip at the top with a
**Move** button: put the pane **left of the map**, **right of the map**, **under the map**, or
**in its own window** (handy for a second monitor; the pane closes here as the window opens). A
move never restarts the pane — the scene keeps its camera and the model keeps its view.

**The table under the map.** **Table under the map** (in the workspace menu) opens a strip under
the map with two tabs: **In view** — the entrances and features the map is showing, updated as you
pan — and **Picked** — the objects you gathered with Ctrl- or Shift-click. Clicking a row selects it
and frames it on the map. While the table is open, the *In view* list lives there instead of in the
details panel. Drag the separator above the strip to make it taller or shorter.

**Swap the side panels** puts the layer panel on the right and the details panel on the left (and
back). The **[** and **]** keys and the edge buttons follow the sides: **[** always folds whatever is
on the left.

**Filling the workspace.** Press **M** to make the pane you are working in fill the whole workspace,
and **M** again to give every pane its own size back. With the keyboard nowhere in particular, **M**
fills it with the map. The 3D scene, the survey model, the table and the map also have a button
for it, and a filled layer or details panel shows a **Restore** button in its corner.

**Arrangements.** The workspace menu (the button of four squares beside the map's other buttons)
offers ready-made arrangements, one press each:

| Arrangement | What you get |
|---|---|
| **Map** | The map between its two panels, nothing else — the workspace as it ships |
| **Map + 3D** | The 3D scene beside the map, on the right |
| **Survey review** | A survey-model pane beside the map; pick a cave and open one of its models there. The layer panel folds away to give it room |
| **Trip planning** | Under the map, the table of what is in view and the 3D scene showing the ground the approach crosses |
| **Two screens** | The map and its panels here, the 3D scene in a window of its own |

The arrangement in force is ticked. **Save the current arrangement…** keeps the panels, the panes
around the map and whether the 3D scene is open under a name; saved arrangements are listed in the
same menu (and in the details panel's *Saved arrangements* menu, as before) and follow your
account. Arrangements saved before this existed still bring back the panels' sections as they did.

Your arrangement follows your account to any browser — except whether the 3D scene is open: it
is a heavy renderer, so it opens when you ask for it (its button, or an arrangement that includes
it), never by itself when the page loads. An administrator can choose which arrangement a **new**
account starts with (*Administration → Messaging*, tab *Workspace*); it is applied once, the first time that account opens
the map.

## From the keyboard

The first **Tab** on the map page offers **Skip to map**, which puts the keyboard on the map
itself, past the layer panel; the panel stays one **Shift+Tab** behind it. Every other page
offers *Skip to main content* in the same place.

On the map page **?** lists every shortcut. **[** and **]** fold and unfold the panels on the left
and on the right, **Alt+←** and **Alt+→** step back and forward through what you selected,
**Escape** clears the selection, and **M** fills the workspace with the pane you are in (and gives
it back) — see [Panes and arrangements](#panes-and-arrangements).

---

## On a phone

The map workspace adapts to touch, including full geometry editing by finger. The panel
collapses; **Show panel** / **Hide panel** toggles it. The application also installs to a
home screen.

A phone shows one pane, the map, with the two panels as drawers: the arrangements, the table under
the map and the 3D scene beside it are desktop arrangements. The *In view* list stays in the details
drawer.

---

Related: [Saved views, panels and multi-window](saved-views-and-windows.md) ·
[Surface features](surface-features.md) · [Georeferenced raster maps](georeferenced-maps.md)
