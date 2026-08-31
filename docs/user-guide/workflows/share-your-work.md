# Workflow: share your work with someone

[← Workflows](README.md) · Reference:
[Sharing, QR codes and public pages](../features/sharing-and-public-pages.md) ·
[Permissions](../admin/permissions.md)

---

Somebody outside needs to see something. A landowner, a journal editor, another club, a
government office, the public. There are five different mechanisms and they are not
interchangeable — picking the wrong one either fails to reach them or discloses more than you
meant.

---

## Decide first: who is this person?

| They are | Use |
|---|---|
| A member of this installation | **Grant them access** — a permission rule or the record's visibility |
| Somebody in your club with no account | **Nothing works.** They must be given an account, or reached another way |
| A stranger who needs one specific record | **A share link** |
| A stranger who needs to see a map you configured | **A shared view** |
| Anybody at all, for pictures | **The public gallery**, or a **shared album** |
| Somebody standing at the cave with a phone | **A printed QR code** — which tells them almost nothing |

---

## 1. Grant access to a member

Two levers.

**Change the record's visibility** — private, caving group, authenticated, public. Blunt but
often right: making a cave *authenticated* means everybody with an account can read it.

**Write a permission rule.** On the object itself (its *Permissions* tab), or in a
[permission group](../admin/permissions.md) they belong to. A rule names a subject (a user or
a caving group), an effect (allow or deny), the actions, and a scope.

Scopes worth knowing:

- **This object only**
- **This object and everything inside** — the subtree
- **A feature set** — a named set of features rules can point at
- **A cabinet and everything filed below it**
- **A caving group's content**
- **Own objects**, **Everything**

> A **deny** outweighs every allow at the same level, and a rule written directly on an
> object outweighs anything inherited. The dialog says so before you save.

If you are not sure why somebody can or cannot see something: every object has **"Your access
here, and why"**, which names the rule that decided it.

## 2. A share link — one record, revocable

On a feature, a cave, a saved view: **Share → Create link**.

You choose:

- **Public** or **Requires sign-in**,
- **Include contained records** — whether the subtree comes with it.

The link is **shown once**. Copy it then; it cannot be recovered afterwards. If you lose it,
revoke it and mint a new one.

Existing links are listed with when they were created and whether they are *Active* or
*Revoked*. **Revoke** kills a link immediately.

> **A share never reveals a protected location.** The person following the link sees the
> record under the same protection rules as an anonymous visitor. Sharing is not a way around
> location protection, and it was designed so it could not become one.

## 3. A shared map view

Save the current map — its extent, its layers, its filters — as a named **saved view**
(*Saved views → Save current view*). Then **Copy share link**.

Someone opening that link gets the map as you configured it, with protections intact. Useful
for "here is the area we are talking about" without giving anybody an account.

You can also **Export map image** for a picture to paste into a document.

## 4. Photographs: the public gallery and shared albums

Two separate things.

**The public gallery** (`/gallery/public`) is the installation's curated shop window. A
photograph appears there because somebody ticked **Show in the public gallery** on it. That
is a *separate decision* from who may read it inside the application.

**A shared album** is one album handed out by its own link. Album → share link, same
once-only, same revocation.

Both show **renderings and nothing else** — never the original file, never its embedded
camera metadata.

Give photographs a **credit and licence** before you publish them. The licence list runs from
CC0 through the CC BY family to *All rights reserved*, plus *Nobody has said*.

## 5. A printed QR code at the cave

If your club bolts labels to cave walls, the address on the label resolves — but only just.

From a cave's page: **Printed codes**. You either **Publish** (codes at this cave resolve for
anyone) or **Stop publishing** (labels already in the field answer nothing).

What a stranger who scans it actually gets: a page saying **the code is registered with this
installation, and nothing else.** No name, no position, no photograph. Having an account
changes nothing — the answer is the same for everyone.

That is the entire point. It confirms the label is genuine and the cave is known, without
being a lookup service for cave locations.

---

## What sharing never does

- It never hands out an exact position that
  [location protection](../admin/location-protection.md) withholds.
- It never discloses a link's other ends to somebody who may not see them.
- It never publishes a trip report — there is no public address for one. A report is
  downloaded by somebody signed in who may read the trip.
- It never sends a cave's name or position in a notification email.

---

## Writing to your own club

Not really sharing, but adjacent: somebody entrusted with it can **Announce** to a caving
group — one line that reaches every member with an account, each in the way they chose (mail
or in-app), rather than as a mailing list nobody can leave.

Before you send, you are told **how many people it reaches**, and confirming is a step of its
own. A message to two hundred people is never one careless click. See
[People and clubs](../features/people-and-clubs.md#announcing-to-a-club).

---

Reference: [Sharing, QR codes and public pages](../features/sharing-and-public-pages.md)
