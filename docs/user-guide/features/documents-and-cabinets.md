# Documents and cabinets

[← Feature reference](README.md) · Workflow:
[File the club archive](../workflows/file-the-archive.md)

---

The club archive: reports, permits, bulletins, correspondence, scans, spreadsheets, audio,
video. Filed in a tree of **cabinets**, searchable by what is written inside them, readable
without downloading, and discussable in place.

---

## Cabinets

**Library → Cabinets.** A filing tree — *Club archive / Bulletins / 1987*.

A cabinet is also **the unit permissions are granted on**. One rule can hand a committee the
whole archive instead of one rule per document. Scope: *a cabinet and everything filed below
it*.

Consequences worth stating plainly:

- **A document can sit in several cabinets at once.** No move-versus-copy question; nothing
  is orphaned by belonging in two places. Each cabinet a rule names reaches it.
- **Filing moves who can read it.** Putting a document on a shelf, or taking it off, changes
  access. The application says so where the control is.
- A cabinet that rules still point at cannot be deleted until those rules are removed. Nor
  can one that still holds cabinets or documents.

### What lands here

Defaults applied to whatever is uploaded into a cabinet:

- **Document kind** — or *same as the cabinet above*,
- **Who can read it**,
- **Tags** applied to everything filed here.

### Metadata expected here

A **checklist, not a rule.** Uploads are never refused for missing these. Documents that lack
them are marked **Incomplete** and say what is *still missing*, so the backlog is findable.

### Not filed

Everything you have added that is not in a cabinet yet. Filing is a later decision, and this
is where the untriaged pile lives.

### Bulk filing

Select documents, then **Also file in…** (adds a cabinet, moving nothing) or **Move to…**
(files them in the new cabinet and takes them out of this one). Partial results are reported:
*"n filed; m could not be."*

---

## Document kinds

**Configuration → Document kinds.** A document has a kind — survey report, permit, trip
report, bulletin, … — and **each kind decides which details its documents are asked for**.
That is what lets an archive be organised the way a club actually files things.

See [Vocabularies](../admin/vocabularies.md#configuration--document-kinds).

---

## A document's page

What it is, which version is current, where it is filed, how far reading its text has got,
and the document itself.

| Field | |
|---|---|
| **Title** · **Document kind** · **Author** | |
| **Who may read it** | And a **caving group** it can be bound to |
| **Filed in** | The cabinets it sits in |
| **Format** · **Size** · **Version** · **Last changed** | |
| **Language** | Romanian, English, or *Not known* |
| **Document properties** | Whatever its kind asks for |

Documents carry **versions**: upload a new one and the old are kept.

---

## Reading without downloading

The viewer shows:

- **PDFs** a page at a time, as pictures drawn by the server, with zoom and page navigation,
- **photographs**,
- **plain text and Markdown** (large files show the first part and offer the download),
- **audio and video** played in the browser.

**Nothing is transcoded and no scanned page is invented.** Where a format cannot be shown,
the codec is not one your browser plays, the layout service is unavailable, or the file holds
no text at all, the page says **which of those it is** and offers the download. You are never
left looking at a blank panel wondering.

It works on a phone. And somebody allowed to read a protected cave's report can read it there
**without ever being able to download the file**.

You can **select text on a PDF page** to copy it, or to point a [link](links.md) at that
passage.

---

## The text inside

The text of an uploaded document is read in the background. Supported: **PDF, Word, Excel,
PowerPoint, LibreOffice, Rich Text, plain text, Markdown, CSV** — including pre-2007 Word and
PowerPoint, and files in the older Central European code pages Romanian archives are full of.

The **Text** state is one of:

| State | Means |
|---|---|
| *Reading the text…* | In progress |
| *The text has been read* | Searchable |
| *This document has no text layer* | Pictures of pages — there are no words in it |
| *This kind of file holds no text to read* | e.g. an image or a recording |
| *The text could not be read* | Failed |
| *Nothing here can read this format's text yet* | Unsupported |

**Nothing recognises text in a photograph.** Scanned pages and image-only PDFs have no text
to search, and the page says so rather than leaving you waiting.

A **password-protected** document is reported as locked rather than read into nonsense.

The **language** is detected from the document's own text and left unset rather than guessed
when the text does not say clearly. Anyone who may edit the document can correct it, which
re-indexes it on the spot.

## Searching inside documents

The same search box that finds caves and trips finds documents **by what is written in
them**, quoting the sentence that matched.

- **Accent-insensitive both ways**: `pestera` finds `peșteră`, and the result is quoted back
  spelled the way its author wrote it.
- Words are **stemmed in the document's own language** — Romanian and English out of the box,
  and any language PostgreSQL has a stemmer for by adding one row.
- A hit **opens the document's page at the page the phrase was found on** (or sheet, or
  slide).
- You only ever find documents you may read.
- **Replaced versions** are searchable only by the people who could replace them — a
  paragraph removed in a new version does not stay findable in the old one.

See [Search and filters](search-and-filters.md).

---

## Discussion

Every document page carries a discussion.

- **Replies go one level deep**, so a thread stays readable.
- The person who wrote a remark can correct it; they or an administrator can take it down.
- Whoever may read the document may join in — and **nobody else can even tell the
  conversation is there**, because a remark is only reachable through the document it sits on.
- Remarks are **plain words**, shown as the words that were typed.

Two separate notification switches: *someone replies to a comment I wrote*, and *someone
comments on something of mine*. So switching off traffic on a busy document never costs you
the answers meant for you. You are never told about your own remark, and if something is both
a reply to you and a comment on your own upload you hear about it once.

**No message ever repeats what was said** — it names the document and links to it.

---

## Link-annotated text

A document can be a **link-annotated text**: passages of it are highlighted, and following a
highlighted passage **moves the open views** — the map, the 3D scene, the survey viewer, the
image view — to what that passage is about.

- **Where links go** lets you turn each of those off, to leave one where it is.
- The text can be **popped out into a window of its own**, to sit beside the map.
- **Edit links**: select any part of the text and link it to a cave, a document, a trip or
  anything else.
- A link card can say **May have moved** — the words are not where the link recorded them, so
  the highlight may be on the wrong passage.
- **Show** moves the open views without leaving the text; **Open** goes to the thing itself.

This is the mechanism for a written account of an exploration that drives the map as you read
it. Where a record has nothing written about it, the section offers *Write one*.

---

## Who has read a document

A document's **owner**, or somebody with the right over the audit trail, can see who has
taken a copy of it.

Being allowed to *read* a document does **not** make its readership yours to see — a club
member could otherwise learn which other members had been looking at a particular survey,
which is a fact about them.

Your own reading history is under your account. **Nobody can fetch somebody else's by naming
them.**

---

Workflow: [File the club archive](../workflows/file-the-archive.md) ·
Related: [Uploads](uploads.md) · [Links](links.md) ·
[Permissions](../admin/permissions.md)
