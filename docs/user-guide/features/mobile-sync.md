# Mobile and offline sync

[← Feature reference](README.md) · Related:
[Your account and settings](account-and-settings.md)

---

The web client expects a connection. **Offline is what the phone app is for.**

SilexGIS serves a sync API for **SpeleoLoc**, a cave-navigation application for phones. A
phone signs in as an installed application, names the caves it carries, reads them a page at
a time, and writes its own edits back — with the server arbitrating each row.

This page is about **your side of that**: what you configure in the web application.

---

## Sync selections

**Settings → Sync.**

A **selection** names the caves a phone carries offline.

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

**Upload visibility defaults to only you, until you widen it.** If you choose *caving group*
you must name the group — *"Without one, records visible to a caving group are visible to
nobody"* — and you must be a member of it, or a device cannot create records for it.

### Things that go wrong, and what they mean

- **A cave you can no longer read** appears as such in the list. Remove it and save again.
- **"This selection was changed somewhere else"** — on a device, or in another tab. Reopen it
  and make the change again.

### Revoking

**Revoke** stops a device being able to ask for those caves. **Nothing already on the device
is deleted** — you are ending an entitlement, not reaching into somebody's phone.

---

## What this server speaks

The Sync page also reports what the installation offers:

- the **protocol version**,
- the **limits** — rows per download page, rows per upload,
- what is **available**: on some installations, *"Selections only — this server does not move
  rows yet."*

---

## What a device cannot get

The same rules apply as everywhere else. A position you may not have is **simply absent** on
the device — not approximated into something misleading, not withheld with a placeholder that
implies more than it says.

Whether an administrator may read another account's sync set is an installation setting, and
it is off unless switched on.

---

## Signing a device in

A phone signs in **as itself** — as an installed application with its own identity, not by
being handed your password. One action a caver can reach takes **every** credential back,
which is the answer to a lost phone.

---

## For whoever is writing a client

The wire contract is documented separately, and is not user documentation:

- [`docs/speleoloc-sync/README.md`](../../speleoloc-sync/README.md) — where to start
- Recorded exchanges under `contract/speleoloc-sync/` are the specification of the wire

---

## Also on a phone: the web application itself

The web client works on a phone. The map workspace adapts to touch, **including full geometry
editing by finger**, and the application **installs to a home screen**.

It is not offline. But for reading a document at a trailhead, answering an invitation, or
ticking a checklist, it works.

---

Related: [Sharing and public pages](sharing-and-public-pages.md#printed-qr-codes) ·
[Your account and settings](account-and-settings.md)
