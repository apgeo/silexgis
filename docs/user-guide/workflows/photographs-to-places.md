# Workflow: turn a trip's photographs into places

🇬🇧 **English** · 🇷🇴 [Română](../ro/workflows/photographs-to-places.md)

[← Workflows](README.md) · Reference: [Photographs](../features/photographs.md)

---

You came back from a weekend with four hundred pictures and no waypoints. Modern phones and
many cameras write a position into every shot. This workflow turns that into records on the
map — without making you reject forty points one at a time.

**Nothing is ever written back into your image files.** Every position, name and decision
lives in the review and then in the records it creates.

---

## Step 1 — Open the review

Two ways in:

- **Geodata → From photographs**, or
- from a trip's page: **Derive features from this trip's photographs** — which files
  everything under that trip.

Then drop the pictures in.

## Step 2 — Understand what it did before you touch anything

The review groups pictures into **places**, not points. Twelve photographs of one entrance
arrive as *one candidate with a gallery*. A shaft shot from the rim and from the bottom is
a judgement call, which is why the grouping distance is a setting.

Each candidate row shows: **Pictures · Name · Becomes · Type · Position · Already there ·
Decision**.

The **Position** column is the one to read carefully, because it says *how* the place was
placed and *what that is worth*:

| Source | Means |
|---|---|
| **From the camera** | The fix the camera itself recorded when the picture was taken |
| **From a track** | Worked out by matching the capture time against a recorded track |
| **Placed by hand** | Somebody dragged it onto the map |
| **Not placed** | Neither the camera nor a track placed it |

Camera fixes also carry a quality — *excellent / good / moderate / unknown* — as the camera
reported it.

If a picture recorded **which way the camera was facing**, that bearing is drawn on the map.
That is often what turns "somewhere on this slope" into a hole you can walk back to.

## Step 3 — Rescue the pictures with no position

The review tells you how many there are. Two ways to fix them:

**Match against a track.** Choose a GPX track you walked. Then set:

- **Camera clock offset** — added to each picture's stated time before comparing. Camera
  clocks drift, and one left on the wrong time zone is out by hours while looking perfectly
  plausible. This field is the difference between a placement and a fiction.
- **Match within** *n* seconds — how far from a recorded position a picture may still be
  placed. Beyond it, the track genuinely says nothing about where the picture was taken.

Each matched row then shows how many seconds it sits from a real fix. A few seconds is a
placement; two minutes is a guess.

**Or place it by hand.** Select the row, **Place on the map**, click. It is written to the
review, never back into the file.

## Step 4 — Set the options

**Places and grouping**
- *Each place becomes* — the default kind for a new candidate.
- *Name prefix*.
- *Two pictures are the same place within* — the grouping distance.

**What gets created**
- *Look for something already there within n metres* — as with GPS import, only objects
  whose exact position you may see are measured against.
- *Capture altitude* — left out unless you ask for it. A phone's GPS altitude is the least
  reliable of the three numbers it reports.

## Step 5 — Decide each place

Each candidate offers what is already in the registry nearby. Filing a picture on the cave
it belongs to is one press — you do not have to create a duplicate record just to have
somewhere to put the photograph.

Or **create a new one**, choose its type, name it.

## Step 6 — Confirm, and undo if wrong

**Create *n* selected.** Nothing exists until then.

And as with GPS import, the whole confirmation appears under **Geodata → Imports** and comes
back out again in one press — including the pictures it hung on caves that were already
there.

---

## What happens to the photographs themselves

They land in the [gallery](../features/photographs.md). From there you can:

- give them **captions**, a **photographer** (from the roster, or a name typed in) and a
  **licence** (CC0, the CC BY family, or all rights reserved),
- put them in **albums**, arrange the order, choose a cover,
- **rotate** them, in bulk,
- **star one as the headline picture** of a cave or a trip,
- **publish** individual photographs to the installation's public gallery, which is a
  separate decision from who may read them.

> **Downloads of originals are governed.** If a photograph's own capture point would place a
> cave whose exact position you may not see, you are told you may not download the original.
> The rendering is still shown. This is [location protection](../admin/location-protection.md)
> doing its job.

---

Next: [Plan and log a trip](plan-and-log-a-trip.md) ·
Reference: [Photographs](../features/photographs.md)
