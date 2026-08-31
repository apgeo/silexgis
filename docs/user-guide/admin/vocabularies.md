# Vocabularies your club owns

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

## Configuration → Report layouts

**What it governs:** the layout a trip is written up in.

Download the standard one — a short text file that explains itself in its own comments — edit
it, upload it, and choose it.

Two properties that make this safe:

- **A layout can only ask for things the reader was already given.** It cannot be used to
  widen disclosure.
- **A line whose contents turn out to be empty simply disappears**, so the same layout
  produces an honest document for a member and for an editor.

Related: [Trips](../features/trips.md#the-write-up)

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
- **Feature types**, with their symbols and their typed properties — on the feature forms.
- **Georeferenced map kinds** — geological, topographic, tourist, cave map, other.
- **Camp roster roles** — member, organiser, cook, base camp, driver, medic, equipment, guest.
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
