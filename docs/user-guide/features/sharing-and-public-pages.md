# Sharing, QR codes and public pages

[← Feature reference](README.md) · Workflow:
[Share your work with someone](../workflows/share-your-work.md)

---

Every surface a visitor **without an account** can reach. There are six, and that is the
complete list.

| Surface | What a visitor gets |
|---|---|
| **A share link** | One record (optionally with what it contains) |
| **A shared view** | A saved map configuration |
| **A shared album** | One album of photographs, as renderings |
| **The public gallery** | The installation's curated photographs |
| **A printed QR code** | Confirmation that the code is registered. Nothing else |
| **The unsubscribe page** | Opened from a mail client |

An installation that publishes nothing shows a visitor **the sign-in page** and nothing more.

---

## Share links

On a feature, a cave or a saved view: **Share → Create link**.

| Choice | |
|---|---|
| **Access** | *Public* or *Requires sign-in* |
| **Include contained records** | Whether the subtree comes with it |

**The token is shown once.** *"Copy it now — this link is shown only once."* If you lose it,
revoke and mint a new one.

Existing links list their **created** date and **status** (*Active* / *Revoked*).
**Revoke** kills one immediately, and labels already handed out stop working.

A shared record renders as a **read-only view**, with the included records listed. A
sign-in-required link tells an anonymous visitor to **sign in** rather than showing them
nothing.

> **A share never reveals a protected location.** Sharing is not a route around
> [location protection](../admin/location-protection.md), and it was designed so it could not
> become one.

---

## Shared views

See [Saved views](saved-views-and-windows.md#saved-views). Same once-only token, same
revocation, same protections.

---

## The public gallery and shared albums

**The public gallery** (`/gallery/public`) is a curated page anybody can reach. A photograph
appears there because somebody ticked **Show in the public gallery** on it — a *separate
decision* from who may read it inside the application.

**A shared album** is one album handed out by its own link, again shown once.

Both show **renderings only** — never the original file, never its camera metadata.

If nothing has been published: *"Nothing has been published yet."*

---

## Printed QR codes

For clubs that bolt labels to cave walls.

### Publishing

From a cave's page: **Printed codes**.

| | |
|---|---|
| **Codes resolve for anyone** | Published |
| **Codes resolve for nobody** | Not published, or withdrawn |

**Publish** / **Stop publishing**, with dates recorded for each. Withdrawing means *"labels
already in the field will answer nothing."*

A cave that carries no code of its own says so — **places inside it carry theirs**, and each
resolves through the cave's decision. Open a place to see its square.

### What a scan actually shows

> *"The code you scanned is registered with this installation, and that is all this page says:
> no cave, no place and no position. **An account changes nothing here — the answer is the
> same for everyone.**"*

If the code is not registered, or the cave is not published:
*"This code is not registered here, or the cave it belongs to is not published."*

That is the whole design. It confirms a label is genuine and known, without being a lookup
service for cave locations.

### Codes that cannot be printed

Some codes contain characters an address has to escape, and phone scanners read an address
exactly as written rather than unescaping it. The application detects this and **says the
code cannot be printed as a label** rather than letting you bolt a broken one to a rock.

### The address

**Copy address** gives you the URL the label should encode. It is a path route, not a
fragment — deliberately, because scanners discard anything after a `#`.

---

## What is never public

- **A trip report.** There is no public address for one; it is downloaded by somebody signed
  in who may read the trip.
- **An exact protected location**, through any of the above.
- **The other ends of a link** you may not see.
- **A cave name in a notification email.**

---

Workflow: [Share your work](../workflows/share-your-work.md) ·
Related: [Location protection](../admin/location-protection.md) ·
[Permissions](../admin/permissions.md)
