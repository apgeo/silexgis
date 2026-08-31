# Uploads

[← Feature reference](README.md) · Workflow:
[File the club archive](../workflows/file-the-archive.md)

---

**Library → Uploads** is for getting files in *in bulk*, and for the record of what arrived
together.

For a handful of files, drag them straight onto the record they belong to, or onto a cabinet.
Come here when there are hundreds.

---

## Before you start

The page tells you, up front:

- **Largest file accepted** — 512 MB by default; it is a setting.
- **Room left for you** — your quota, if the installation sets one.
- **Accepted types**, or *"Any file type is accepted"*, and anything **not accepted**.
- If you are uploading into a cabinet: **what documents filed here are expected to carry**.

## Uploading

**Drop files here, or click to choose them** — or **Choose a folder**.

Options:

| Option | |
|---|---|
| **Name this batch** | Optional. Everything uploaded together is findable again by that name |
| **Tag everything with** | Optional. The tag is created if it does not exist yet |
| **Expand ZIP archives on the server, keeping their folders** | Folders become cabinets |

### Duplicates

If a file's contents are already stored you are asked: **Store anyway** or **Skip it** —
worded differently depending on whether you can see the existing document or only know that
the contents exist.

### The summary

*"n of m stored · k skipped · j failed"*, with a reason per row:

| Reason | |
|---|---|
| Already stored | Identical contents exist |
| Larger than this installation accepts | |
| This file type is not accepted | |
| Not enough room left | Quota |
| The file is empty | |
| This folder path cannot be filed | |
| You may not file into that cabinet | |
| The file could not be read | |
| Upload failed | |

**Retry n failed** puts the failures back in one press.

---

## Importing from a server directory

If your installation is configured for it: **Import from a server directory**.

This copies files the server can already reach, **without sending them over the network** —
the right answer for a hard drive plugged into the server rather than a 40 GB upload over a
domestic connection. Folders become cabinets.

The page lists which directories the installation may import from. If none are configured it
says so, and names the setting an operator would need to set.

The import starts and its **report fills in as it runs**.

---

## The batch register

Every upload batch is listed:

| Column | |
|---|---|
| **From** | Browser · Archive · Server directory |
| **Status** | Open · Running · Finished · Failed |
| **Counts** | *n stored · m skipped · j failed* |
| **Uploaded** | When |
| **Report** | The per-file detail |

This is how you answer "what happened to the box of scans I did in March" six months later.

---

## What happens next

Uploaded documents land in the [library](documents-and-cabinets.md). Their **text is read in
the background**, and you can watch that state on each document's page. Photographs land in
the [gallery](photographs.md).

If you asked to be told, a notification arrives when **your uploads finish processing** — see
[Notifications](notifications.md).

---

Related: [Documents and cabinets](documents-and-cabinets.md) ·
[Photographs](photographs.md)
