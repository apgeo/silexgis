# Live tracking and published trips

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/live-tracking.md)

[← Feature reference](README.md) · Related: [Trips](trips.md) ·
[Checklists and the callout](checklists-and-callout.md) ·
[Sharing, QR codes and public pages](sharing-and-public-pages.md)

---

While a party is underground, somebody on the surface usually takes the calls: *"Ana and Radu
are at the pitch head"*, *"everybody is out"*. A trip's **Tracking** tab is where that goes.
Each call is recorded as a **report**, and every person is drawn on the cave's survey where they
were last reported. A trip can then be **published**, so that people with no account — the
families waiting, the club's own website — can follow the party too.

> **Tracking records; it does not raise an alarm.** The tab says so itself: *"Tracking records
> reports; it does not raise an alarm."* It watches no clock. If somebody should be told when a
> party is overdue, that is the [callout](checklists-and-callout.md), which is a separate
> arrangement on the same trip.

---

## Starting and closing tracking

On the trip's page, open **Tracking** and fill in **Tracking setup**:

| Field | What it decides |
|---|---|
| **Survey** | The survey the party's positions are placed in — one of the cave's uploaded surveys that holds stations |
| **Depth is measured from** | The station a reported depth counts down from. Left empty, the survey's highest entrance station is used |
| **Only these parts of the survey** | Where a reported depth may land — the start of a station or survey name. Left empty, the whole survey is searched |

Then **Start tracking**. The trip now shows *Tracking*, and reports can be recorded.

- **Choosing the survey or the reference station needs the right to see the cave's exact
  location**, because a station name is a position. Closing, restarting and editing the filter
  do not.
- **A running watch can move to another survey of the same cave** — a corrected survey arriving
  mid-trip, say — and never to another cave's: *"close it first"*. Changing the survey clears
  the depth datum and the filter, because station names mean nothing, or something else, in
  another survey; the page warns before saving.
- **Close tracking** when everybody is out. The confirmation says what follows: the party stops
  being followed; a published page says the trip is over, and stops showing it once the grace
  period after closing has run out. **The log itself stays open** — reports can still be
  recorded, corrected or removed afterwards, so the write-up does not have to be finished
  first. **Start tracking again** reopens the watch.
- **A trip where tracking was never started takes no reports** — a report names a place on a
  survey, and there is no survey yet. The tab says so and points at **Start tracking**.

**Teams** are optional named groups ("Survey team", "Rigging"). A report can carry one; deleting
a team keeps its reports and drops only the label.

---

## Recording a report

The **Record a report** card:

1. **Tick the people** the report is about — one report is recorded for all of them at once.
2. Choose **What is being reported**:

   | Kind | Carries |
   |---|---|
   | **Went in** | No place |
   | **At a station** | A station of the survey, spelled the way the survey spells it |
   | **At a depth** | A depth below the datum — or a **Place** the cave has declared (below) |
   | **Note** | Words only |
   | **Came out** | No place |

3. Optionally a **Team** and a **Note**.
4. **When it was said** — left empty, the report is stamped now. Fill it in for word relayed
   out some time after it was said. A report can never be about the future.
5. **Record for N selected.** **Mark N out** is the shortcut for the call everybody waits for.

**A depth is a number, not yet a place.** *"The sign makes no difference: −120 and 120 are both
120 metres down"*, and a depth is kept to one decimal. Under the field the card answers **Which
station is that?** — the station the cave has declared for that depth first, if it declared
one, then the nearest stations. If the depth is far from the station it will land on, the card
warns before you record, and says which of two things happened: the depth is simply that far
from the nearest station, or **the cave declares that depth as that station** and the survey
puts it some metres away — in which case check the number or the cave's declaration.

**From the drawing.** **Show the model** opens the survey with everybody on it; press a station
and choose **Record here** to report at exactly that station.

## The party on the survey

The model panel marks each person where they were last reported, with **Underground**, **Out**
or **Not heard from** — three states, never two: a party nobody has heard from yet is not a
party already out.

| You may see | Meaning |
|---|---|
| **On another survey** | The place was reported against a different survey than the one the watch uses now |
| **Not on the drawing** | The survey's drawing holds no station of that name |
| **Not shown to you** | The position exists and you may not be told it: the cave's exact location is withheld from you |

**Replay the trip** plays the whole log back on the survey, with the notes said along the way;
**Back to live** returns. Photographs can be hung on **a moment** of the trip (*Photographs of
this moment*) rather than on a report, so correcting or deleting a report never loses them.

---

## Correcting and deleting reports

Each row of **Reports** offers two controls, and they mean different things:

- **Correct** — for a report written down wrongly. It changes the moment, the place, the team
  or the note, and **the report keeps its place on the log and everything pinned to it**. The
  person cannot be changed: a report about somebody else is a different report, so delete this
  one and record that one. The dialog says plainly that this changes what the log says
  happened — the replay and the published page follow it.
- **Delete this report** — only for a report that should not be there at all. It leaves the log
  for good.

Both work on a **closed** watch as well as a running one. On a trip where tracking was never
started, neither does: positions put onto such a trip by importing a SpeleoLoc recording are
taken back by undoing that import.

---

## What a cave's depths mean

A depth on its own is not a place: where several stations sit near each other, arithmetic
cannot tell which one people mean by "96 metres". The club can. On the **cave's** page, the
section **What this cave's depths mean** → **Declare a depth**:

| Field | |
|---|---|
| **Depth** | The depth being declared. Kept to one decimal; the sign is ignored |
| **Station** | Which station that depth is, spelled the way the survey spells it |
| **Place name** | Optional — the name people use for it ("Meander") |

- **One declaration per depth.** Declaring a depth again replaces what it said. Editing a
  declaration to a different depth writes the new one and then withdraws the old.
- **Every report of that depth lands on the declared station** — typed, corrected or imported —
  as long as the watch's survey still has that station. One that names a station the survey no
  longer has (a survey re-uploaded since) is passed over, the depth is placed by measurement,
  and the tab says which of the two happened.
- **A place name becomes something to report by.** The report card offers the declared places
  by name, shallowest first; choosing one reports its depth, which lands on the declared station.
- **Withdraw** removes a declaration. Reports already recorded keep the station they were given.

Declaring needs the right to write to the cave. A declared station is a position, so the list
carries the cave's own protection.

---

## Importing a spreadsheet of reports

What a coordinator often actually keeps is a sheet: a line per phone call, with a time, who it
was about, how deep they were and a note. **Import a sheet**, beside the reports:

1. **Download a sample sheet** — a template this installation reads without any settings.
2. **Choose a CSV file, or drop one here.** The characters and the column separator are worked
   out from the file and shown beside its name; **File settings** overrules either, and sets
   **Day and month** where the file's own dates cannot settle which number is the day.
3. **Read it.** Reading writes nothing: what comes back is exactly what importing would do,
   row by row — **New**, **Replaces**, or refused — with **Findings** beside each row. Above the
   rows the dialog states how the times and dates were read.
4. **Column settings** — point a field at a header only where the detection got it wrong. The
   **Words for going in** and **Words for coming out** replace the usual lists for that side.
5. **Untick** any row you do not want (a line naming several people is taken or left out
   whole), tick **Overwrite what the log already holds at these moments** if you mean to, and
   **Import**.

| Rule | Why |
|---|---|
| **A report is filed under the person and the moment** | A corrected sheet imported again corrects the rows it corrected instead of doubling them |
| **A time written without a zone is read as UTC, exactly as written** | A sheet's 14:30 becomes 14:30 UTC, shown in your own zone. Where the hour has to sit beside reports typed live, write the offset in the cell: `2026-09-12T14:30+03:00` |
| **A date with no time is refused** | Filed at midnight, a whole day's reports would collapse onto one moment |
| **The date and the time in two separate columns is refused for the whole file** | The moment is read from one column carrying both — join them |
| **Names are matched against this trip's roster only** | Full name, then given name and initial ("Ion P."), then given name. A name matching nobody, or two people, is refused rather than guessed |
| **A row's place: station, then declared place, then depth** | Going in and coming out claim no station |
| **Two rows for the same person at the same moment are one report** | The last one wins, and both lines say so |
| **A note longer than a report may carry is refused** | 2000 characters, note and details together — that row, not the sheet |
| **A moment the log already holds twice for that person is refused** | Which of the two the row would correct is not the importer's to guess |

A sheet can be imported onto a **closed** watch — that is usually when it is typed up. A trip
with no watch at all has nothing to import onto: choose a survey and save the setup first.

---

## Publishing a trip

**Publish this trip** → **Create a follow link**, on a trip whose tracking is running.

- **Copy it now — this is the only time it is shown.** Only a fingerprint of the link is kept,
  so it cannot be shown again; a lost link is taken back and a new one made.
- The same panel gives **For a website**: a block to paste into your club's site (below).
- **Publishing needs the right to share the cave**, because the page hands over the cave's
  survey drawing.
- **A cave whose coordinates are protected cannot be published at all.** Not blurred, not
  partial — refused, and decided again every time the page is read, so protecting a cave later
  closes links already handed out.
- **Everybody on the trip who has an account is told** that it has been published.

**Who the page names.** By default this installation publishes **real names**; an operator can
switch that off for everybody, after which people appear as *Caver 1*, *Caver 2*. The
**On the public page** column shows, for each person, what the page will call them. A
**caption** outranks that setting in both directions — *"third of the party"* keeps one person
off a page that would otherwise name them, without turning names off for the club.

**A link ends on its own.** It works while the watch is running, then for a grace period after
closing (two days by default) so the people following can read that everybody is out, and in
any case only until the date the panel shows (by default two weeks after the trip's last day).
**Take it back** ends it at once. Each link is listed as **Live** or **Not open**.

---

## What a follower sees

The link opens a page with **no link anywhere on it** and nothing else of the installation: the
trip's name, each person with where they were last reported and when they were last heard of,
and the survey with everybody drawn on it. It refreshes itself every minute while the party is
underground, says *"This trip is over"* once the watch is closed, and grows a tab per scanned
map where the survey has them.

**Past trips in this cave** opens the cave's list:

- **Being followed now** — every party of the same cave being followed at this moment, the
  link's own trip marked *This link's trip*. A party whose watch has just been closed says
  *Just finished*, never *Underground now*. These rows are for reading; the page keeps drawing
  its own party.
- **The cave's finished published trips** — **Play** replays one on the survey its reports were
  measured in, under a banner that says plainly *"You are looking at a past trip"*. The view can
  keep up with one team or person, and **Back to the party now** returns. The address follows
  what is on screen (`?past=…&team=…`), so a reader can send somebody that exact view.

Two consequences worth knowing before you publish:

- **Publishing one trip also puts it among the cave's past trips** for anyone holding a link to
  any published trip of that cave — including links made later, for other trips. That list
  lasts after the live page has ended, for as long as the installation keeps past trips
  readable (by default, indefinitely).
- **An old link keeps showing who is in the cave now**, through *Being followed now*, for as
  long as its own trip stays readable as a past trip. An operator who does not want that turns
  past trips off or sets how long they are kept; see the [install
  guide](../../INSTALL.md#configuration-reference).

---

## Putting it on your website

The **For a website** block goes into a **Custom HTML** block — not a paragraph, which prints
the code instead of running it. It sizes itself from the column's width; do not give it a
fixed height. Your installation's administrator must also allow your site to frame it
(`SILEXGIS_FRAME_ANCESTORS`, see [Letting a website embed a live
trip](../../INSTALL.md#letting-a-website-embed-a-live-trip)); until then the frame stays blank.

**Links in your own prose can drive the viewer** — one attribute on an ordinary link:

| Attribute | Moves the view to |
|---|---|
| `data-silexgis-station="<station>"` | A station of the survey |
| `data-silexgis-survey="<survey>"` | A whole branch, framed |
| `data-silexgis-caver="<place>"` | Where that member of the party is (1, 2, 3…) |
| `data-silexgis-trip="<trip id>"` | A past trip of the same cave, played back |
| `data-silexgis-trip="live"` | Back to the trip the block publishes |
| `data-silexgis-team="<team id>"` | Keeps the view on that team as a replay runs |
| `data-silexgis-moment="<instant>"` | Winds a replay's clock |

The ids come from the full page's address as you pick a trip and a team there. With two viewers
in one article, every link names the one it drives with `data-silexgis-target` (the id is in the
comment at the top of each block).

**The viewer also tells your page what it shows**, as browser events on `document`:
`silexgis:ready` (the party, and whether it is a replay — a `past` member means it is) and
`silexgis:focused` (whether a pressed link found its place). A script writing words from the
party must check `past` before saying where anybody is. A block pasted before
**29 September 2026** greets the viewer too early and may never pass these on; paste a fresh
one, replacing every older block in the article.

---

## When something is refused

| You see | Why |
|---|---|
| *"Tracking has never been started for this trip, so its log cannot be written to."* | Start tracking first; a closed watch is fine |
| *"That survey belongs to a different cave…"* | A running watch stays in its cave — close it first |
| *"No station matches that depth under this trip's filter…"* | Widen **Only these parts of the survey**, or report a station |
| *"A report cannot be about the future."* | Check the time on it |
| *"This trip has no watch to import reports onto."* | Choose a survey and save the tracking setup first |
| *"Publishing this trip hands over its cave's survey drawing, and that takes the right to share the cave."* | Ask whoever looks after the cave |
| *"This trip's cave has protected coordinates, so the trip cannot be published at all."* | Deliberate — the drawing is the cave's position |
| *"Nothing to show for this link"* (on the public page) | Taken back, ended, or the cave has been protected since |
| *"Past trips are not offered through this link"* | The installation has switched past trips off |

---

Related: [Trips](trips.md) · [Checklists and the callout](checklists-and-callout.md) ·
[Sharing, QR codes and public pages](sharing-and-public-pages.md) ·
[Location protection](../admin/location-protection.md)
