# Camps (expeditions)

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/camps.md)

[← Feature reference](README.md) · Related: [Trips](trips.md)

---

**Activity → Camps.** A camp is a multi-day, multi-trip effort: a summer expedition, a
fortnight in a remote system, a training camp.

A camp gathers trips rather than replacing them. Everything a trip records stays on the trip;
the camp is what reads across them.

---

## The camp record

| | |
|---|---|
| **Name** · **Days** · **State** | |
| **Organising club** | |
| **Working area** | A shape somebody drew on the map |

A camp goes through the same states a trip does.

## Creating a camp

**Activity → Camps → New camp**, if you may create one. The form asks for the name, the days,
who may read it (the caving group unless you say otherwise), the organising club, a description
and the **working area** — a rough shape drawn on a small map. The working area carries no
location protection: *"Shown exactly to everybody who may read the camp"*, so draw it roughly,
never tightly around an entrance that must stay protected.

A camp entered without touching the dates is a one-day camp today. Saving opens the camp.

A new camp is a **draft**, and the page says so to anybody who could announce it: *"This is a
draft. It has not been announced yet — announce it when the plan is settled, or record it as
done once it has happened."*

## Editing, the lifecycle, and deleting

**Edit camp** opens the same form, filled in. If somebody else saved the camp while you had
it open, your save is refused and you are asked to reload.

The lifecycle buttons are the trip's — *Publish · Float it · Start organising · It is going
ahead · Put it back · Settle a new date · Mark as done · Call it off · Back to draft* — and
which of them are offered depends on the state the camp is in. Two are confirmed first:

- **Publish** — *"Announce this camp? Nobody is notified by this; it marks the camp as
  announced. Who may read it does not change."* Unlike a trip, announcing a camp tells nobody.
- **Call it off** — the camp is kept, and can be reinstated as a draft.

**Delete** asks *"Delete this camp? The trips it gathered stay; only the camp and its own rules
go."* — a camp gathers trips rather than owning them.

One document goes with the camp: a write-up filed by **Save to the camp** (see *The write-up*,
below). It was made from the camp and says nothing once the camp is gone, so it is deleted with
it rather than left in the library. Everything else on the camp's *Files* tab stays in the
library and loses only its place on the camp — pictures, a report somebody wrote and uploaded,
and a saved write-up that has since been filed in a cabinet or attached to something else.
Write-ups saved to the camp's trips belong to those trips and stay with them.

## Tabs

**Trips · Map · Leads · Who was there · Photographs · Files · History**

### Trips

The trips belonging to this camp — *"No trips in this camp that you may read"* if none, and
*"Showing n of m trips"* if the list is bounded.

### Map

The camp's working area plus the trips you may read, drawn together. If a camp has neither, it
says so rather than showing an empty map.

The caveat it prints: *"Drawn over the trips you may read. The working area is the shape
somebody drew on the map."*

### Leads

The board of what this camp's trips left open, grouped by whether each way on is still going.

**It is a reading of what the trips already say, not a register of its own.** A lead is a
place of the *continuation* kind — the sort an exploring club keeps its question marks as —
named by one of the camp's member trips. Nobody writes anything on the board, and there is
nothing on it that can fall out of step with the places themselves.

| | |
|---|---|
| **Grade** | How promising it looked, on the register's own scale, or absent because nobody graded it |
| **State** | Open · Checked · Dead end · Continues — or *not recorded* |
| **Note** | What is left to do there, as whoever found it wrote it down |

Four properties worth knowing:

**Whether a way on is still going is a fact about the place, not about the trip.** So the board
reports **what is true now**, rather than what some trip saw on some afternoon.

**It reads over every trip role at once.** A club that recorded a continuation under *Visited*
rather than under *Leads left* has still recorded it — and a board that asked about one role
would drop it without saying so, which is exactly the failure nobody would notice, because the
missing lead is missing from the only list that would show it.

**A place two of the camp's trips both named is one lead, not two.**

**A lead you may not place exactly is left off the board entirely** — not shown with its
position blurred.

> That is a deliberate and slightly unusual choice, so it is worth stating why. A board is a
> bulk path, and *a list of undefended ways into caves, sorted by how promising they look*, is
> the same species of disclosure as a single coordinate — arguably a worse one, since it is
> ordered by how much somebody would want it. **The position is not what is being withheld; the
> place is.** No coordinate is served on this board at all.

Consequently: *"Read as visible to you: leads found on trips you may read, and only the ones
you may see."* Two people opening the same camp see different boards and **both are right** —
the page says so in words rather than leaving the numbers to imply otherwise.

If there is nothing: *"This camp's trips left nothing open — or nothing you may be shown."*

The board is read whole — it is not paged and takes no filter — and it is capped. Over the cap
it shows **the same board every time it is opened** rather than an arbitrary subset that moves
under you, and it says it was capped.

### Who was there

The camp roster: who was at the camp, as what, and for which days. It is kept by hand rather
than worked out from the trips, because the people it exists for — the cook, the driver,
whoever kept the base camp — went on none of them.

People are **counted once each**, however many roles or stays somebody is recorded in.

Roles: Member · Organiser · Cook · Base camp · Driver · Medic · Equipment · Guest.

**Keeping it.** If you may edit the camp, **Add a stay** records one, and each row has *Edit
stay* and *Remove stay*. A stay is **one person in one role, for one stretch of days**:

| | |
|---|---|
| **Who** | Start typing and pick somebody from the list of cavers — or type the name of somebody who is not on it |
| **Role** | *Member* unless you say otherwise |
| **Days** | The first and the last day they were there; for a single day, pick it twice. Starts out as the whole camp |
| **Still there** | Tick it for somebody who has not left yet: the stay then has a first day and no last one, and the roster shows it as *"from 14/07/2026, still there"*. Untick it and give the last day once they have gone |
| **Note** | *Arrived late, left early, came back…* |

Somebody who cooked and also surveyed is **two stays**, and so is somebody who left and came
back. Nothing stops stays overlapping.

A stay with no last day always means **still there** — it is never how one day is recorded.
The roster and the camp's write-up both say it in words, print one date for a single day, and
print both dates for a longer stay.

**Naming somebody new.** A name you type instead of picking is *"saved as a name: it means
whoever the list of cavers already holds under exactly this name, and adds them if nobody
is."* So the camp's cook can be written straight onto the roster, and is under **People →
Cavers** afterwards — once, however many stays name them. It is the same rule a trip names
its people by. Two things follow from *exactly*:

- **Pick from the list when the person is on it.** A name typed slightly differently — a
  missing accent, a nickname — is a different name, and makes a second entry for the same
  person. (Two entries for one person are joined afterwards with **Merge**, on the cavers
  page.)
- **If two people are recorded under one name, the list shows both**, each with their clubs.
  Typing that name instead of picking one means the older entry.

If the person you picked is merged into another entry, or removed, before you save, the stay
is not saved: *"That person is no longer in the list of cavers. Choose them again, or type
their name."* The dialog stays open with everything you entered, and the name in the box is
from then on an ordinary typed name — pick the person again from the list, which has been
read again, or save it as it stands.

*Remove stay* asks first, naming whose stay it is, and takes away only the record that they
were at the camp for those days — *"the person stays in the list of cavers."*

If you may read the camp but not the people in this installation, the roster is withheld
whole — names, count and all — rather than shown partially, and nothing is offered for
keeping it.

The roles are a vocabulary of the camp's own, separate from the trip roles; an administrator
edits it under **Configuration → Camp roles** (see [Vocabularies](../admin/vocabularies.md)).

### Photographs

The pictures filed against the camp and the ones on the trips it gathers that you may read,
drawn as a grid with the gallery's own viewer. *Open in the gallery* narrows the gallery the
same way.

What is shown is one reader's answer — *"The photographs you may see, from the camp and the
trips in it you may read. Somebody else may see more, or fewer."* A trip you may not open
contributes no picture, and a camp you may not open shows none. If there is nothing: *"No
photographs are filed against this camp or the trips in it that you may read."*

### Albums

**Albums about this camp** — on the *Files* tab; see [Photographs](photographs.md#albums).

---

## The write-up

**Write-up**, in the camp's header, opens the camp arranged to be read: *About the camp*, *The
trips*, *What each trip wrote*, *Who was there* and the pictures, built from what **you** may
see — the page says so: *"This write-up shows what you may see. Somebody else reading the same
camp may see more, or less: the trips in it, the people, the pictures."*

**What each trip wrote** is each trip's own text under the trip's date and title: its account,
its results and the answers on its form. You are shown, for each trip, exactly what that trip's
own write-up would show you — so the account of what went wrong on a trip appears only if you
may edit that trip, and a trip you may not read is not there at all. A trip that wrote nothing
has no heading. In the Word document the same part is printed by the layout line `accounts`; a
club that does not want it takes that line out of its camp layout.

- **Download document** saves it as a Word document, in the standard layout or in one your club
  keeps under **Configuration → Report layouts**.
- **Download PDF** gives the same document as a PDF. It is there only where your installation
  runs its document converter; where it is not, **Print** and *Save as PDF* in the browser is
  the way. Nothing is kept of the PDF on the server, and the copy saved to the camp is always
  the Word document.
- **Save to the camp** files the document on the camp's *Files* tab. The copy saved there can
  be opened by everybody who may read the camp, so it is built from what any account may see of
  its trips — including what each trip wrote: a trip's text is in the saved copy only as any
  account may read it, never as you do. Your own fuller copy is the download.
- **Print** uses the browser, with the application's chrome left off the paper.

## Permissions and sharing

Two buttons in the header, both for somebody who may manage the camp's permissions:

**Permissions** opens the camp's own access rules — who may read, write or delete this camp,
person by person or group by group. See [Permissions](../admin/permissions.md).

**Share** reaches further: *"Sharing a camp writes one rule onto each trip it gathers, marked
as the camp's, so whoever it is shared with can read what the camp gathered."* Each trip
answers for itself: the camp is shared on every trip whose permissions you may manage, and
the others are **skipped and listed**, so a camp that gathers another club's trips is shared
for your own at once.

After you apply a sharing the dialog says what it did — *"Trips shared: 2. Trips skipped: 3."* —
and lists the skipped trips:

- a trip you may read is **named**, with what stands in the way: *"its owner must let you
  manage its permissions"*, or *"you asked to pass on more than you hold on this trip
  yourself"* (you can never give somebody more on a trip than you have there);
- a trip you may **not** read is not named — a camp can gather a trip its organiser cannot
  open — and is only counted: *"Skipped trips that you may not read, and that are therefore
  not named here: 1."*

Nothing is written onto a skipped trip. Once its owner has let you manage its permissions,
**Apply again** picks it up. If no trip at all could be shared, nothing is shared and you are
told so.

The dialog also shows how many of the camp's trips each sharing covers. Fewer than all means
trips joined after it was last applied, or were skipped: apply it again to cover the ones you
may, or withdraw it to take back from every trip what the camp wrote — and only that.

---

## Permissions written by a camp

A camp can write access rules onto the trips that belong to it. On a trip's *Permissions* tab
these show as **From a camp**, and are changed at the camp rather than on the trip.

They are shown there anyway, so the whole list of who may read a trip is in one place. See
[Permissions](../admin/permissions.md).

---

## If a camp is not there

*"Either it is not there, or it is not yours to read. This page cannot tell the two apart"* —
which is deliberate. A page that answered differently for a camp that exists would disclose
its existence.

---

Related: [Trips](trips.md) · [Events and calendar](events-and-calendar.md)
