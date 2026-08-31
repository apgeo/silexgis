# Surface features

[← Feature reference](README.md) · Related: [Caves and entrances](caves-and-entrances.md) ·
[Map workspace](map-workspace.md)

---

Everything on the map that is not a cave, an entrance or a centerline: sinkholes, dolines,
springs, resurgences, ponors, faults, walls, karst areas, anything your club draws.

But "surface feature" is a bit of a misnomer, because the underlying model is the same one
caves use. See [Core concepts](../core-concepts.md#1-everything-on-the-map-is-a-feature).

---

## Kind, category and type

Three axes, and they do different work.

**Kind** — which of the four things this is: *Feature*, *Cave*, *Cave entrance*,
*Centerline*. You cannot change a record's kind.

**Category** — *Surface*, *Underground*, *Area*, *Structure*. Broad grouping for filtering.

**Type** — the actual thing: doline, spring, fault, wall, … Types come from a taxonomy your
installation owns and can extend. A type carries a **symbol** used to draw it on the map, and
decides which **typed properties** the feature is asked for.

## Geometry

A feature can be a **Point**, **Line**, **Polygon**, or the multi- variants (Multi-point,
Multi-line, Multi-polygon). Drawing is covered in
[Map workspace](map-workspace.md#drawing-and-editing).

Areas drawn as outlines get **measured shape** figures — see
[Measurements](measurements-and-statistics.md#measured-shape-morphometry).

## Typed properties

Beyond name, description and geometry, a feature carries whatever properties its type
defines. A spring is asked different things from a fault. These are shown as a **Properties**
section and are filterable.

---

## Hierarchy: parents and children

A feature can have **parents** — the things that contain it — and **contained features**.

- A karst area contains caves; a cave contains its entrances.
- One of several parents can be marked **primary**, which is the one breadcrumbs follow.
- **Containment inherits location protection downwards.** Protect the area and everything
  inside it is protected with it.
- **Deleting a feature deletes what it contains.** The confirmation says so.

Edit parents from the feature's page: *Edit parents → Add parent…*, searching features by
name.

## Related features and links

Beside containment, features can be **linked** — an outgoing or incoming named relation with
an optional note. And beyond features, the general
[link mechanism](links.md) connects a feature to documents, trips, cavers, clubs, saved
views, 3D models, cabinets and camps.

A link end you may not read is shown as *Not accessible* rather than named.

---

## The feature list

**Cadastre → Features.** Search by name; filter by kind, category and type. Columns: name,
type, geometry, visibility, description, updated.

Each row offers **Show on map**, **Open**, **Edit**, **Delete**. A cave, entrance or
centerline row also offers **Open full page** — its own typed page rather than the generic
feature page.

Export as GeoJSON, GPX, KML, CSV or zipped shapefile.

## Visibility and protection

Same four visibilities as everything else, and the same **Protected location** flag. A
feature whose geometry is withheld reads **"Location withheld"** on the map and
**"Location withheld — exact geometry is protected"** in the panel.

---

Related: [Map workspace](map-workspace.md) ·
[Links between records](links.md) ·
[Vocabularies](../admin/vocabularies.md)
