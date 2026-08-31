# Geodata files

[← Feature reference](README.md) · Workflow:
[Import a season of GPS waypoints](../workflows/import-a-gps-file.md)

---

**Cadastre → Geodata** holds the vector files you have uploaded, the raster maps, and the
register of what every import created.

Three tabs: **Vector files**, **Raster maps**, **Imports**.

---

## Vector files

### Uploading

Drag files in, or click to choose. Accepted:

**GPX · KML · KMZ · GeoJSON · zipped shapefile · CSV with coordinate columns · WKT**

The import runs in the background. Status: *Queued → Importing… → Imported* (or *Failed*).

Each geofile becomes a **layer** you can switch on in the map's layer tree, with its own
bounding box and style overrides. That is the file *drawn on the map* — it is not yet in the
registry.

### Into the registry

Drawing is half the job. **Review and import into the registry** turns the waypoints into
caves, entrances and surface features, proposing what each one is from your club's own naming
habits. That is a whole workflow of its own:
[Import a season of GPS waypoints](../workflows/import-a-gps-file.md).

### Managing

Search by name, edit a geofile's details, delete it — which deletes its imported features
with it, and the confirmation says so.

---

## Raster maps

See [Georeferenced raster maps](georeferenced-maps.md).

---

## Imports

The register of every confirmed import. One row per import, showing:

| Column | |
|---|---|
| **File** | The geodata file, the photographs, or a phone upload it came from |
| **How** | *Reviewed* or *Without review* |
| **Confirmed** | When |
| **What this import created** | Expandable detail |

And the thing that makes importing safe to experiment with: **Undo**. It deletes all the
objects that import created — *"Delete all n objects this import created?"* — including the
ones it hung on records that already existed. An undone import is marked as such rather than
vanishing.

If the source file has since been deleted, the row says so; if the import created nothing, it
says that too.

---

## Exporting

The other direction lives on the list pages and the map, not here. Caves, features and
geodata files export as:

**GeoJSON · GPX · KML · CSV · zipped shapefile**

An export carries only what you may read, with the same location protection as the screen.

---

Workflow: [Import a season of GPS waypoints](../workflows/import-a-gps-file.md) ·
Related: [Detection rules](../admin/vocabularies.md#configuration--detection-rules)
