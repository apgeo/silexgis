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

## Photographs from underground: placed by the hour, not by GPS

Everything above places a photograph by the position its file carries, and a camera underground
records none. A photograph taken on a **tracked trip** is placed another way: by *when* it was
taken and *whom* it is about, read against the trip's [tracking log](../features/live-tracking.md).

1. Put the photographs on the trip (its **Files** tab), as for any trip.
2. On the trip's **Tracking** tab choose **Add photographs** — or the picture button on a
   report's row, which opens at that report's moment.
3. Tick the photographs. Each is filed at the moment its own file says; one whose file says
   nothing is flagged and offered the moment the dialog was opened at.
4. Say whom they are about, once at the top or per photograph on its own line.
5. **Read the line under each one before attaching**: it names the station the replay will draw
   that photograph at. If the lines say *No station had been reported for them by then*, or name
   a station the picture plainly was not taken at, the camera's clock was out — correct it once
   under **The camera's clock was ahead by** and every line follows.

A photograph about nobody in particular is kept on the timeline at its moment and drawn under no
station: a party that has split is in two places. Nothing here writes a place onto the
photograph — correct a report afterwards and the photograph moves with it. And nothing here
tells you a station you could not already see: where the cave's location is withheld from you,
the line says so. The lines are explained one by one under
[The party on the survey](../features/live-tracking.md#the-party-on-the-survey).

---

Next: [Plan and log a trip](plan-and-log-a-trip.md) ·
Reference: [Photographs](../features/photographs.md)
