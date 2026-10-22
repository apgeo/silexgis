# Search and filters

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/search-and-filters.md)

[← Feature reference](README.md)

---

## The search box

In the top bar, and on the map. One box, several kinds of answer, grouped:

| Group | |
|---|---|
| **Features** | Caves, entrances and every other feature — picking one opens its page |
| **Trip logs** | Picking one opens the trip |
| **Camps** | Picking one opens the camp |
| **In document text** | What is written *inside* your documents — picking one opens the document at the matching page |
| **Places** | Place names from an external geocoder (Nominatim) — picking one moves the map there |

Under a name, a quieter line says **what the hit is and where it belongs**, so two things of the
same name can be told apart: a cave gives its type and region; any other feature gives its type
and the name and region of the cave it belongs to — that cave named only if you may read it; a
document gives its type and the cabinets it is filed in. The line never carries a coordinate, an
altitude, an address or a location note.

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

A filter is a **question** about your records: which kinds of object it is about, the
conditions they have to meet, and how the answer is sorted. You build one on a list screen —
the [cave registry](#the-cave-registrys-filter), the
[feature list](#filtering-on-list-pages) and the
[gallery](photographs.md#conditions-and-saved-gallery-views) today — and you can keep it as a
[saved filter](#saved-filters).

**A filter is always answered with what you yourself may see.** It narrows what you are
allowed to read; it never widens it.

### Kinds of object

A filter covers one or more **kinds of object**: **Features · Trip logs · Map views ·
Documents · Georeferenced maps · Cabinets · Survey models · Photographs · Geodata files**.
Each appears once, and there is a cap on how many one filter covers.

### Fields you can condition on

Which fields are offered depends on the kind of object. Among them:

- for every kind: **Created · Last changed**, and either a **Title** (trip logs, documents and
  photographs) or a **Name** (everything else);
- for every kind except cabinets and survey models: **Owner · Caving group · Visibility**;
- for features and caves: **Kind · Category · Type · Tag · Protected location · Region · Rock
  type · Surveyed length (m) · Depth (m) · Cave type · Other names**, **Position** (see
  [Position conditions](#position-conditions)), and the
  [typed properties](#property-conditions) a feature type declares;
- for trips: **Trip type · Trip date · Had an incident · State · Organising group**;
- for documents: **Type · Language**;
- for photographs: **Tag · Caption · Place name · Taken on · Dated (taken, else uploaded)**;
- for survey models: **Survey format · Surveyed on · Current model**;
- for georeferenced maps: **Map kind**; for geodata files: **File format**.

A field that is not yours to use is not offered. If a filter names one anyway — one that was
shared with you, or written by hand — it is refused exactly as if the field did not exist:
the refusal never tells you what is there.

### Sorting

**Added · Changed · Name · Owner · Date · Nearest**

If nothing being searched can be sorted a given way, it tells you rather than sorting
arbitrarily. **Nearest** is offered only where there is a place to measure from — today the
cave registry's advanced filter; see [Nearest first](#nearest-first).

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

The same selector is used wherever you choose one feature, cave or trip from a search: a
feature's parent, *Add parent*, a link's target, feature-set members, a permission rule's
feature, the other cave of a closest approach, the cave of a new entrance, of a library
photograph or of a georeferenced map, a recording import's trip and cave, and the caves a
phone carries. Caves are found by any part of their name, accents ignored. Only the recording
import's trip list shows your newest trips before you type.

From the keyboard: **Down** from the search box reaches the first result, **Up / Down** move
between results, **Enter** or **Space** chooses. Where a choice can be withdrawn, *Clear the
choice* appears once something is chosen.

---

## Filters on the map

The map reads the same filters. A saved filter — picked in the
[selector beside the search box](#the-selector-beside-the-maps-search-box), a quick filter, or a
saved view — or a filter handed over from the cave registry or the feature list with **Show these
on the map** ([Between a list and the map](#between-a-list-and-the-map)) narrows the map's cave
entrances, surface features, survey lines and — in the 3D view — the cave walls.
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
quick filter"*). They appear as one-click chips on the map, in the cave registry's toolbar and on
the feature list, on every browser you sign in to. A chip applies its filter; pressing it again
lifts it. A chip whose filter is no longer there, or cannot be run, is greyed out and says so
until you unpin it, from the chip or from **Saved filters**.

---

## The cave registry's filter

**Caves** opens the cave registry. Above the list sit its quick controls: **Search name or
toponym…**, **All types**, **Region**, **Length (m)** and **Filter by tag…**. They
are one filter, and the address of the page carries it, so a link you copy opens the same
question for whoever follows it (answered with what *they* may see).

**Advanced filter…** opens the [condition editor](#the-condition-editor) on the same filter,
for anything the controls cannot say — an OR, a NOT, a range. When you apply it,
the parts the controls can show go back into the controls; the rest is kept beside them:
*"This filter also has conditions these controls do not show (n). They still narrow the
list."* — with **Edit them** and **Remove them**.

Next to the controls:

- **Save as filter…** keeps the current question as a [saved filter](#saved-filters);
- **Apply a saved filter…** picks one you may use — yours, or one shared with you;
- **Saved filters** opens the list of them.

While a saved filter is applied, the page says *"Saved filter: name"*, and *"changed since"*
once you change the controls. **Stop naming this saved filter** keeps the question and forgets
the name.

**Save as filter…** is unavailable, and says why when you point at it, while the question holds a
typed point, a pasted polygon or a place to measure *Nearest* from — a saved filter never keeps
one. See [Position conditions](#position-conditions).

If a link's filter cannot be read, the page says so and shows every cave rather than guessing;
ask whoever sent it for a fresh link.

---

## Between a list and the map

The cave registry, the feature list and the map hand a question to each other, so you can look
at the same set both ways.

| Control | Where | Does |
|---|---|---|
| **Show these on the map** | Cave registry, feature list | Opens the map on the list's own filter, so it draws the rows listed |
| **Select all found (n) on the map** | Cave registry, feature list | Puts the list's whole result — not just the page on screen — into the map's selection and opens the map on it |
| **List the caves in view** | Map, in the row under the search box beside the quick filters | Opens the cave registry on the map's filter, cut to what the map is showing |

**Select all found** takes at most **200**. When the list found more, it says so with both
numbers — *"Selected the first 200 of 1,234 on the map — that is as many as a selection holds."*
— and the selection keeps saying *"The first 200 of 1,234 found by the list."* The count is only
of rows you may read, in the order the list shows them. While the selection is still exactly that
result, it offers **Save as a filter…**, which saves *the filter that found it*, not the objects:
run later, it finds whatever matches then. It is not offered for a result cut to the map's view,
because a saved filter never keeps a place.

**List the caves in view** marks the registry *"Only caves in the map's view"*, with **Show caves
everywhere** to lift that. What is in view is decided by the server from the map's window, not
from the markers the map happens to hold. The registry's exports and its Distributions panel
take the same window; the **KarstLink (JSON-LD)** export does not, so it is unavailable until
you lift the window — *"Not while the list is cut to the map's window…"*. If the map's filter is a
saved filter that cannot be read, the map says so and opens nothing; a link naming a window that
is not a valid area opens the registry on caves everywhere and says so.

**Rows and the map point at each other.** With the map open beside the list — a registry popped
into a window of its own, or a second browser window — pointing at a row rings that cave's
entrance, or that feature, on the map, and picking something on the map marks its row and
scrolls it into view. The map rings only what it has already drawn, at the position it drew,
and never a cluster, so nothing you were not already shown is revealed.

---

## The condition editor

The same editor serves the cave registry (**Advanced filter**) and the gallery
(**Photograph conditions**). Each row is one condition: a **Field**, a **Comparison** and a
**Value**.

- **Add condition** adds a row; **Add group** adds a group inside the current one.
- Each group says how its rows combine: **All of these (AND)** or **Any of these (OR)**.
- **Not** turns a row or a group into its opposite.
- Groups nest to a fixed depth; past it the editor says *"Groups are nested at most n deep."*
- **Matches: n** is counted live, before you apply, with what you may see.
- **Apply** puts the edited filter on the list.

### Comparisons

Which comparisons a field offers depends on what it holds:

| Comparison | For |
|---|---|
| **is** · **is one of** | a choice from a list: a type, a tag, a person, a group |
| **contains** · **starts with** | text |
| **less than** · **at most** · **more than** · **at least** · **between** | numbers and dates |
| **is empty** · **is not empty** | anything that may be left blank |

**at least** and **at most** include the value itself: *Surveyed length at least 500* finds a
cave of exactly 500 m; *more than 500* does not. **between** includes both ends.

### Typing a comparison

A number or a date can be typed with a sign instead of choosing the comparison from the list:

| Typed | Means |
|---|---|
| `>300` | more than 300 |
| `>=300` · `≥300` | at least 300 |
| `<30` | less than 30 |
| `<=30` · `≤30` | at most 30 |
| `=300` | equal to 300 |
| `!=300` · `≠300` | not equal to 300 — the row's **Not** is ticked, so it also finds what has no value recorded |
| `300..500` · `300-500` | between 300 and 500, both included |
| `300` | the comparison already chosen for the row |

A number may use a comma or a point for its decimals (`2,5`) and may be followed by `m`
(`300 m`); a comma is never read as a thousands separator. A day is written `2024-05-01` or
`1.5.2024`; a range of days is written only with two dots (`2024-05-01..2024-06-30`), because a
day already has dashes in it, and `=2024-05-01` means the whole of that day. Beside the box the
editor says what it read — *"Read as: at least 300 m"* — before anything is applied. Text it
cannot read, or a comparison the field does not offer, is said so on the row and applies
nothing. The registry's **Length (m)** box reads the same signs; there, a bare number means
*at least*.

**Dates are days.** When you pick a day, it is the day on your own calendar: *Trip date at least
5 October* finds the trips of 5 October and after, wherever you are when you ask. The same holds
for a date a feature type's property records, and for the gallery's date range. A field that holds
a moment rather than a day — **Created**, **Taken on** — counts the day from midnight to midnight
where you are. A saved filter keeps the day its author picked and shows it as that day.

### Property conditions

Mapped features have a feature type, and some types declare typed properties of their own — a
number, a text, a date, a yes/no. Where features are filtered, the editor offers each as
**Property: name**, with the comparisons its kind allows.

A property belongs to its type, so it is asked **only together with its feature type in the same
AND group** — the editor says so under the row. A property of a type you may not read is not
offered, and is refused like a field that does not exist. Caves themselves carry no feature
type, so a type or property condition never matches a cave.

### Position conditions

Where caves and features are filtered, the editor offers **Position**, with two comparisons:

| Comparison | Takes |
|---|---|
| **within** | A **Distance in metres** (1 to 200,000), then what it is **Measured from**: **an object** picked with the [selector](#the-selector), **a point** typed as **Longitude** and **Latitude**, or **a saved filter's results** |
| **inside** | An **Outline**: **a named area** picked with the selector, or **a pasted polygon** — a GeoJSON Polygon, or a Feature holding one, of at most 2,000 positions |

**A position condition is answered on the positions you are shown.** A record you may see exactly
is measured where it is; a protected point you may not is measured at the rounded position the
map shows you; a protected line or outline you may not see exactly never matches. So the
condition never tells you more about where something is than the map already does.

- An object you may not see exactly cannot be measured from. It is refused with the same
  sentence as one that does not exist — *"What this condition measures from is not available to
  you."* — so the refusal does not tell you which it is.
- **a saved filter's results** lists only saved filters about the same kind of object that are
  ready to run. One that itself measures from a saved filter cannot be used, and one that finds
  more than 1,000 objects is refused as too many.
- A pasted polygon that is not a single valid polygon is explained under the box before anything
  is sent.

**A typed point, a pasted polygon or a place to measure nearest first from is never saved** —
not in a saved filter, and not in a saved view. *"Use an object or a named area instead."* A
condition measured from an object or inside a named area can be saved.

### Nearest first

In the cave registry's advanced filter, **Order** chooses how the answer is sorted: **Changed**
or **Nearest**. **Nearest** asks for a place to measure from — a point or an object — and the
count and **Apply** wait until it has one: *"Nearest first needs a place to measure from: type a
point or pick an object."*

Only caves you may see exactly are ranked by distance. Every other cave the filter keeps comes
after them, most recently changed first, so a protected cave is never placed by where it falls
in the list. The order stays as you turn pages, and is carried in the page's address — but the
place it measures from is not passed to exports or to *Save as filter…*.

### When a filter is refused

If the server cannot run a filter, it says why, **on the row it is about** — for example
*"This field does not support that comparison."*, *"The range ends before it starts."* or
*"A value is longer than 200 characters."* Problems that belong to no single row — too many
conditions, groups nested too deep — are listed for the filter as a whole. **Apply** stays unavailable
until the filter can be run.

---

## Saved filters

A saved filter is **a question you keep**. The account menu's **Saved filters** lists the ones
you may use: your own, those shared with a caving group you belong to, and those shared with
everyone signed in to this installation.

| Column | Shows |
|---|---|
| **Name** | its name and description |
| **Asks for** | the conditions, in words |
| **Who can use it** | **Only me**, **Members of** a caving group, or **Everyone signed in** |
| **Saved by** | **You**, the author as their profile shows them to you, or **A club member** |
| **State** | **Ready**, **Out of date** (a type or tag it names has since been removed, so that condition matches nothing) or **Cannot be read** |

**Open in the cave registry** applies a cave filter there. A saved gallery view is listed here
too, and is applied from the gallery with **Apply a saved gallery view…**.

### Who a new filter is shared with

Wherever you save a filter, the dialog shows **who can use it** and says in one line what that
means — *"Only you can see this filter."*, *"Members of … can use it"* or *"Everyone signed in
can use it"*. It starts from **your default**, which is **Only me** until you change it under
**Settings → Saved filters** (the dialog's **Change your default** link goes there). You can
choose differently for each filter you save, and change it later from the list. Nobody without
an account ever sees a saved filter.

### Sharing grants nothing

Sharing a filter — with a caving group or with everyone signed in — gives them **the question
only**. Each of them is
answered with what they themselves may see; a filter never lends its author's access to anyone.
For the same reason a saved filter **never keeps a place to measure from** — *nearest to here*
is asked afresh each time.

### Who may change it

Only **its author** or a **full administrator** may rename, describe, share or delete a saved
filter. The list offers **Rename, describe or share** and **Delete** on exactly the filters you
may change. Deleting a shared filter takes it away from everyone it was shared with.

A saved filter that asks something you cannot ask — a field you may not use, or a form this
version cannot read — shows as **Cannot be read** and cannot be applied.

---

## The selector beside the map's search box

Beside the map's search box sits a [selector](#the-selector) for *"which of these things do I
mean"*, with a button for each kind it can look among:

| Button | Typed first | Picking one |
|---|---|---|
| **Caves** | `#` | Selects it and moves the map to it |
| **Entrances** · **All features** | | Selects it and moves the map to it |
| **Documents** | `@` | Opens the document |
| **Trips** | `!` | Opens the trip |
| **Views** | | Applies the saved view |
| **Photographs** | | Opens the photograph |
| **Saved filters** | | Makes it the [map's filter](#filters-on-the-map) |

A character typed first — `#`, `@`, `!` — narrows to that kind, and only at the very start, so a
name that contains one is still found. A feature the map cannot place for you — never placed, or
one whose position you may not see — opens its page instead, which says which. A saved filter
that asks nothing about features, or that you cannot run, is not applied: *"That saved filter
does not ask about features, or cannot be run with your access, so it was not applied to the
map."* A saved filter's row shows its description under its name.

---

## Filtering on list pages

Every list page has its own quick filters as well — search by name, filter by type, kind or
category, filter by tag. The cave list, feature list, trip list, event list, camp list and
document lists all work this way, with server-side paging and sorting.

**The feature list's controls are one filter too** — **Search by name…**, **All kinds**, **All
types**, **All categories** and **Filter by tag…**. Like the cave registry's, they are kept in
the page's address, and every export from the list carries the same question, so the file holds
what the table shows. The list has the same saved-filter controls — **Apply a saved filter…**,
**Save as filter…**, **Saved filters** and the quick filters — but no condition editor: a saved
filter with conditions these controls cannot show keeps them, says so, and offers **Remove
them**. A saved filter that is not about features is refused: *"That saved filter is not about
features."*

**From the keyboard**, the name in each row of the cave registry, the feature list, the map's
**In view** list, the trip lists, events, camps and the calendar's table is a link or a button:
**Tab** reaches it and **Enter** opens it.

---

Related: [Documents and cabinets](documents-and-cabinets.md#searching-inside-documents) ·
[Map workspace](map-workspace.md#search) · [Caves and entrances](caves-and-entrances.md#the-cave-list) ·
[Surface features](surface-features.md#the-feature-list)
