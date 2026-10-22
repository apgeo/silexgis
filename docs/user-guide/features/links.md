# Links between records

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/links.md)

[← Feature reference](README.md) · Related:
[Core concepts](../core-concepts.md#4-links-say-how-two-things-are-related)

---

A cave and the report that describes it. A trip and the photographs it produced. Two records
that turned out to be the same hole. A link names **two or more things of any kind** and says
**how they are related**.

This is the general mechanism. [Containment](surface-features.md#hierarchy-parents-and-children)
is the other one, and it is specifically about features being inside features.

---

## What can be linked

| Kind | |
|---|---|
| Feature | Including caves, entrances and centerlines |
| Document | |
| Trip log | |
| Caver | |
| Caving group | |
| Saved view | |
| 3D survey model | |
| Geodata file | |
| Cabinet | |
| Camp (expedition) | |

A link can also **mark a new point on the map** — naming a spot that had no record yet, and
creating the point as the link is written. You choose its name, altitude and audience there
and then.

## The relation

The wording comes from a list your installation owns and can extend:

**Same object as · Related to · Contains / Contained in · Documented by / Documents ·
Source of / Derived from · Adjacent to · Original of / Duplicate of · Needs clarification**

plus the trip-specific relations that drive a trip's *What this trip did* section
(*Worked in, Aimed at, Visited, Surveyed, Discovered, Dug at, Photographed, Searched not
found, Left lead, Follows on from*), and *Text of / Has text*.

**A relation that reads one way reads correctly from both ends.** A single link says
"Contains" on one page and "Contained in" on the other. When a relation is directed you must
mark **which item it reads from** — the *main member* — and the dialog previews both
readings before you save.

An administrator can add relations of their own. Ones that ship with the product keep their
code and the way they read, and cannot be deleted. A relation that links already record
cannot be redefined or removed. See [Vocabularies](../admin/vocabularies.md#configuration--link-relations).

## Pointing at a part rather than the whole

A link end can be an **anchor** into part of a thing:

| Anchor | |
|---|---|
| **The whole thing** | Default |
| **A page** / **a range of pages** | Of a document |
| **A passage of text** | Selected in the document |
| **A region of an image** | Drawn on the picture itself — a rectangle, a circle, an outline or a point. Every region already drawn on a picture is painted when you read one, the one you followed heavier than the rest. Both drawing and picking also work [from the keyboard](#regions-of-a-picture-from-the-keyboard) |
| **A moment** / **a stretch of a recording** | In seconds |
| **A survey station** / **a run of stations** / **a survey** / **a run of surveys** | Of a 3D model |
| **A point in the model** | |
| **A waypoint** / **a run of waypoints** | Of a geodata file |
| **A moment of the trip** | Of a trip [followed from the surface](live-tracking.md#the-party-on-the-survey) — how a photograph is hung on a moment of its replay. A link saying a photograph *documents* a trip's moment needs the right to **edit** that trip, because it decides what the trip's replay shows |

Some of these need a viewer that can select them, and the dialog says so plainly: *"Pointing
at this kind of part arrives with the viewer that can select it."*

### Regions of a picture from the keyboard

A region can be drawn and picked without a mouse.

**Drawing.** Press **Tab** until the picture has focus; a cursor appears in its middle.

| Key | What it does |
|---|---|
| **Arrow keys** | Move the cursor a small step across the picture |
| **Shift** + arrow | A larger step |
| **Enter** or **Space** | Press here: places a **point**; the first and second press give a **rectangle**'s two opposite corners, or a **circle**'s centre and then a spot on its edge; for an **outline**, each press adds a corner |
| **Enter** again on the last corner | Closes the outline |
| **Backspace** | Takes back the last corner of an outline |
| **Escape** | Abandons the shape in progress. With nothing in progress, it closes the dialog the picture is in |

The help line under the shape choice says the same in the screen's words. A region drawn
from the keyboard is exactly the region the same drag would have drawn.

**Picking.** When you read a picture, each region already drawn on it is in the **Tab** order,
named by its relation (or *Region n* when it has none). The focused region is drawn heavier;
**Enter** or **Space** opens it as a click would. A region drawn inside another comes after it,
so the inner one can always be reached.

### Text anchors survive editing

For a passage of text: open the document, drag across the passage. **What is stored is the
passage itself**, not a character offset, so the link still finds it after the document
changes.

If it cannot be found exactly any more, the link says which:

- **Measured against an older version, found again in the current one**,
- **Measured against an older version — shown on the whole resource**,
- **This part is no longer in the resource.**

---

## Reading a link

Each record shows **Linked items (n)**. Each link row names its members and its relation.

- A link with only one member visible to you is marked **Incomplete** — *"A link says
  something once it connects two or more."*
- A member you may not read shows as **Restricted item**: *"This link names something you may
  not read."*
- If nothing in a link is visible to you, it says so.
- A member that is a photograph, a cave or a person shows **a small picture** — the photograph,
  the cave's headline picture, the person's portrait. The other kinds show none, since a picture
  of a saved view, a geodata file or a survey model would have to draw positions.
- Following a link to a survey station or a survey **moves the 3D view** there rather than
  loading the survey again, so you keep the place you were looking at.

> **A link is never the thing that discloses a protected cave.** It shows each reader only the
> ends they are allowed to see.

Every link has **an address of its own** — a short code you can paste into a message or a
report, which opens the link's own page.

## Making one

**Link…** on any record. Choose the kind of item, find it — in the search selector described
under [Search and filters](search-and-filters.md#the-selector), which also reaches cabinets,
survey models and geodata files — pick the relation, optionally add
a **note** ("Why these belong together") and a **description**, and say which item the
relation reads from if it is directed.

On the link's page you can add members, remove them, reorder them, change which is the main
member, and edit the relation and description.

**Deleting a link deletes nothing it connects**, and the confirmation says so.

---

Related: [Documents and cabinets](documents-and-cabinets.md#link-annotated-text) ·
[Surface features](surface-features.md) · [Vocabularies](../admin/vocabularies.md)
