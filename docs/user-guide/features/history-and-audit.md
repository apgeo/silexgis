# History and audit

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/history-and-audit.md)

[← Feature reference](README.md) · Related:
[Permissions](../admin/permissions.md)

---

Three separate mechanisms, for three separate questions.

| Question | Mechanism |
|---|---|
| *What changed on this record, and can I put it back?* | **History**, on the record itself |
| *What has been happening across the installation?* | **The audit trail** |
| *Who has taken a copy of this document?* | **Access history** |

---

## History (per record)

Every cave, entrance, feature, trip, centerline, survey model, attachment, tag, participant,
camp, invitation and event carries a **History** section.

It shows, per change: **when**, **who** (or *System*), the **action** (Created · Updated ·
Deleted), and the fields that changed with their before and after values.

### Restoring

Two actions:

- **Restore this value** — put one field back,
- **Restore all values from before this change** — put the whole record back to its prior
  state.

This is what makes it safe for several people to maintain the same records. A wrong bulk edit
is a nuisance, not a disaster.

### Protected values in history

A value that is part of a protected location reads **"Value hidden (protected location)"**
rather than being exposed.

That matters more than it sounds: a change history is exactly the sort of side door through
which a withheld coordinate escapes, and it is closed.

### Fields with readable names

The history names fields in words, not columns: Name, Description, Location, Altitude,
Position quality, Closest address, Land registry number, Location notes, Linked cave,
Visibility, Caption, and so on.

---

## The audit trail

**Administration → Audit.** A view over the whole installation, for whoever holds the right
over the audit domain.

| Column | |
|---|---|
| **When** | |
| **User** | |
| **Action** | |
| **Entity** | Filterable by type — e.g. `Cave` |
| **Entity id** | |

This is the *what happened here* view rather than the *what happened to this record* view.

---

## Access history — who has read a document

Two surfaces, with deliberately different rules.

**Your own reading history** — documents you have taken a copy of, newest first. Needs no
permission beyond being you.

**Who has read this document** — for a document's **owner**, or somebody with the right over
the audit trail.

> **Being allowed to read a document does not make its readership yours to see.** A club
> member could otherwise learn which other members had been looking at a particular survey,
> which is a fact about *them* and one they never agreed to publish.

The document's owner is included because they are answerable for what they put in and may
reasonably ask who took a copy.

**Nobody can fetch somebody else's reading history by naming them.** The per-document view is
the only way one person's reading is visible to another, and it is bounded to a document they
already administer.

A document you may not read answers *"not found"* rather than *"forbidden"* — because a
history that answered differently for a document that exists would disclose its existence.

---

## What is recorded about actions taken on behalf of others

Where somebody acts for somebody else — an administrator putting a given-up notification back
by hand, an organiser answering an invitation for a person who phoned in — **the act is
recorded against the person who made it**, not against the person it was made for.

---

Related: [Permissions](../admin/permissions.md) ·
[Location protection](../admin/location-protection.md)
