# Core concepts

[← Back to the guide](README.md) · Previous: [First steps](first-steps.md)

---

Six ideas. Everything else in the application is an application of one of them.

---

## 1. Everything on the map is a *feature*

Caves, cave entrances, centerlines and surface features are not four separate systems that
happen to look similar. They are **one model** with a *kind*:

| Kind | What it is |
|---|---|
| **Cave** | The cave itself — the record all the cave data hangs on |
| **Cave entrance** | A way in, with its own precise position |
| **Centerline** | Survey line work, projected onto the map |
| **Feature** | Everything else: sinkhole, spring, fault, wall, karst area, … |

Because they share a model, three things follow that would otherwise need special cases:

- **Anything can contain anything.** A karst area contains caves; a cave contains its
  entrances. Containment drives the breadcrumbs you see at the top of a feature's page.
- **Containment inherits protection downwards.** Protect an area and what is inside it is
  protected with it.
- **Anything can be linked to anything by a named relation** — see idea 4.

Features carry a **category** (surface, underground, area, structure), a **type** (from a
taxonomy your installation owns), a geometry (point, line, polygon, or the multi- variants),
and typed properties belonging to their type.

## 2. Visibility, permissions, and location protection are three different things

People conflate these constantly. They are not the same and they compose.

**Visibility** is a property of the record, set by whoever made it. Four values:

| Value | Means |
|---|---|
| Private | Only you (and anybody explicitly granted) |
| Caving group | Members of the group it is bound to |
| Authenticated users | Anybody with an account on this installation |
| Public | Anybody, including visitors with no account |

**Permissions** are rules — *allow* or *deny*, per action, per kind of thing, at a chosen
scope. They live in **permission groups** you are a member of, or directly on one object.
See [Permissions explained](admin/permissions.md).

**Location protection** is a separate flag on a cave or feature: *the exact position is
withheld*. It is orthogonal to the other two. A cave can be readable by everybody signed in
and still have a protected position. See [Location protection](admin/location-protection.md).

The consequence to internalise: **being able to read a thing does not mean being able to
place it**, and being invited onto a trip grants nothing at all.

## 3. Nothing is counted over more than you may read

Every total, every count, every list length in this application is computed over the records
*you* are allowed to read. Trip statistics, a person's hours underground, the number of caves
a trip visited, a camp's roster, an album's photograph count.

Two consequences:

- **Two people legitimately see different totals for the same subject, and both are right.**
  The screen says so where it matters.
- **A list that is short because you cannot see the rest says so.** You get *"3 not shown to
  you"* rather than a quietly incomplete list. The application would rather tell you that
  something exists than let you believe you have the whole picture.

## 4. Links say how two things are related

Beyond containment there are **links**: a named relation between two or more things of *any*
kind — features, documents, trips, cavers, clubs, cabinets, 3D models, saved views, cabinets.

The relation comes from a list your installation owns and can extend: *Contains*,
*Documented by*, *Duplicate of*, *Same object as*, *Adjacent to*, and the trip-specific ones
(*Surveyed*, *Dug at*, *Left lead*, …). A directed relation reads correctly from both ends —
one link says "Contains" on one page and "Contained in" on the other.

A link can point at a *part* rather than the whole: a page or run of pages of a document, a
quoted passage, a moment or stretch of a recording, a survey station. Each link has its own
short address you can paste into a chat. And a link shows each reader only the ends they may
see — a link is never how a protected cave gets disclosed.

See [Links between records](features/links.md).

## 5. Records that happen have a life, and a draft tells nobody

Trips, events and camps move through states:

`draft → proposed → planned → confirmed → done → published`
with `cancelled` and `delayed` available throughout.

The important part: **a draft notifies nobody**. You can write a trip up over several
sittings and nothing goes out. **Publishing is the act that announces it** — and only to
the people who may actually open it.

A draft is not a *hidden* trip, though. Whoever the trip's visibility already admits can read
it from the moment it exists. Draft is about *telling people*, not about *hiding*.

## 6. Your club owns the vocabulary

The application ships sensible lists and then gets out of the way. Cave types, entrance
types, rock types, feature types, document kinds, trip purposes, trip participant roles, link
relations, report layouts, waypoint detection rules — all of these are lists an
administrator can extend with whatever this archive actually says.

Items that ship with the product cannot be renamed out from under the software or deleted,
so nothing you build on disappears. Everything you add is filterable and countable from the
moment you add it.

See [Vocabularies your club owns](admin/vocabularies.md).

---

## A note on how the application talks to you

It is unusually blunt, on purpose. When something cannot be done, or a figure cannot be
computed, or a file holds no text to read, it says which of those it is instead of showing a
spinner or a blank panel. When a number is partial it says what it is partial over. When an
action cannot be undone it says so before you take it.

Read those lines. They are the documentation nearest to hand.

---

Next: pick a [workflow](workflows/README.md), or browse the
[feature reference](features/README.md).
