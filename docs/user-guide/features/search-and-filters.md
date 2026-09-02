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
