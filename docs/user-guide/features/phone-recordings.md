# Phone recordings and trips that need a look

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/phone-recordings.md)

[← Feature reference](README.md) · Related:
[Mobile and offline sync](mobile-sync.md) ·
[People, cavers and clubs](people-and-clubs.md#phone-users) ·
[Trips](trips.md) ·
[Phone sync settings](../admin/messaging.md)

---

The server side of this is built; the SpeleoLoc phone application that sends recordings is a
separate project; a version that syncs with SilexGIS is in development and not yet published for
general use (see [Mobile and offline sync](mobile-sync.md)). Until it is, recordings arrive here
only from the builds being tested.

A phone running SpeleoLoc can **record a trip**: which cave, when it started and ended, and a scan
at each marked place it passed. That recording is not yet the club's trip report. This page is about
the two places where you meet it: the **cave page**, which lists the recordings made in that cave,
and the **trip page**, which tells you when a report made from a recording needs somebody to look at
it.

---

## Recordings on the cave page

Open a cave. If phones have recorded trips in it, a **Phone recordings** card sits below the cave's
trips. Each row shows the recording's name, when it started, how many scans it holds and where it
stands:

| State | What it means |
|---|---|
| **Waiting** | No trip report has been made from it yet. A recording still **open on the phone** waits for the phone to close it. One whose automatic conversion was switched off (see below) waits for you. |
| **Made into a trip** | A trip report exists. *Open the trip* takes you to it. |
| **Needs attention** | A trip report exists, but the conversion could not settle everything in it. *Open the trip* shows what. |

You see a recording only if you may read it, and only if you may be told about the cave and where it
is. If the cave is one whose position is protected from you, the card is not drawn at all.

### How a recording becomes a trip

When a recording has been closed on the phone and sent, the installation makes a trip report from it
by itself **when nothing is in doubt**: every scan's place gives exactly one survey station, and
every person who scanned is linked to a caver (see
[Phone users](people-and-clubs.md#phone-users)).

When something is in doubt there are two outcomes, chosen in the administrator's
[phone sync settings](../admin/messaging.md#phone-sync):

- **Convert trips with problems on** (the default): the trip is made from what could be settled and is
  **flagged**, with the reasons written on it. See *Trips that need a look* below.
- **Off**: no trip is made; the recording **waits** on the cave page for a person.

### Convert to a trip, by hand

On a waiting recording that is yours to change, **Convert to a trip** opens the same review screen as
importing a SpeleoLoc export, but reading this recording instead of a file. The recording is named at
the top; there is nothing to choose from an archive.

1. Say where the positions go: **an existing trip**, or **a new one**.
2. Choose the **survey** (through its cave) the positions are stations of.
3. Settle each scan: the station is proposed from the depth of the place that was scanned, and you
   confirm or choose another. A scan nobody is linked to needs its phone user mapped in the
   right-hand panel.
4. **Record** the scans you want.

Nothing is written until the last button, and a recording becomes one report only: once it has one,
the button is gone. If your account's uploads are held for review, the conversion waits for a
reviewer instead. The review is **not saved between visits** on this route: leave the page and you
start again.

---

## Trips that need a look

A trip made from a recording that had something in doubt shows a notice at the top of its page. It
says, in plain words and with a count each, what could not be settled:

- scans at **no marked place**, or at one this installation no longer holds;
- scans whose place does **not give a single survey station**;
- scans by a **phone user who is not linked to a caver** yet;
- scans **dated in the future**, which no position can be;
- changes made to the recording **on a phone after** the trip was made from it. The trip is not
  rewritten; it is flagged instead.

Everyone who can read the trip sees the notice. The reasons name how many scans, never a station or a
place.

Fix what you can with the tools that already exist — link the phone user, correct the survey — then
press **Mark as resolved**. That button is for people who may change the trip. If somebody else
changed the flag while you were reading it, you are asked to read it again first.

After it is resolved the notice stays on the page as a quiet note with the date, and **the reasons stay
visible**, so a trip that once needed looking at still reads as one. If the phone changes the
recording again, the same notice is raised again.

---

Related: [Mobile and offline sync](mobile-sync.md) ·
[People, cavers and clubs](people-and-clubs.md) · [Trips](trips.md) ·
[Notifications](notifications.md)
