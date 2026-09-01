# The national cave catalogue

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/cave-catalogue.md)

[← Feature reference](README.md) · Related: [Caves and entrances](caves-and-entrances.md) ·
[Geodata and imports](geodata.md)

---

**Cave catalogue** searches [speologie.org](https://www.speologie.org) — the Romanian community
register of published caves, about 8,650 of them — and lets you bring caves from it into your own
installation without re-typing them.

It is two screens. The first only searches; it creates nothing, and most visits stop there because
what you wanted was to read what the register says and follow the link to it. The second imports the
caves you picked.

## Before it works: a key

The catalogue is somebody else's service and it is reached with a **personal API key**. Get one from
*Editează profilul* on a speologie.org account, and ask an administrator to set it on this
installation.

Until one is set, the screen says so and does nothing else. That is not a fault: most installations
of this application have nothing to do with the Romanian register, and one without a key works
exactly as it always did.

**Searching needs the right to create caves**, even though searching creates nothing. The searches
go out over your installation's own key against a small volunteer-run service, so the page is offered
to the people who have a reason to be looking for caves to add.

---

## Searching

Give a name, a county, or both. A search naming neither is refused — the register is not something to
page through from the beginning.

**The county is chosen from a list, not typed.** The catalogue matches counties on the two-letter
code (`BH`, `GJ`, `HD`), exactly and case-sensitively. Typing *Bihor* finds nothing at all, which is
why there is no box to type it into.

### Why the screen sometimes says it searched for three spellings

Romanian is properly written with `ș` and `ț` (comma below). For about two decades the fonts and
keyboards in circulation produced the Turkish cedilla letters `ş` and `ţ` instead, and a great deal of
Romanian was written with those. A good deal of this catalogue was typed with no diacritics at all.

**The catalogue's search treats all three as different words**, and the effect is not subtle:

| you type | the catalogue finds |
|---|---|
| `Padis` | 1 cave |
| `Padiș` | none |
| `Padiş` | 2 caves |
| `Scarisoara` | none — the famous ice cave is filed as *Scărișoara* |

So typing without diacritics, which is what most keyboards make you do, usually finds **nothing** —
and a cave that is in the register looks exactly like a cave that is not.

This screen works around it: it asks for up to two dozen spellings of whatever you typed, all in one
request, and tells you how many it used. You do not need to know any of this to use the box.

**The workaround is good, not perfect.** A long name needing two accents in different letters can
still fall outside what is tried — *Scarisoara* is the awkward example. If a search comes back empty
and you think the cave exists, type the accents.

One consequence worth knowing: **paging past the first page is approximate.** Each spelling is paged
separately at the far end, so a later page can be short or repeat something. Narrow the search rather
than paging deep.

### What each row tells you

Length, depth, altitude and protection class as the register holds them, plus two things it does not:

- **Whether this cave is already in your installation.** If you may see the cave, the row links to it.
  If you may not, it still says the entry is already here without naming the cave — otherwise you
  would be invited to import a second copy of something a colleague already has.
- Tags for a cave the register records as **lost** or as having a **sump**.

Clicking the name opens everything the register holds, including the description. **Open on
speologie.org** goes to the cave's own page there.

---

## Importing

**Import** on a row, or tick several rows and use **Import selected**. Either way you land on the
import screen with those caves listed, and nothing has been created yet.

### The thing to understand first: there are no coordinates

**The catalogue publishes no coordinates. Not approximate ones, not withheld ones — the field does
not exist.**

So a cave imported from it arrives **with no position**. It is a real cave in your registry, it turns
up in the cave list and in search, it has its length and depth and description — and it is on no map,
because a cave's position here is the position of its main entrance and it has no entrance.

You have two honest options and the screen offers both:

1. **Leave it unplaced.** Perfectly reasonable. Find them later under **Caves** with the *no position*
   filter and place them as you learn where they are.
2. **Place it now.** Press **Place on the map** on a row and click the map. That creates a main
   entrance at the point you clicked, recorded as *read off a map* rather than as a GPS reading —
   because that is what it is.

Nothing invents a position for you. A county's centre is not a cave, and a coordinate that looks
surveyed and is not is worse than an empty field.

### Choices that apply to everything you import

**Visibility** (private unless you say otherwise), a **caving group** to bind them to, and whether
they are **location-protected**. These are the same choices any import here offers, and the defaults
are the most restrictive ones.

### Per cave

- **What to do** — create it, refresh it, or leave it alone. A cave that was imported before is
  proposed as a refresh rather than a duplicate.
- **Kind** — worked out from the name (*Peștera …* is a cave, *Avenul …* is a pit) using the same
  naming rules a file import uses. Override it if the name misleads.
- **Position** — optional, as above.

### What refreshing overwrites

Refreshing a cave that came from the catalogue rewrites **the fields the catalogue owns**: name,
description, region, altitude, length, depth and protection class. Everything else you have recorded
on that cave — entrances, surveys, photographs, documents, tags, links, your own notes — is left
alone. If you have edited one of the catalogue's own fields by hand, a refresh will replace your
edit.

### Undoing

The whole import is **one batch**. Undo it from **Geodata → Import history**, the same place and the
same button that reverses a bad GPX. Everything it created goes; a cave it merely refreshed stays,
because the batch did not create it.

---

## What is imported, and what is only kept

Length, depth, negative depth, altitude, protection class, mountain range and nearest locality go
into the cave's own fields. The description is converted from the catalogue's formatting into plain
text and a very long one is shortened, with a note saying so and a link to the full page.

The register's own codes — its identifier, county code, rock code, hydrologic number, basin
identifier, protected-area code, scientific interest, and whether it is recorded as lost — are kept
on the cave as extra properties, and repeated in a short note at the end of the description so a
reader sees them without hunting.

**Two are deliberately kept as written rather than interpreted.** The **rock code** (`00`, `05`, …)
has no published legend, so it is not turned into a rock type — filling in a geology on a guess would
be worse than leaving the field empty. The **basin identifier** is a number whose names that interface
does not publish.

**The nearest locality is stored where a location is stored** — in the cave's address field, which is
withheld from people who may not see a protected cave's exact position — and not in the description,
which is shown to everyone.

---

## Being a good guest

This installation talks to speologie.org **one request at a time, with a pause between them**. That
is deliberate and it is why a search takes a moment. There is no button that downloads the register,
no scheduled sync, and no way to page through it from the beginning.

That register is maintained by volunteers and paid for by somebody. Caves imported from it keep a link
back to the page they came from, so the credit travels with the data. If your club needs the whole
catalogue rather than the caves you are working on, ask them — do not take it a page at a time.

---

[← Feature reference](README.md)
