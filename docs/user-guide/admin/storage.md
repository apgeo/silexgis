# Storage

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/storage.md)

[← Administration](README.md) · Related: [Point clouds](../features/point-clouds.md)

---

Point clouds and raw scanner recordings can fill a disk in an afternoon. An installation therefore
**caps** what may be stored, **warns** its full administrators as the store fills, and has a page
that says **what fills it and what could go**.

**Administration → Storage**, for **Full Administrators**.

---

## The caps

Three limits are set when the installation is configured (see the install guide):

| Limit | What it bounds |
|---|---|
| **Whole store** | Everything the installation keeps |
| **Point clouds** | Scans and recordings, as a separate total (optional — without it, scans share the whole store's cap) |
| **Per account** | What one person's uploads may take |

Sizes already promised count too: a large upload that has been opened but whose files are still
arriving holds its room, so two clouds started side by side cannot both claim the same space.

**What does not fit is refused before it starts** — an upload when it is opened and as each file
arrives, a processing run before its first step that writes anything. The refusal names the limit,
what was needed and how much is missing. Everyone sees the room left before they start: the
point-cloud upload dialog, the estimate and a request to process a scan again show the whole
store, the point clouds and the person's own account.

The disks themselves refuse only what genuinely cannot be written; a minimum free space is a
warning level, not a refusal.

## Warnings

Once an hour the installation compares what is used with each cap, and with the free space of the
store's disk and the processing worker's. When a level is crossed — a **notice** level and a
**warning** level, both percentages the installation sets — the full administrators are notified
once, under the storage category of their [notifications](../features/notifications.md) (*Storage
is filling up*).
Falling back below a level is recorded without a message, so a later climb is reported again.

## The page

Once a day the installation measures the store and keeps the result. The page shows the latest
measurement — or, on a first visit, offers **Measure now** — with:

- the totals of the store, of the point clouds and of the disks, against their caps;
- how fast the store grew over the last 7 and 30 days, and **the date it would be full** at that
  pace;
- what fills it, **by kind of file, by cave and by account**;
- the **50 largest files**;
- the groups of files that **could be deleted**.

## What can be deleted

Each group has its own **Delete** button, a confirmation saying what will go and how much room it
frees, and a fresh measurement afterwards:

| Group | What it holds |
|---|---|
| **Superseded point-cloud copies** | Thinned copies and tiles a scan no longer uses, because it was tiled again |
| **Work of failed runs** | What a processing run left behind when it failed |
| **Older survey versions' products** | Products made from survey versions that a newer upload has replaced |
| **Expired uploads** | Uploads that were opened and never finished |

**Uploaded originals are in no group and are never offered** — a scanner recording, a cloud, a
survey file stay as they were sent, and the server refuses to delete one through this page even if
asked directly. Every deletion is recorded in the audit trail.

---

Related: [Point clouds](../features/point-clouds.md) · [Maintenance sweeps](maintenance.md)
