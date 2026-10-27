# Reviewing uploads

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/upload-review.md)

[← Feature reference](README.md) · Related:
[Mobile and offline sync](mobile-sync.md) ·
[Uploads](uploads.md) ·
[Location protection](../admin/location-protection.md)

---

An administrator can mark an account **"Uploads need review"**. Everything that account sends in —
from the phone app, or by importing a file — then **waits**. Nothing of it is written to the maps,
the documents or the trips until somebody with the right to review it says yes.

This page is for that somebody. It needs the **Upload review** right: *read* to see the waiting
uploads, *write* to decide them.

---

## The queue

**Uploads awaiting review** in the side menu lists every upload that still has something waiting,
**oldest first**:

| Column | |
|---|---|
| **Sent by** | The account that sent it |
| **Waiting since** | When it arrived, and how long ago |
| **Source** | The phone, or the kind of file import |
| **What it holds** | Counts by kind: places, photos and documents, recordings, scans, raster maps, map pins… |
| **Waiting** | How many lines are still undecided |
| **Held files** | The size of the files kept on the server until the upload is decided |

Pressing a row opens the upload.

---

## Reading an upload

The upload's lines are listed **under the cave they belong to**: a cave, its entrances and places,
the recordings made in it, the photos hung on it. Lines that belong to no cave — the phone's own
users, for one — come last. Each line says what it is, whether it is **new**, a **change** or a
**removal**, and what it needs: *"Needs: Cave X"* means the line cannot be accepted without Cave X.

Press the arrow beside a line to read it.

- **A new place** shows the values it would be created with.
- **A change to a place** shows a small table, field by field: the place **now**, beside what the
  phone **proposes**, with a box for each field. Fields that already match are marked *same*.
- **A photo or a document** shows its title, file name and size. The file itself is kept until the
  line is decided, and its picture appears once it is accepted.
- **A recording** shows its title, log, times and its scans.
- **A raster map's pins** show their place on the map only where the server sent it. A pin on a
  place whose position you may not see is listed without one — see
  [Location protection](../admin/location-protection.md).

A line may also say **"Changed since the phone read it"**: somebody changed that place on the
server after the phone took its copy. See [Changed since](#changed-since) below.

Lines already decided are hidden once something is still waiting; tick **Show lines already
decided** to see them all, with who decided and any note.

---

## Deciding

Everything you decide can carry a **note to the uploader**. Write it in the box above the lines
before pressing the button; it goes with the next decision and the uploader is told.

| To… | Do this |
|---|---|
| Accept or reject **everything** waiting | **Accept all** / **Reject all** (rejecting asks you to confirm) |
| Decide **one line** | **Accept** / **Reject** on the line |
| Accept **part of a change** | Open the line, untick the fields you do not want, press **Accept**. Only a change to a place can be accepted field by field; everything else is accepted or rejected whole |
| Decide **several lines at once** | Tick them — or use **Select** for *all waiting lines* or *all of one kind*, or tick the box in a cave's heading to take every waiting line under it — then **Accept selected** / **Reject selected** |

You cannot decide an upload you sent yourself, whatever your rights: another reviewer does.

### Dependencies

Accepting a line **also accepts the waiting lines it needs** — an entrance needs its cave. Before
anything is written you are shown which lines come with it. Rejecting a line **also rejects the
waiting lines that depend on it**, and says so first.

### Changed since

If a place was changed on the server after the phone read it, accepting the phone's version would
write over that change. The page stops and shows which places it is, and nothing is decided. Then:

- **Write over the current version** writes the fields you accepted onto the place as it is now.
- **Reject these** turns the phone's version of those places down.
- **Cancel** leaves it all waiting.

### Taking a rejection back

A rejected line shows **Accept after all** for a while (30 days by default). It works only if the
file is still kept and the phone has not sent the same place again since; otherwise the page says
it can no longer be changed.

### A line that could not be written

A line you accepted can still end as **Could not be written** — for example because the uploader
lost the right to write there in the meantime. The line says why. The rest of the upload is not
affected.

---

## If the account is yours

When your own account is held for review, **Geodata → Imports** shows **Your uploads under review**
above the list of confirmations. Each row is one upload: when you sent it, from where, what it
holds, how it stands — **Waiting**, **Accepted**, **Partly accepted** or **Rejected** — who decided
it and when, and the **note** the reviewer left.

Press a row to open it. You see the same page the reviewer sees, minus the buttons: what was
proposed, line by line, and what became of each line. You cannot decide your own upload, whatever
your rights. A rejected upload is not written anywhere; send it again, changed, if it was meant to
be.

---

## What happens next

The uploader is told of each decision. On the phone, the rows appear as accepted or rejected with
your note; on the web, the upload is listed on **Geodata → Imports** with the same state.
Accepted lines are written exactly as an upload nobody held would have been, under the uploader's
own rights, with you named as the person who decided; the History of each place says so, and an
accepted upload can be undone like any other.
