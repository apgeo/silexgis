# Maintenance sweeps

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/maintenance.md)

[← Administration](README.md) · Related: [Uploads](../features/uploads.md) ·
[Documents and cabinets](../features/documents-and-cabinets.md)

---

Some work is done **when a file arrives**: its text is read, an office document is laid out for
reading, a photograph's position is taken off its camera data.

Which leaves a question every growing archive eventually asks: *what about everything that
arrived before that worked?*

Three **backfill sweeps** answer it. Each one walks the installation's existing rows and does
the work that was not done at the time.

---

> **These have no page of their own yet.** They are triggered through the application's API,
> not from a button in the interface. If you are not comfortable doing that, this page is still
> the one to point your administrator at — it says which sweep solves which symptom.

---

## The three sweeps

### Text extraction

Reads the text of stored files that **nothing has read**, or that **a newer reader should read
again**.

**Run it when:**

- documents uploaded before you noticed text search existed are not turning up in searches,
- a batch of files shows *"Nothing here can read this format's text yet"* and the installation
  has since been upgraded,
- you bulk-imported an archive from a server directory and want it searchable.

Afterwards, each document's **Text** state should move to *The text has been read*. Files that
genuinely hold no text — scans, image-only PDFs — will still say so, correctly. **Nothing
recognises text in a photograph**, and a sweep does not change that.

### Document conversion

Makes **a readable copy of every office document that has none yet** — the pages the browser
shows for Word, Excel, PowerPoint and OpenDocument files.

**Run it when:**

- you installed the optional converter service *after* uploading an archive,
- documents show *"This document is being prepared for reading here"* forever, or
  *"This installation cannot lay out office documents"* and that has since been fixed.

This is the single most likely sweep a club needs, because the converter is optional and gets
added later.

### Photo geo-backfill

Puts the **position a camera recorded** onto photographs already stored without one.

**Run it when:**

- geotagged photographs are not appearing on the map's *Geotagged photos* layer,
- you imported a large photo archive before this worked.

It reads what is in the files. It does not invent positions, and it does not write anything
back into your image files.

---

## What they have in common

- **Each takes no input of its own.** The work is a property of the installation's own rows, so
  the only thing that varies is which sweep you run.
- **They are queued as background jobs**, like an import or a raster conversion. They do not
  block anybody.
- **They need the *Run* right on the Jobs domain** — see
  [Permissions](permissions.md#actions).
- **They are safe to run more than once.** A sweep does work that has not been done; it does
  not redo work that has.

## Watching one

A queued job has a status you can read — the requester can see their own, and anybody with
**Read** on the Jobs domain can see any of them.

Members who asked to be told when **their uploads finish processing** will get the
notification as the work lands. See [Notifications](../features/notifications.md).

---

## Not a sweep: things that fix themselves

For contrast, these need no maintenance:

| | |
|---|---|
| **A document's language** | Detected when its text is read, and correctable by hand on the document at any time — which re-indexes it on the spot |
| **Computed cave statistics** | Never stored. Worked out from the survey each time, so uploading a better survey corrects them |
| **First visits and hours underground** | Never stored. Typing up an older trip from the archive *corrects* the figures rather than leaving a stale flag |
| **Camp leads** | A reading of what the trips already say, so recording a lead once records it everywhere |

That is a deliberate pattern: **where a figure can be derived, it is derived.** The sweeps
exist only for work that genuinely has to be done once and stored — reading a file, converting
a file, or lifting a coordinate out of one.

---

Related: [Uploads](../features/uploads.md) ·
[Documents and cabinets](../features/documents-and-cabinets.md) ·
[Photographs](../features/photographs.md)
