# Search and filters

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/search-and-filters.md)

[← Feature reference](README.md)

---

## The search box

In the top bar, and on the map. One box, several kinds of answer, grouped:

| Group | |
|---|---|
| **Caves** | |
| **Features** | |
| **Trip logs** | |
| **Camps** | |
| **Places** | Place names from an external geocoder (Nominatim) |
| **In document text** | What is written *inside* your documents |

Each result offers **Zoom to** or **Open** as appropriate.

### Searching inside documents

The distinctive part. A document hit **quotes the sentence that matched** and opens the
document's page **at the page the phrase was found on** (or sheet, or slide).

- **Accent-insensitive both ways**: `pestera` finds `peșteră`, and the result is quoted back
  spelled the way its author wrote it.
- Words are **stemmed in the language the document is written in** — Romanian and English out
  of the box.
- *"Showing n of m matching documents"* when there are more.
- If nothing matched: *"No document text matched. Scanned pages hold no text to search until
  they are typed up."* — which is usually the real answer.
- A **replaced version** is marked *"version n, replaced"*, and is findable only by people who
  could replace it.

**You only ever find what you are allowed to read.**

---

## Filters

A filter builder for building saved, shareable queries across several kinds of record at
once.

### Worlds

A filter covers one or more **kinds of object**: **Features · Trip logs · Map views ·
Documents**. Each appears once, and there is a cap on how many one filter covers.

### Fields you can condition on

**Name · Title · Kind · Category · Type · Tag · Owner · Caving group · Organising group ·
Visibility · Protected location · Created · Last changed · Trip type · Trip date · Had an
incident · State · Language**

### Sorting

**Added · Changed · Name · Owner · Date · Nearest**

If nothing being searched can be sorted a given way, it tells you rather than sorting
arbitrarily.

### Limits, stated

A filter is bounded, and each bound has its own message: too many parts (*"Try splitting
it"*), nested too deep, too many values in one condition, text too long in a condition, at
least one kind of object required.

### The selector

Where you pick values, the selector says what it is doing:

- *"Type at least n characters"*,
- *"Nothing matches"* / *"Nothing chosen"* / *"n chosen"*,
- and, importantly, **"n not shown — gone, or not yours to see"**.

Some values are **placeable** — *"You can be shown where this is"* — and offer a *Go to…*
that moves the map to them.

---

## Filters on the map

The map reads the same filters. A saved filter, or a filter built in the cave registry, narrows
the map's cave entrances, surface features, survey lines and — in the 3D view — the cave walls.
A filter about caves keeps those caves' entrances; a filter about entrances keeps those entrances.
It only ever takes away from what you can already see.

- **In the address.** The map's address carries its filter as the cave registry's does, so a
  filtered map is a link you can send. A link whose filter cannot be read — or which the person
  opening it cannot run, such as your private saved filter — opens the map unfiltered and says
  *"The filter in this link could not be read…"*.
- **A notice names it**: *"Map filtered by …"*, with a button to clear it. A filter that stops
  working while you look (deleted, no longer shared) empties the layers it narrowed and says so.
- **The layer panel's tag chooser** is a one-condition filter: it keeps entrances carrying the
  tag and the entrances of caves carrying it.
- **Saved views** remember the filter and bring it back when applied. A **shared link** to a view
  narrowed by anything more than a tag shows its area and background but no caves, because a
  link has nobody to run the filter as; the share dialog says so.
- **A place stays out of saved views.** A filter that holds a place of its own — a typed point, a
  pasted outline, or the place *Nearest* is measured from — works in the map's address but is
  never saved in a view, because a view is shown to everyone it is shared with. Saving one says
  so; measure from an object or a named area instead, or apply a saved filter.

### Layers from saved filters

**Add a layer from a saved filter…** in the layer panel draws a second (third…) question over
the map: the entrances and features that saved filter keeps, in a colour and a symbol of their
own, both changed from the layer's row. Up to six at once. They are switched, reordered and made
transparent like every other layer and are kept in saved views. A layer whose filter is gone or
cannot be run any more stays in the list marked **Unreadable** and draws nothing. Markers on
these layers are for looking at; selection stays with the main layers. Shared links do not show
them.

### Quick filters

Pin up to six saved filters — yours or ones shared with you — from **Saved filters** (*"Pin as a
quick filter"*). They appear as one-click chips on the map and in the cave registry's toolbar,
on every browser you sign in to. A chip applies its filter; pressing it again lifts it. A chip
whose filter is no longer there, or cannot be run, is greyed out and says so until you unpin
it, from the chip or from **Saved filters**.

---

## Scopes

Where a scope selector appears, it narrows to: **Caves · Entrances · All features ·
Documents · Trips · Views**.

---

## Filtering on list pages

Every list page has its own quick filters as well — search by name, filter by type, kind or
category, filter by tag. The cave list, feature list, trip list, event list, camp list and
document lists all work this way, with server-side paging and sorting.

---

Related: [Documents and cabinets](documents-and-cabinets.md#searching-inside-documents) ·
[Map workspace](map-workspace.md#search)
