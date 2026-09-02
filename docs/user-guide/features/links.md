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
| **A region of an image** | |
| **A moment** / **a stretch of a recording** | In seconds |
| **A survey station** / **a run of stations** / **a survey** / **a run of surveys** | Of a 3D model |
| **A point in the model** | |
| **A waypoint** / **a run of waypoints** | Of a geodata file |

Some of these need a viewer that can select them, and the dialog says so plainly: *"Pointing
at this kind of part arrives with the viewer that can select it."*

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

> **A link is never the thing that discloses a protected cave.** It shows each reader only the
> ends they are allowed to see.

Every link has **an address of its own** — a short code you can paste into a message or a
report, which opens the link's own page.

## Making one

**Link…** on any record. Choose the kind of item, find it, pick the relation, optionally add
a **note** ("Why these belong together") and a **description**, and say which item the
relation reads from if it is directed.

On the link's page you can add members, remove them, reorder them, change which is the main
member, and edit the relation and description.

**Deleting a link deletes nothing it connects**, and the confirmation says so.

---

Related: [Documents and cabinets](documents-and-cabinets.md#link-annotated-text) ·
[Surface features](surface-features.md) · [Vocabularies](../admin/vocabularies.md)
