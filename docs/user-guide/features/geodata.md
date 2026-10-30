# Geodata files

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/geodata.md)

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

### Filtering the list

Beside the search box, **Conditions…** opens the same condition editor as the gallery, on the
geodata-file world: the *File format*, the name, the owner, the visibility, the dates and the
*Position* of a file's footprint. **Apply** narrows the list and puts the conditions in the page's
address, so the link carries them. **Apply a saved filter…** picks one by name (only filters about
geodata files can be chosen), **Save as filter…** keeps the conditions on screen under a name you
choose and for the audience you choose, and the tag beside the buttons says which saved filter the
conditions came from and whether you have changed them since. The list is always asked under your
own access: a filter somebody shared shows exactly the files you could have found by writing the
same conditions, and a file you may not read is never counted. Switching to another tab leaves
the conditions behind.

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

The confirmation says that **each cave and feature the import created can be restored**: an
undo deletes them the way you would by hand, one deletion each, so they are on
[Deleted caves and features](caves-and-entrances.md#deleted-caves-and-features) afterwards.
For an import that created **trips**, it adds that each of those can be restored too, and for
how long — they are on [Deleted trips](trips.md#deleting-and-restoring-a-trip). The detail of an
undone import marks each such object *Deleted* and links to where it is put back.

If the source file has since been deleted, the row says so; if the import created nothing, it
says that too.

### Undoing an upload from a phone

Uploads from a **phone** are listed here too, but they are different from a file import: most of
their lines **change caves and features that already existed** rather than creating new ones. So
an upload is undone by putting things back, not only by deleting.

Open an upload to see its lines. Each line names the cave or feature it concerns; open a line to
see **what the upload changed in it** — every field with its value before and after. The screen
does not guess which fields were changed again since: the server decides that, by the rule the undo
applies, and the result of an undo says which fields it left.

- **Undo this upload** puts every value back as it was before the upload, deletes what the upload
  created, and brings back what it deleted. A field somebody has changed again since the upload is **left as it is**,
  and the result below says which fields were left and why.
- **Undo this row** does the same for one line only, and leaves the rest of the upload in force.
  The upload counts as undone once every line has been.
- **Undo anyway** puts back the fields that were changed since as well. The later changes are
  overwritten, but they stay in the cave's [history](history-and-audit.md), so they can be put back
  from there. A position you may not see exactly is never put back, whatever you press.

Only the account that confirmed the upload, or an administrator, can undo it. Once a line has
been undone — even with a field left — it cannot be undone again from here; the field that was
left can be put back from the cave's history.

See also [Mobile and offline sync](mobile-sync.md).

---

## Exporting

The other direction lives on the list pages and the map, not here. Caves, features and
geodata files export as:

**GeoJSON · GPX · KML · CSV · zipped shapefile**

An export from the cave registry or the feature list holds **everything the list's filter finds**,
not only the page on screen — see [Search and filters](search-and-filters.md#filtering-on-list-pages).
Caves can also be exported for interchange as
[KarstLink](caves-and-entrances.md#sharing-caves-in-the-interchange-format).

On the **Geodata** page, **Export the listed files** writes the imported rows of **every file the
list shows** — its name search and its conditions both apply — as one layer, each row carrying the
name of its file in a `geofile` column. A file you may not read is not in it, whoever sends the
link. A selection of more than 200 files, or of more than 200,000 rows, is **refused** with a
message to narrow the filter and export in parts, rather than cut short. The export button beside a
single row still exports that one file.

The trip list keeps its own controls, and its spreadsheet export follows them. The export route
also accepts a trip filter (a link's condition or a saved filter) for programs that call it: a trip
must satisfy both, and a filter that cannot be read is refused instead of exporting every trip. No
trip-list screen sends one yet. Trips you may not read are never in the file.

An export carries only what you may read, with the same location protection as the screen.

---

Workflow: [Import a season of GPS waypoints](../workflows/import-a-gps-file.md) ·
Related: [Detection rules](../admin/vocabularies.md#configuration--detection-rules)
