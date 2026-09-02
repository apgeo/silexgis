# Mobile and offline sync

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/mobile-sync.md)

[← Feature reference](README.md) · Related:
[Your account and settings](account-and-settings.md) ·
[Location protection](../admin/location-protection.md)

---

The web client expects a connection. **Offline is what the phone app is for.**

SilexGIS serves a row-level sync API for **SpeleoLoc**, a cave-navigation application for
phones. A phone signs in as an installed application, names the caves it carries, reads them a
page at a time, and writes its own edits back — with the server arbitrating each row.

This page is your side of that: what you configure, what a device can and cannot get, and what
happens to what it sends back.

---

## Sync selections

**Settings → Sync.** A **selection** names the caves a phone carries offline.

> *"A selection names caves — it never takes them over — so ending one here stops a device
> asking for them and changes nothing about the caves themselves."*

### Creating one

| Field | |
|---|---|
| **Name** | |
| **Caves** | Searched by name and added to the selection |
| **New records are visible to** | What a device's own records get when it sends them back |
| **Caving group** | Required if the upload visibility is *caving group* |

The count is shown: *"Caves carried: n."*

**Upload visibility defaults to only you, until you widen it.** Choose *caving group* and you
must name the group — *"Without one, records visible to a caving group are visible to
nobody"* — and you must be a member of it, or a device cannot create records for it.

### Things that go wrong, and what they mean

- **A cave you can no longer read** appears as such in the list. Remove it and save again.
- **"This selection was changed somewhere else"** — on a device, or in another tab. The
  selection carries a revision, so a change made from two places does not silently overwrite
  itself. Reopen it and make the change again.

### Revoking

**Revoke** stops a device being able to ask for those caves. **Nothing already on the device is
deleted** — you are ending an entitlement, not reaching into somebody's phone.

### Whose selections are whose

A selection is **yours**. Whether a full administrator may read somebody else's is an
installation setting, and it is off unless switched on.

---

## What this server speaks

The Sync page reports what the installation offers:

- the **protocol version** — a statement about the code, not a setting an installation can
  lower,
- the **limits** — rows per download page, rows per upload,
- what is **available**, announced by name: *download*, *upload*. A device meeting a server
  that serves only some of them **takes the parts that are there** rather than failing at the
  first transfer. Some installations show *"Selections only — this server does not move rows
  yet."*

---

## What a device cannot get

Rows you may not have are **absent** — not approximated.

That differs from the rest of the application on purpose, and the reason is worth
understanding:

> Elsewhere, somebody who may not place a protected cave is shown a grid-snapped point flagged
> *approximate*. A snapped coordinate on a map is looked at once. **A snapped coordinate
> delivered to a phone is written in cleartext, kept for as long as the app is installed, and
> re-shared to people this server never authenticated.** And these rows come in numbers: a
> scatter of protected places snapped into the same few grid squares **outlines the cave the
> protection exists to hide.**

Three consequences:

**The decision is per row, never per cave.** A place inside a cave can be its own protection
root, independently of the cave containing it — so a filter that asked only about the cave
would hand an independently protected inner point to somebody entitled to the cave and not to
the point.

**Withheld rows are removed before the page is cut**, not after. A page that shortened would
tell a device *how many* rows it was not allowed to have.

**It fails closed.** An identifier with no row behind it is reported as withheld rather than as
absent.

See [Location protection](../admin/location-protection.md).

---

## What happens to what a device sends back

A phone uploads a batch of rows, applied in the order given, **arbitrated row by row** against
what the server already holds. Three properties hold it together, and each exists because of a
specific way an offline device breaks:

| Property | Because |
|---|---|
| The batch carries **an identifier the device minted** | An answer lost on the way back costs a resend, not a duplicate registry |
| A row carries **the identifier the device gave it**, adopted verbatim | Neither side ever has to translate the other's names |
| A row carries **the server revision the device last saw** — the only thing compared | Never the device's own clock, which is unsynchronised, resettable by whoever holds the phone, and routinely wrong by hours |

### Every upload is an import batch

This is the part worth knowing as a user.

**An upload from a phone is recorded exactly like an imported file.** It appears under
**Geodata → Imports**, marked as a phone upload — which means a caver can look at what a phone
put into the registry and **take all of it back in one act**, through the same screen and the
same **Undo** button that reverses a bad GPX.

See [Geodata → Imports](geodata.md#imports).

### Defaults and duplicate warnings

A device that does not name a kind gets a sensible default — a cave lands as a cave, an
entrance as a natural entrance, a place inside a cave as a place.

And each created row is told about **the nearest few things already in the registry**, so a
caver deciding whether they have just re-entered a cave the club already holds gets the nearest
handful — not a census of the massif.

---

## Signing a device in, and taking it back

A phone signs in **as itself** — as an installed application with its own registration, not by
being handed your password, and not by borrowing the browser's. Its refresh credential lasts
substantially longer than a browser session's (45 days by default, and an operator can set it
between 1 and 365).

That long window is only defensible because it can be taken back, so:

> **Changing or resetting your password revokes every token and every authorisation on the
> account — every client, every device, including the one you are using.**

That is the answer to a lost phone, and it is one action any caver can reach without an
administrator. You are signed out everywhere and sign back in where you still have the device.

---

## For whoever is writing a client

The wire contract is documented separately and is not user documentation:

- [`docs/speleoloc-sync/`](../../speleoloc-sync/README.md) — where to start
- Recorded exchanges under `contract/speleoloc-sync/` are the specification of the wire, and
  are compared byte for byte by the test suite

---

## Also on a phone: the web application itself

The web client works on a phone. The map workspace adapts to touch, **including full geometry
editing by finger**, and the application **installs to a home screen**.

It is not offline. But for reading a document at a trailhead, answering an invitation, or
ticking a checklist, it works.

---

Related: [Location protection](../admin/location-protection.md) ·
[Geodata](geodata.md#imports) ·
[Your account and settings](account-and-settings.md)
