# Workflow: file the club archive

🇬🇧 **English** · 🇷🇴 [Română](../ro/workflows/file-the-archive.md)

[← Workflows](README.md) · Reference:
[Documents and cabinets](../features/documents-and-cabinets.md) ·
[Uploads](../features/uploads.md)

---

A box of paper, a hard drive of scans, thirty years of bulletins. This is the workflow for
getting it in and making it findable.

---

## Step 1 — Design the filing tree first

**Cabinets** are the tree documents live in: *Club archive / Bulletins / 1987*. Spend an
hour on this before you upload anything, because a cabinet is also **the unit permissions
are granted on** — one rule can hand a committee the whole archive instead of one rule per
document.

Some things to know while you design it:

- **A document can sit in several cabinets at once.** There is no move-versus-copy question,
  and nothing is orphaned by belonging in two places.
- **Filing moves who can read it.** Putting a document on a shelf a rule names gives that
  rule's subjects access to it. The application says so where the control is.
- Cabinets nest, to a bounded depth.

### What lands here

Each cabinet can carry defaults applied to whatever is uploaded into it:

- **Document kind** (or *same as the cabinet above*),
- **Who can read it**,
- **Tags** applied to everything filed here.

And a separate, softer thing: **Metadata expected here**. This is a *checklist, not a rule*.
Uploads are never refused for missing these; documents that lack them are simply marked
**Incomplete**, so you can find them later. That is the mechanism for "every permit should
record its expiry" without blocking somebody at 23:00 who just wants the scan stored.

## Step 2 — Decide your document kinds

**Configuration → Document kinds.** A document has a *kind* — survey report, permit, trip
report, bulletin, correspondence, whatever your club files — and **each kind decides which
details its documents are asked for**.

This is what makes the archive organisable the way your club actually files things rather
than the way we guessed.

## Step 3 — Get the files in

Three routes, depending on volume:

### A few files
Drag them onto a cabinet, or onto the record they belong to (a cave, a trip, a feature).

### A batch
**Uploads.** Drop files or choose a folder. Before you start, the page tells you the largest
file accepted, the room left for you, and which types are accepted or refused.

You can:
- **Name the batch** — everything uploaded together is findable again by that name,
- **Tag everything** with one tag (created if it does not exist yet),
- **Expand ZIP archives on the server, keeping their folders** — folders become cabinets.

If a file's contents are already stored, you are asked whether to store this copy as well or
skip it. The summary reads *"n of m stored · k skipped · j failed"*, with a reason per row —
already stored, larger than accepted, type not accepted, not enough room, empty, folder path
cannot be filed, you may not file into that cabinet, unreadable.

Failed rows can be retried in one press.

### A directory the server can already reach
**Uploads → Import from a server directory**, if your installation is configured for it.
This copies files without sending them over the network — the right answer for a hard drive
plugged into the server. Folders become cabinets.

## Step 4 — Let the text be read

The text of an uploaded document is read in the background. Supported: **PDF, Word, Excel,
PowerPoint, LibreOffice, Rich Text, plain text, Markdown and CSV** — including pre-2007 Word
and PowerPoint, and files written in the older Central European code pages Romanian archives
are full of.

A document's page shows how far reading has got:

| State | Means |
|---|---|
| *Reading the text…* | In progress |
| *The text has been read* | Done — it is searchable |
| *This document has no text layer* | It is pictures of pages. There are no words in it to read |
| *This kind of file holds no text to read* | e.g. an image or a recording |
| *The text could not be read* / *cannot read this format yet* | Exactly what it says |

> **Nothing recognises text in a photograph.** Scanned pages and image-only PDFs have no text
> to search, and the page says that instead of leaving you waiting for words that are never
> coming. If your archive is mostly scans, plan for typing up the important ones.

A **password-protected** document is reported as locked, rather than read into nonsense.

> **Filed the archive before all this was working?** That is the normal order of events. An
> administrator runs a [maintenance sweep](../admin/maintenance.md) and the whole backlog
> becomes readable and searchable without re-uploading anything.

The **language** is worked out from the document's own text, and left *unset* rather than
guessed when the text does not say clearly. Anyone who may edit the document can correct it,
which re-indexes it on the spot. Words are stemmed in the document's language — Romanian and
English out of the box.

## Step 5 — Find what you filed

The same search box that finds caves and trips finds **documents by what is written in
them**, quoting the sentence that matched.

- Searching is **accent-insensitive both ways**: `pestera` finds `peșteră`, and the result
  is quoted back spelled the way its author wrote it.
- A search hit opens the document's page **at the page the phrase was found on**.
- You only ever find documents you are allowed to read.
- **Replaced versions** are searchable only by the people who could replace them — a
  paragraph removed in a new version does not stay findable in the old one.

## Step 6 — Read without downloading

A document has its own page: what it is, which version is current, where it is filed, how far
reading has got, and the document itself:

- **PDFs** a page at a time, as pictures drawn by the server,
- **photographs**,
- **plain text and Markdown**,
- **audio and video** played in the browser.

Nothing is transcoded and no scanned page is invented. Where a format cannot be shown, the
codec is not one your browser plays, or the file holds no text, the page says **which of
those it is** and offers the download.

It works on a phone. And somebody allowed to read a protected cave's report can read it there
without ever being able to download the file.

## Step 7 — Talk about it where it lives

Every document page carries a **discussion**: who recognises the cave in an unlabelled 1987
photograph, which survey superseded which.

- Replies go **one level deep**, so a thread stays readable.
- The person who wrote a remark can correct it; they or an administrator can take it down.
- Whoever may read the document may join in — and **nobody else can even tell the
  conversation is there**, because a remark is only reachable through the document it sits on.
- Notifications about replies and about comments on your own uploads are two separate
  switches, so switching off traffic on a busy document never costs you answers meant for you.

## Housekeeping

- **Not filed** — everything you have added that is not in a cabinet yet, waiting to be
  filed. Filing is a later decision, and this is where the backlog lives.
- **Versions** — a document can be replaced by a new version; the old ones are kept.
- **Bulk filing** — select documents and *Also file in…* (adds a cabinet) or *Move to…*
  (adds the new one and takes them out of this one).
- **Who has read this** — a document's owner, or somebody with the right over the audit
  trail, can see who has taken a copy. Being allowed to read a document does *not* make its
  readership yours to see. Your own reading history is under your account, and nobody can ask
  for somebody else's by naming them.

---

Next: [Share your work](share-your-work.md) ·
Reference: [Documents and cabinets](../features/documents-and-cabinets.md)
