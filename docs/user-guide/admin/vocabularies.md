# Vocabularies your club owns

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/vocabularies.md)

[← Administration](README.md)

---

The application ships sensible lists and then gets out of the way. This page covers every
list your installation can extend, where it lives, and what it governs.

**A rule that holds throughout:** items that ship with the product **cannot be renamed out
from under the software or deleted**, so nothing you build on disappears. Everything you add
is filterable and countable from the moment you add it.

---

## Configuration → Document kinds

**What it governs:** a document's kind — survey report, permit, trip report, bulletin,
correspondence — and, crucially, **which details documents of that kind are asked for**.

That is what lets an archive be organised the way your club actually files things rather than
the way we guessed.

Needs **write** on taxonomies, not merely read: authoring a kind's schema decides what every
document of that kind may say.

Related: [Documents and cabinets](../features/documents-and-cabinets.md)

---

## Configuration → Feature kinds

**What it governs:** the kinds of [feature](../features/surface-features.md) the installation
records on the map — sinkhole, spring, pitch, bivouac, karst area and the rest — and for each:

| | |
|---|---|
| **Name**, **Description**, **Sort order** | How the kind is listed and in what order |
| **Code** | Lower-case letters, digits and underscores. *"Installations exchange features by this code, so keep it stable."* |
| **Category** | Surface · Underground · Area · Structure |
| **Shapes it may be drawn as** | Point · Line · Outline · Several points · Several lines · Several outlines |
| **When protected** | *Shown snapped to the grid (points only)* or *Not shown at all* — how a feature of this kind appears to somebody without exact view. See [Location protection](location-protection.md) |
| **Only inside another feature** | For kinds that make no sense on their own, such as a place inside a cave |
| **Map symbol** | Chosen from the symbols the application carries, or none |
| **Map style overrides** | Optional; empty keeps the default style |
| **Details schema** | What a feature of this kind is asked for. Only flat text, number, yes/no and choice fields appear on the form, and the dialog previews the fields the feature form will offer |

**Add a kind** for something your club records that nothing shipped describes. The kinds that
ship with the product are marked **Ships with the product**: their code, category, protected
display and need for a parent stay as shipped, they keep every shape they ship with, and they
cannot be deleted — but their wording, symbol, order and details are yours.

**Cave** is the one kind marked **Every cave**: every cave carries it, whichever way it was
made — the cave form, a catalogue or file import, a phone, a photograph — and nothing else can
be given it, so it is not offered when you draw or import other features. It is where your
club's **own cave fields** go ("rigging needed", "bat colony"): add them to its details schema
and they appear on the cave form under **Club fields**, on the cave's page, and in the filter
as *Cave → field*, beside *Type is Cave*. The fields it ships with are the codes a catalogue
import, a phone or a photo library writes; they keep their names and types, the schema stays
open to fields it does not list, and no field can be made required — otherwise imported caves
would be refused the next time somebody edits them. How a protected cave is shown does not
depend on this kind at all: caves are always shown snapped for somebody without exact view.

There is one **pit**: the cave type *Pit / Aven*. The surface kind *Pit* no longer ships; an
installation that already had features of that kind keeps it as one of its own kinds, which
you can retire by giving those features another kind and deleting it. **Hill** is an outline,
like *Massif / Mountain*, and is treated the same way everywhere a massif is.

Once features of a kind exist (deleted ones included), the kind can only be **loosened**: its
category stays, no shape can be taken away and it cannot start needing a parent, because the
stored features would stop agreeing with it. A kind can be deleted only while no feature uses
it.

Needs **write** on taxonomies, like document kinds: a feature kind decides what every feature
of it may be drawn as and how a protected one is shown.

Related: [Features](../features/surface-features.md)

---

## Configuration → Trip purposes

**What it governs:** three things at once.

1. **What a trip was for** — the eight shipped kinds are *Exploration · Survey / mapping ·
   Maintenance / rigging · Training · Tourism / visit · Rescue · Science · Other*. A club
   that runs something nobody thought of adds it.
2. **What a report of that kind asks for** — the three report sections (field data,
   logistics, safety), drawn from a form the club describes once.
3. **Which [checklist](../features/checklists-and-callout.md)** trips of that kind work
   through.

> Changing what a purpose asks for **does not invalidate a single report already written**.

Related: [Trips](../features/trips.md)

---

## Configuration → Trip roles

**What it governs:** what somebody did on a trip. Every roster row renders its job from this
list.

Ships with: *Participant · Proposer · Leader · Driver · Surveyor · Photographer · Trainee ·
Instructor · Callout contact*. **Attending and proposing are there from the start and cannot
be removed.**

Somebody who did two jobs is recorded doing both rather than made to choose.

---

## Configuration → Camp roles

**What it governs:** what somebody was at a [camp](../features/camps.md) as. Every row of a
camp's *Who was there* list renders its role from this list.

Ships with: *Member · Organiser · Cook · Base camp · Driver · Medic · Equipment · Guest*.
**Member is there from the start and cannot be removed** — without it a camp could not record
that somebody was simply there.

A list of its own, kept apart from the trip roles on purpose: cooking for thirty or keeping the
base camp is not a job underground, so a role added here is never offered on a trip, and a trip
role is never offered at a camp. It is also reached from the camps list, under *Configure*.

---

## Configuration → Report layouts

**What it governs:** the layout a trip or a camp is written up in. The two are different kinds
of layout with different words, and each kind has its own standard one and its own chosen one.

Download the standard one for the kind you want — a short text file that explains itself in its
own comments — edit it, upload it, and choose it. A camp layout has a line of its own,
`accounts`, that prints what each trip in the camp wrote about itself; take the line out for a
shorter document.

A layout can also be **one trip purpose's own** — the survey bulletin, say: a trip recorded under
that purpose is written up in it unless somebody picks another, ahead of the layout chosen for
everything else. One layout per purpose; leave the purpose empty for a layout any trip may use.

Two properties that make this safe:

- **A layout can only ask for things the reader was already given.** It cannot be used to
  widen disclosure.
- **A line whose contents turn out to be empty simply disappears**, so the same layout
  produces an honest document for a member and for an editor.

Related: [Trips](../features/trips.md#the-write-up) · [Camps](../features/camps.md#the-write-up)

---

## Configuration → Link relations

**What it governs:** the wording of every [link](../features/links.md).

Ships with *Same object as · Related to · Contains/Contained in · Documented by/Documents ·
Source of/Derived from · Adjacent to · Original of/Duplicate of · Needs clarification*, the
trip relations, and *Text of/Has text*.

Add whatever this archive actually says. A directed relation carries both readings, so a link
says one thing on one page and its inverse on the other.

Restrictions the application enforces:

- A relation that **ships with the product** keeps its code and the way it reads, and cannot
  be deleted.
- A relation that **links already record** cannot be changed that way or deleted.
- Codes are unique.

Needs Full Administrator.

---

## Configuration → Detection rules

**What it governs:** what a waypoint called *Peștera*, *P.*, *aven*, *izbuc*, *ponor* or
*doline* is proposed to become when you [import a GPS file](../workflows/import-a-gps-file.md).

**Not an administrator's page.** Everybody with something to import keeps their own sets.
Only *promoting* a set to what a caving group or the whole installation inherits is an
administrator's act, and that is refused on the server for anybody else.

### A rule set

Has a **name** and a **scope**: *The whole installation · A caving group · You*. Sets can be
**saved as a file** and **loaded from a file** — which is how you hand your conventions to
another club. A set that is not yours can be edited as *a copy of your own*.

### A rule

| Field | |
|---|---|
| **Rule name**, **On** | |
| **Compare by** | Contains · Whole word · Starts with · Regular expression |
| **Terms** | Typed one at a time |
| **Language** | Romanian terms · English terms · Terms in no language (which always take part) |
| **Proposes** | A cave · A cave entrance · A surface feature, and of which **type** |
| **Read the name** / **Read the description** | Where to look |
| **Take the term out of the name** | Keep it · Only from the front · Only from the end · Wherever it is |

> **Order decides.** When two rules claim one candidate, the one higher up wins — and the
> import review tells you which other rules also claimed it.

---

## Configuration → Feature sets

Named sets of features that access rules can be scoped to. Covered under
[Permissions](permissions.md#feature-sets).

---

## Configuration → Message texts

The wording of every message the application sends, **editable per language**.

Wording an operator has rewritten is the wording members see — including in their
notification inbox, which renders lines from these texts in the language each person is
reading the site in.

Related: [Messaging](messaging.md) · [Notifications](../features/notifications.md)

---

## Other taxonomies

Reached from the records that use them rather than from a page of their own:

- **Cave types**, **entrance types**, **rock types** — on the cave and entrance forms.
- **Georeferenced map kinds** — geological, topographic, tourist, cave map, other.
- **Event kinds** — club meeting, training, working day, gear check, conference, deadline.
- **Tags** — free-form, created as you use them, filterable everywhere.

---

## A note on changing a vocabulary later

Adding is always safe. Renaming is safe for anything you added. What is not offered — because
it would silently rewrite the record — is deleting or redefining something records already
use. The application refuses and tells you what is still pointing at it.

That is worth knowing before you design a scheme: **it is much easier to add a document kind
than to merge two of them afterwards.**

---

Related: [Permissions](permissions.md) · [Trips](../features/trips.md) ·
[Documents and cabinets](../features/documents-and-cabinets.md)
