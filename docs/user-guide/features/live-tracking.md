# Live tracking and published trips

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/live-tracking.md)

[← Feature reference](README.md) · Related: [Trips](trips.md) · [Camps](camps.md) ·
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
| **Where the party said it was going** | The parts of the cave the party plans to visit — the start of a station or survey name, as many as needed. A reported depth is looked for only there, and a station reported anywhere else is marked *Outside the declared parts*. Left empty, the whole survey is searched and nothing is marked |

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
  first. **Start tracking again** reopens the watch. A watch that was started again shows both
  moments on the setup card — **First started** and **Started again** — so the hour the party
  was first followed from is not lost to the restart.
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
   out some time after it was said. A report can never be about the future. Once tracking is
   closed the field has to be filled in (*Writing a report up after the trip*, below).
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

A station the survey file gives no name can be pressed too, on a Therion survey: the drawing
labels it with the number the file wrote it at, in square brackets (`[42]`), and the report is
recorded under that label. If the card answers that the survey has to be read again, the survey
was read before such stations could be reported at — press **Read again** on its row under
**3D survey models** on the cave's page, wait for it to finish, and report again. A Survex survey's
nameless stations are not on the drawing at all, and a reported depth never lands on one.

**Writing a report up after the trip.** Once tracking is closed the card says so — *"This
trip's tracking is closed — a report added now is being written up afterwards"* — and **When it
was said** is no longer optional. The quick answers (*Now*, *15 min ago*…) are not offered, and
a report sent with the field empty is held back with *"Say when this was said."* A call
forgotten on Saturday and typed in on Monday would otherwise be filed on Monday: the replay
would run two days past the trip, and a *Went in* typed that way would read as somebody
underground on a trip that was over. **Mark N out** asks for the moment as well, and so does
**Record here** on the drawing. Nothing else about a closed log changes — it takes reports as
before — and for a whole trip's worth of them a spreadsheet (below) is the quicker way.

**Who is in the table.** Everybody on the trip's roster, in the order the trip lists them — the
same order a published page numbers the party in — followed by anybody the log has reports about
who is no longer on the roster. Such a row is marked *No longer on the roster*: it still shows
where that person was last reported, its checkbox is off and **Select everybody** leaves it out,
because no further report can be recorded for somebody the trip does not name. A published page
does not show them.

**Taking somebody off a trip that is being followed.** Saving the trip sends its whole roster,
so removing a row removes the person. While tracking is running, that is refused for anybody the
log has reports about — *"Somebody taken off the roster has reports on this trip's tracking, and
it is still running."* Close the tracking first, or keep them on the trip. Somebody nobody has
reported on can be taken off at any time, and giving a person a different job on the trip is
never refused.

**No word for hours.** While tracking is running, somebody who is underground and about whom
nothing at all has been recorded for three hours gets an amber tag beside their name — *No word
for over 3 h* — and the line under the three counts says how many such people there are. Any
report ends it, a note included: "voice contact, all well" names no place and is exactly what
ends a silence. Somebody who is out is never marked, nor is somebody nobody has heard from at
all (that is its own count), and nothing is marked once tracking is closed. Press the **Last
heard** heading to put the longest silence at the top; on a phone, where the table has no
headings, tick **Longest silence first** above it instead. The three hours are the installation's
setting; an administrator can change the figure or switch the mark off.

**Planned out by.** When the trip has an expected return, the strip shows it — *Planned out by
17:00*. Once that hour has passed **and somebody is still underground** it turns amber and says
by how much: *2 h late*. With everybody reported out it goes back to the plain hour. A trip with
no expected return shows no such cell.

**Outside the declared parts.** When the setup names **where the party said it was going**, a
station reported anywhere else carries an amber tag — *Outside the declared parts* — beside the
place, on the person's row and on the report in the log. It compares the station with the
declared names exactly as a reported depth is matched to them, so `upper` takes `upper.2` and
`upper2.1` alike. Nothing is marked when nothing was declared, for a place reported on a survey
the watch no longer uses, or for a place you are not shown: the tag only ever stands beside a
station that is on your screen. A depth chosen from the cave's own declared places lands on the
station the cave declared, which may itself lie outside the parts this trip named — and is then
marked like any other.

**What none of this does.** The three marks — *No word for over 3 h*, *2 h late*, *Outside the
declared parts* — are readings for whoever is looking at the tab, and the tab says so under the
counts: *nothing is sent, no alarm is raised and none is stood down.* Nobody is notified, by
e-mail or otherwise, and a tab nobody has open tells nobody anything. The planned hour is read
off the trip; whether a [callout](checklists-and-callout.md) was arranged, and whether it has
fired, is not involved in any of them — if somebody must be told when a party is late, arrange
the callout. A published page shows none of the three.

## The party on the survey

The model panel marks each person where they were last reported, with **Underground**, **Out**
or **Not heard from** — three states, never two: a party nobody has heard from yet is not a
party already out.

| You may see | Meaning |
|---|---|
| **On another survey** | The place was reported against a different survey than the one the watch uses now |
| **Not on the drawing** | The survey's drawing holds no station of that name |
| **Not shown to you** | The position exists and you may not be told it: the cave's exact location is withheld from you |

**Show only the declared parts** — offered above the model when the setup names where the party
said it was going — takes off the drawing every survey that holds nothing declared, and says how
many it hid. Everybody keeps their marker: somebody reported outside the declared parts is then
drawn without the passage around them, which is the same thing the amber tag in the table says.
The switch stays off, and says why, when the drawing has no survey of a declared name, or when
every survey of it is declared. Turning it off brings back exactly what it hid.

**Replay the trip** plays the whole log back on the survey, with the notes said along the way;
**Back to live** returns. Photographs can be hung on **a moment** of the trip (*Photographs of
this moment*) rather than on a report, so correcting or deleting a report never loses them.

**Look at the reports on** — a chooser above the model, offered only on a trip whose reports were
not all recorded on one survey: the watch was moved to a corrected survey part-way through, say,
and the earlier reports name stations of the one it left. It lists the watch's own survey and
every other survey a report of this trip was recorded on that is still on the server and that
you may open; two surveys of one name are told apart by the day each was added. Choose another
one and the markers, the replay, the station photographs and the map sheets are those of the
reports recorded on *it* — somebody whose last place is on the watch's survey is then the one
listed as *On another survey*.

**While another survey is on screen the panel records nothing, and says so**: pressing a station
offers no report, and no photograph can be hung on a moment. A report is always measured against
the survey the watch uses, so a station pressed on an earlier survey would be looked up, by name,
in the wrong one. Choose the watch's survey again — or hide the model, which always reopens on
it — to record. A survey deleted since, or one of a cave whose location is withheld from you, is
not on the list; the reports made on it stay marked in the table, in words.

**Make a movie**, on the same panel, saves the replay as a file — a GIF or a video of the party
moving through the survey, of this trip or of several. See
[A movie of a tracked trip](tracking-movie.md).

### Several parties at once: a camp's head count

A trip that belongs to a [camp](camps.md) is also counted on the camp's **Who is underground**
tab: every trip of the camp being tracked, or closed in the last two days, with its
**Underground · Out · Not heard from** numbers and its people, on one screen. Nothing has to be
switched on — starting tracking on the trip is what puts it there.

That tab is the count and nothing else:

- **It shows no place.** No station, depth, survey or cave is on it, for anybody — so it can
  tell somebody that a person is underground in a cave whose location is withheld from them,
  exactly as this tab's *Not shown to you* does. Where people are stays here, on the trip.
- **It raises no alarm either.** It prints the hour the party plans to be out by as a time, and
  compares it with nothing. The [callout](checklists-and-callout.md) is not on it.
- **It lists only the trips its reader may read**, and names people as the trip's own page
  names them to that reader.

See [Camps → Who is underground](camps.md#who-is-underground).

---

## Correcting and deleting reports

Each row of **Reports** offers two controls, and they mean different things:

- **Correct** — for a report written down wrongly. It changes the moment, the place, the team
  or the note, and **the report keeps its place on the log and everything pinned to it**. The
  person cannot be changed: a report about somebody else is a different report, so delete this
  one and record that one. The dialog says plainly that this changes what the log says
  happened — the replay and the published page follow it.

  The dialog's title says whose report it is and of when — *Correct the report about Ana of
  12 May, 14:05* — so the row pressed is the row being changed. **The place is asked with the
  same block as the report card**: the cave's declared places to choose from, a station whose
  names are offered as you type, or a depth with **Which station is that?** under it. A depth
  that is far from the station it lands on is warned about **as soon as the dialog opens**,
  without the number being touched — a report recorded past that warning still carries it.
  A moment earlier than the start of the watch brings *This moment is before the watch was
  started*, with the hour the watch began: it is a warning and not a bar, and the correction
  is saved as written — check the day and the hour first.
- **Delete this report** — only for a report that should not be there at all. It comes off the
  log, and off everything drawn from the log: the party table, the survey, a replay, a
  published page. **It is not destroyed** — see *Putting a deleted report back* below.

Both work on a **closed** watch as well as a running one.

**Putting a deleted report back.** The notice that says *The report is off the log* carries
**Undo** for a few seconds — for the bin pressed on the wrong row. After that the report waits
under the log, in **Removed reports** (the heading carries the count, *Removed reports: 2*, and
is not drawn while there are none). Open it and each removed report is listed with its moment,
its person, its place and when it was taken off, beside two buttons:

- **Put back** — the report returns to the log exactly as it was: the same moment, the same
  place on the same survey, the same note. It is not marked **Corrected**, because nobody
  corrected it.
- **Delete for good** — destroys the report. The confirmation says so: **this cannot be
  undone**, and nothing in the application brings the report back afterwards. Only a report
  already taken off the log can be deleted for good, so losing one always takes two separate
  acts.

A removed report is kept for as long as its trip exists; deleting the trip for good takes its
removed reports with it. *Removed reports* is shown to those who may write the trip's log, and
a place you may not be told on the log is not told there either.

**Reaching the report.** **Reports** shows twenty at a time, the most recent first, and the
whole log is behind it: the page numbers under the table, or on a phone **Show older reports**
and **Show newer reports**, with a line saying where you are (*Reports shown: 21–40 of 57*).
The wrong report is nearly always one person's, so the chooser above the table — it reads
**Everybody's reports** — narrows the log to the person you pick from the trip's roster;
clearing it brings everybody's back. Narrowing always starts again from that person's most
recent reports.

**Corrected.** A report that no longer reads as it was first written down carries the word
**Corrected** beside its moment. It appears after **Correct**, after a sheet that overwrote the
report (below), and after the roster entry it is about was merged into another. A correction
that changed nothing leaves no mark. The word says *that* the report was changed — not when,
and not by whom — and it is shown on this log only: nothing on a published page carries it.

**On another survey.** In **Reports**, a place carries this grey tag when it was recorded on a
survey the watch has since stopped using — the watch was moved to another survey after that
report, or the survey it was measured in has been deleted. Nothing is wrong with the report: it
is what was said at the time. The tag is there because the same station name can be another
place on the survey in use. It is the quiet twin of the amber tag in the table of people, which
says the same thing about where somebody is *now*. To see such a report where it was made, open
the model and use **Look at the reports on** (above).

**Some imported positions cannot be corrected one by one.** A SpeleoLoc recording imported onto
a trip that already existed puts its positions on that trip's log and leaves its tracking as it
was — an import does not start a watch on somebody else's trip. Where tracking was never
started, the log is not open for writing, so those rows are shown **without** *Correct* and
*Delete this report*, and a notice above them says where they came from. They are taken back
all together, by **Undo** on that import under **Geodata → Imports**.

Two cases that look alike are not in this state. A recording that **created** its trip writes
the trip's tracking already closed, and its rows are corrected like any others. And reports read
from a spreadsheet (below) only ever land on a trip whose tracking has been started, so they can
always be corrected and deleted row by row.

---

## What a cave's depths mean

A depth on its own is not a place: where several stations sit near each other, arithmetic
cannot tell which one people mean by "96 metres". The club can. On the **cave's** page, the
section **What this cave's depths mean** → **Declare a depth**:

| Field | |
|---|---|
| **Depth** | The depth being declared. Kept to one decimal; the sign is ignored |
| **Station** | Which station that depth is. As you type, the stations of the cave's current survey whose names begin that way are offered under the box — capitals and accents do not matter — and choosing one fills in the survey's own spelling. Any name is still taken: the list is a help, not a rule |
| **Place name** | Optional — the name people use for it ("Meander") |

The list under **Station** offers nothing where the cave has no survey that has been read yet,
or where the cave's exact location is withheld from you; the box is then a plain text box. When
there are more names than it shows, its last line says how many — type more of the name.

- **One declaration per depth.** Declaring a depth again replaces what it said. Editing a
  declaration to a different depth writes the new one and then withdraws the old.
- **Every report of that depth lands on the declared station** — typed, corrected or imported —
  as long as the watch's survey still has that station. One that names a station the survey no
  longer has (a survey re-uploaded since) is passed over, the depth is placed by measurement,
  and the tab says which of the two happened.
- **A place name becomes something to report by.** The report card offers the declared places
  by name, shallowest first; choosing one reports its depth, which lands on the declared station.
  Where the cave has declared none the card says so — *This cave has declared no places, so a
  place is reported as a station or as a depth* — with a link, **Declare them on the cave's
  page**, that opens this card. The correction dialog says the same.
- **Not in the current survey.** A declared place carries this amber tag when the survey marked
  as the cave's **current** one has no station of that name — a mistyped name, or a survey
  exported again with its stations renamed and then **made the current one**. The declaration
  is not followed there: a report of that depth lands on the nearest station instead. Correct
  the station's name (the list offers the current survey's), or check which survey is the cave's
  current one. **A survey that has only been uploaded is not judged here**: uploading does not
  take the mark from the survey that has it, so a corrected file shows no tags until it is made
  current — and no tags then is not a sign the declarations suit it. A watch pointed at another
  survey says so itself: the report card judges each place against the survey the watch uses,
  in its list of places — *Meander — station not in this survey*. No tag is not a promise
  either way: where the current survey has not been read (even if another upload has), or the
  cave's location is withheld from you, nothing is said.
- **Withdraw** removes a declaration. Reports already recorded keep the station they were given.
- **A station the survey file gives no name cannot be declared.** It is known only by the number
  its file wrote it at, and the next export of the survey gives that number to another station —
  so the declaration would come to mean a different place without anybody changing it. Declare
  the depth at a station that has a name.

Declaring needs the right to write to the cave. A declared station is a position, so the list
carries the cave's own protection.

---

## Importing a spreadsheet of reports

What a coordinator often actually keeps is a sheet: a line per phone call, with a time, who it
was about, how deep they were and a note. **Import a sheet**, beside the reports.

**A sheet is a desk task.** It is for writing a trip up from notes: read whole, checked row by
row, then written in one go. For a report that comes in while the party is underground, use the
report form — it takes one report at a time and overwrites nothing. A watch that has not been
started takes no sheet: reading one says so at once, as importing it would.

1. **Download a sample sheet** — a template this installation reads without any settings.
2. **Choose a CSV file, or drop one here** — or switch to **Pasted rows** and paste the rows
   copied from your spreadsheet, header row first, which is the quick way on a phone. The
   characters and the column separator are worked out from the sheet and shown beside it;
   **File settings** overrules either, sets **Day and month** where the sheet's own dates cannot
   settle which number is the day, and sets the **Sheet's time zone**.
3. **Read it.** Reading writes nothing: what comes back is exactly what importing would do,
   row by row — **New**, **Replaces**, or refused — with **Findings** beside each row. Above the
   rows the dialog states how the times and dates were read. A place the sheet named is shown
   with the station it became (*Meandru → p8.98*), so you can see the name was understood. A row
   marked **Replaces** shows two lines: **In the log now** — the report as it stands — and
   **After the import**. Where the place of the report in the log is one you may not be told,
   it reads *Not shown to you* rather than being left blank.
4. **Column settings** — point a field at a header only where the detection got it wrong. The
   **Words for going in** and **Words for coming out** replace the usual lists for that side.
5. **Untick** any row you do not want (a line naming several people is taken or left out
   whole), tick **Overwrite what the log already holds at these moments** if you mean to, and
   **Import**.

When it is done, one line says what happened, in four counts: **recorded** — new reports;
**corrected** — reports the log already held that the sheet changed; **already as the sheet
says** — reports the sheet was allowed to overwrite and had nothing to change in; and **left
out** — rows you unticked, and rows the log already holds where overwriting was not ticked.

**Where the moment is written.** Three layouts are read, and which one a sheet uses is worked
out from its header row: one column carrying the date and the time together (`Data si ora`, as
in the sample), a date column beside a time column (`Data`, `Ora`), or a time column on its own,
for a sheet kept during a single day — the dialog then asks for the day.

**Whose clock the times are on.** A sheet's *14:05* is a time on somebody's wall. Left as it
is, **Sheet's time zone** reads it **Exactly as written (UTC)**; choose **My time zone** when
the sheet was filled in off your own clock, or search for another zone by name. The choice is
made for the whole sheet, each time it is read; nothing about it is remembered on the trip.

| Rule | Why |
|---|---|
| **A report is filed under the person and the moment** | A corrected sheet imported again corrects the rows it corrected instead of doubling them |
| **Overwriting changes only what the sheet has a column for** | Where the log already holds the report, where the person was is always written; the team only if the sheet has a team column, and the note only if it has a note or a details column — an empty cell under such a column clears it. A sheet of times and depths leaves a note typed by hand standing. Reports that already say what the sheet says are not written again, and are counted apart as *already as the sheet says* |
| **Import does what the table showed, or nothing** | The sheet is read again when you press **Import**. If the trip changed in between — somebody typed a report at one of the sheet's moments or corrected one the sheet would replace, a team or a participant changed, a place was declared in the cave, the watch was put on another survey — nothing is written, the sheet is read again and the dialog says so: check the rows, tick the overwrite again if you still mean to, and press **Import** |
| **The same sheet imported a second time writes nothing** | Every one of its rows is found on the log already. Without the overwrite tick they are all *left out*; with it, each is compared with the report the log holds and, saying the same, is counted as *already as the sheet says* — no report is added, none is rewritten, and none becomes **Corrected**. So a sheet you are still adding to can be imported again as often as you like: only its new rows and its changed ones are written |
| **A time written without a zone is read as UTC, exactly as written — unless you name the sheet's time zone** | A sheet's 14:30 becomes 14:30 UTC, shown in your own zone. Choose **Sheet's time zone** under **File settings** — your own zone is offered by name and any other can be searched — and 14:30 is read on that zone's clocks, summer and winter time included; the preview says which zone was used and shows each row on its clocks. A cell that writes its own offset (`2026-09-12T14:30+03:00`) is read as it says under either choice |
| **A sheet already imported is not corrected by importing it again in another zone** | A report is filed under the person and the moment, and the zone changes every moment — so the second import adds rows beside the first instead of replacing them. Delete the earlier rows first |
| **An hour the clocks skipped is refused; an hour they showed twice is read as the first of the two** | When the clocks go forward an hour never exists, and a row written in it is refused. When they go back an hour happens twice: the earlier one is taken and the row says so — write the offset in the cell if the later one was meant |
| **A sheet of times with no dates asks for its day** | The dialog asks for **The day the sheet was kept on** and offers the trip's date: check it and read the sheet again. A time earlier than one further up the sheet is flagged, whoever the two rows are about, because a sheet kept in order that does this ran past midnight and that row belongs to the next day — leave it out, or give the sheet a date column |
| **A date with no time is refused** | Filed at midnight, a whole day's reports would collapse onto one moment |
| **The date and the time may be in two separate columns** | They are joined and read as one moment, under the same rules as a single column. A row that leaves its date blank is refused — the date is not carried down from the row above |
| **Names are matched against this trip's roster only** | Full name, then given name and initial ("Ion P."), then given name. A name matching nobody, or two people, is refused rather than guessed |
| **A row's place: station, then declared place, then depth** | Going in and coming out claim no station |
| **A row with a note and no place is a note — if the place cells are empty** | A call that said "water rising" and no place is a report all the same. A depth that is not a number (*96 cm*) or a standing word neither list knows is not an empty cell: that row is refused, with the cell named, rather than filed as its note — correct the cell, or add the word under **Column settings** |
| **Two rows for the same person at the same moment are one report** | The last one wins, and both lines say so |
| **A note longer than a report may carry is refused** | 2000 characters, note and details together — that row, not the sheet |
| **A moment the log already holds twice for that person is refused** | Which of the two the row would correct is not the importer's to guess |

A sheet can be imported onto a **closed** watch — that is usually when it is typed up. A trip
with no watch at all has nothing to import onto: choose a survey and save the setup first.

---

## Publishing a trip

**Publish this trip** → **Create a follow link**, on a trip whose tracking is running.

- **Copy it now — this is the only time it is shown.** Only a fingerprint of the link is kept,
  so it cannot be shown again; a lost link is replaced (below).
- The same panel gives **For a website**: a block to paste into your club's site (below).
- **Publishing needs the right to share the cave**, because the page hands over the cave's
  survey drawing.
- **A cave whose coordinates are protected cannot be published at all.** Not blurred, not
  partial — refused, and decided again every time the page is read, so protecting a cave later
  closes links already handed out.
- **Everybody on the trip who has an account is told** that it has been published.
- **A warning may appear beside the new link**: *"A protected cave, or one of its entrances,
  stands inside the area this trip's survey covers."* Nothing was refused and the link works.
  It is a check by position that cannot see inside the survey file — what it means, and what it
  can miss, is under [Everything published](#everything-published-for-full-administrators).

**Who the page names.** By default this installation publishes **real names**; an operator can
switch that off for everybody, after which people appear as *Caver 1*, *Caver 2*. **A person's
number stays theirs**: it is given when the trip first names them and does not move when the
roster is edited afterwards — giving somebody another role, or taking somebody else off the
trip, renumbers nobody. Somebody taken off leaves a gap (*Caver 1*, *Caver 3*) rather than
handing their number to the next person, and gets the same number back if the trip names them
again. The
**On the public page** column shows, for each person, what the page will call them. A
**caption** outranks that setting in both directions — *"third of the party"* keeps one person
off a page that would otherwise name them, without turning names off for the club.

**A link ends on its own.** It works while the watch is running, then for a grace period after
closing (two days by default) so the people following can read that everybody is out, and in
any case only until the date the panel shows (by default two weeks after the trip's last day).
**Take it back** ends it at once. Each link is listed as **Live**, **Not open** or **Run out**.

**A link that has run out is still listed, and still matters.** Running out ends the following,
not the publication: while nobody takes the link back it keeps the finished trip among its
cave's past trips, and its address still opens them. So a **Run out** row keeps both buttons —
**Replace link** when the address is somewhere it should not be, **Take it back** to remove the
trip from the cave's past trips as well.

**Replace link** is for an address that has ended up somewhere it should not be, or that
nobody kept. In one act the old address stops answering — wherever it was pasted — and a fresh
one is shown, once. The fresh link runs out when the old one would have, the trip keeps its
place among the cave's past trips, and nobody on the trip is told again. It asks for what
publishing asks for (the right to share the cave, a cave that is not protected) but **not**
for a running watch, so a finished trip's link can be replaced without starting its watch
again — one that has run out included. A link that has already been taken back cannot be
replaced.

---

## What a follower sees

The link opens a page with **no link anywhere on it** and nothing else of the installation: the
trip's name, each person with where they were last reported and when they were last heard of,
and the survey with everybody drawn on it. It refreshes itself every minute while the party is
underground, says *"This trip is over"* once the watch is closed, and grows a tab per scanned
map where the survey has them.

The page says what it is showing, for a reader who has never followed a trip:

- **What a place is.** One standing sentence over the party — each place is where somebody was
  last *reported*, at the time beside it, passed out of the cave and typed in by a person, and
  not a live position — and **How this page works** opens the rest in place: nobody is tracked by
  a device, long gaps are normal underground, and a gap means nobody has passed word yet.
- **Since when.** Under the title, *Followed since 08:40 · 3 hr 10 min* while the watch runs, and
  the span it ran for once it is closed. The hour is when the watch was **started**, not when
  anybody went underground.
- **How old the page is.** *Page updated 40 seconds ago*, and when a read fails *"This page has
  stopped refreshing"* says since when — that is about the page and the connection, never about
  the trip. A framed page says the same in one line. The same notice stands while the phone
  knows it has no connection (the page then does not even try to read, and tries by itself the
  moment the connection is back) and while the server has asked the page to wait before asking
  again — a busy server names the wait, and the page keeps to it instead of asking sooner.
- **Times that stay true.** Every *N minutes ago* moves on by itself while the page is open
  (paused while its tab is in the background). Once the trip is over, or the link has stopped
  answering, the page prints the hour instead — with the date when it is not today — because a
  finished trip's "ago" would otherwise grow all night.
- **Which language.** The page opens in Romanian. The **English** button at the foot of the
  page switches it (and **Română** switches back); the choice is remembered in that browser for
  published trips — it does not change the language the application itself opens in for somebody
  who signs in there — and is
  written into the address (`?lang=en`), so the address sent on opens the same way. A link can
  ask for a language itself — see *Putting it on your website* — and that lasts for the visit only: what
  the address says wins over what was chosen in the browser before, and is not remembered. The
  language the browser is set to is not asked. The 3D viewer takes its language once, when it
  opens: after the button is pressed its own tooltips stay as they were until the page is
  loaded again.

Two lists stand at the bottom of the page, each shut until it is pressed. A reader who opens
neither is told about nobody else, and the page asks the server for neither.

**Also in this cave now** opens the parties being followed in the same cave at this moment:

- **Being followed now** — every party of the same cave being followed at this moment, the
  link's own trip marked *This link's trip*. Every other party's row says how many of its people
  are *Underground*, *Out* and *Not reported yet*, and the camp it is out from where it has one.
  A party whose watch has just been closed says *Just finished*, never *Underground now*.
- **Watch**, on another party's row, puts that party on the page in place of the link's own:
  its name in the heading, its people in the list, its markers on the drawing — under a banner
  that says *"You are watching another party of this cave"*, with **Back to this link's trip**
  beside it. The watched row says *Watching*, and the link's own row carries the same way back.
  In a framed viewer the list opens from the button on the frame's one line, and that line then
  reads *Another party of this cave: …* with the way back beside it. What watching does and
  does not show:
  - **Only what the list already held.** Everybody holding the link is handed the cave's
    followed parties when they open the list; **Watch** draws one of them and asks the server
    for nothing more. It opens no other link and needs none.
  - **On this link's survey, and no other.** A place the other party reported on a different
    survey of the cave is listed as such and not drawn, exactly as for the link's own party.
    The pictures and map sheets around the drawing stay the link's too.
  - **The browser tab and the address stay the link's own trip.** An address copied while
    watching opens the trip the link was published for: there is no link to "the other party",
    and reloading the page returns to the link's own trip.
  - **As fresh as the list.** The watched party is read with the list, every minute, for as
    long as it is watched — the list need not stay open — and *Page updated …* then says how old
    the list is.
  - **It ends out loud.** A watched party whose watch is closed is told as *"This party's trip
    is over"*. When it leaves the list — its link taken back, or the time a closed watch stays
    readable run out — the page goes back to the link's own trip under a notice that names the
    party it was. It never changes silently whom you are looking at. Picking a past trip ends a
    watch as well: one party at a time.
- **How fresh the list is.** While a party in it is underground the list is read again every
  minute; while nobody is, every five minutes, for as long as the list is open and its tab is
  in view — which is how a party that goes in later appears without reopening it.

**Past trips in this cave** opens the cave's finished published trips:

- **How fresh the list is.** The finished trips are not re-read on a clock: a list older than
  five minutes is read again when you come back to the tab or open it again.
- **Gathered by camp.** Where a finished trip was part of a camp, the list gathers the camp's
  trips under *Camp: …*, with the trips of no camp last under *Other trips of this cave*; a
  party being followed and a replay's banner say the camp in a line of their own. Where no trip
  of the cave belongs to a camp the list is one plain list. The camp is named only — it cannot
  be opened from here, and the lists cannot be narrowed to one camp.
- **The cave's finished published trips** — **Play** replays one on the survey its reports were
  measured in, under a banner that says plainly *"You are looking at a past trip"*. The view can
  keep up with one team or person; while it does, the two arrows that step from one report to
  the next step through that team's or person's own reports and pass over everybody else's (the
  marks on the rail stay the whole trip's, and where nothing was ever reported about the party
  followed the arrows go on stepping through every report). **Back to the party now** returns —
  as does the browser's own Back button, since picking a trip is a step in the page's history.
  The address follows what is on screen (`?past=…&team=…`), so a reader can send somebody that
  exact view. While a past trip is on screen, **Play another trip of this cave** directly under
  the replay's controls opens the same list there, so going through a cave's history trip by
  trip does not mean scrolling to the foot of the page and back. A replay belongs to the link it
  was opened under, and so does everything else a reader chose there — another party they asked
  to watch, the lists they opened: a second published link opened in the same tab starts on its
  own party with both lists shut, or on the past trip its own address names.
- **A link to one moment of a replay** — under the replay's rail, **Copy link to this moment**
  copies an address that opens the same trip, following the same team or person, with the clock
  at the moment showing; **Copy link that plays from here** copies one that also starts the replay
  as soon as it has loaded. The line beside the buttons says which moment went into the link. The
  address bar itself never carries the moment — it would change five times a second while a
  replay plays — so these two buttons are how a moment is sent on. Written by hand, the same link
  is `?past=<trip>&at=<instant>&play=1`: `at` is an instant with its zone
  (`2019-07-06T13:40:00Z`) and is brought into the trip's own stretch when it falls outside it;
  `play` alone starts the trip from where it opens; `play=0` (or `no`, `false`, `off`) is read as
  no. A reader whose device asks for reduced motion gets the replay opened at the moment and
  standing still, with **Play** one press away. Where the browser refuses the clipboard, the
  link is shown in a box to copy by hand.

Two consequences worth knowing before you publish:

- **Publishing one trip also puts it among the cave's past trips** for anyone holding a link to
  any published trip of that cave — including links made later, for other trips. That list
  lasts after the live page has ended, for as long as the installation keeps past trips
  readable (by default, indefinitely).
- **An old link keeps showing who is in the cave now**, through *Being followed now*, for as
  long as its own trip stays readable as a past trip. An operator who does not want that sets
  how long after its own trip an old link goes on doing so — the past trips stay readable — or
  turns past trips off, or sets how long they are kept; see the [install
  guide](../../INSTALL.md#configuration-reference).

The whole rule — how long each of these lasts, what a past trip hands over and how a trip is
taken off — is under [How long a published trip stays readable, and taking one
off](#how-long-a-published-trip-stays-readable-and-taking-one-off).

---

## Putting it on your website

The **For a website** block goes into a **Custom HTML** block — not a paragraph, which prints
the code instead of running it. It sizes itself from the column's width; do not give it a
fixed height. Your installation's administrator must also allow your site to frame it
(`SILEXGIS_FRAME_ANCESTORS`, see [Letting a website embed a live
trip](../../INSTALL.md#letting-a-website-embed-a-live-trip)); until then the frame stays blank.

**The frame's language.** Above the address and the block, **Language the page opens in** has
three positions: *None named*, *Română*, *English*. Picking one rewrites the address and the
block (`?lang=ro` or `?lang=en`) without creating a new link — the same link answers in either
language, so you can copy the Romanian address for one article and the English one for another.
An address or a block that names no language opens in Romanian — or in the language its reader
chose on this installation before, in that browser. The frame also carries a two-letter button
(**EN** / **RO**) in its bottom line, for the reader in front of it; while a past trip is
playing in the frame that line is the replay's controls and the button is not drawn. A frame
with nothing to show — a link that has ended, a read that failed — carries the button under its
one sentence.

**A frame that opens on a past trip.** The frame's address answers to the same words as the
full page's: add `past=<trip>`, and where wanted `team=` or `caver=`, `at=<instant>` and
`play=1`, to the address inside the block (`…/embed?lang=en&past=…&at=…&play=1`) and the frame
opens on that trip, at that moment, playing. The values are the ones **Copy link to this
moment** puts into the address it copies.

**What the frame shows while a past trip plays** depends on the room the frame has — its own
width and height, not the screen's.

- **A frame at least 800 px wide and 760 px tall** (the block's 4:3 box in a column about 1,020 px
  wide, or a box you made taller) shows the whole strip under the drawing: the statement that this
  is a past trip, the clock, play and pause, the speed, the steps from one report to the next, whom
  the view keeps up with, the rail and the way back, all on screen at once, so the rail is dragged
  while looking at the drawing it moves. Beside the way back, **Past trips in this cave** opens a
  sheet over the frame with the cave's two lists.
- **A smaller frame** — the 4:3 box in an ordinary article's column as well as a phone's — shows
  one line along its bottom edge: a **Past trip** tag and the trip's name, the clock, play and
  pause, the way back (**Now** where the block's own party is underground, **Back** where it is
  not) and one last button. That button opens a sheet over the frame with the rest: the rail, the
  speed, the steps from one report to the next, whom the view keeps up with and the statement in
  full. On a phone the frame is about 260 px tall, and the line is what leaves the drawing four
  fifths of it; the whole strip, with somebody followed and everything it can have to say, is about
  350 px tall under a finger and 300 under a mouse, and is shown only where the drawing still keeps
  half the frame beside it. Under those controls, **Past trips in this cave** opens the cave's two
  lists in the same sheet, so a reader can change trip without leaving the replay first.

In both, moving through a replay asks the server for nothing: the cave's lists — who else is
underground now, and the finished trips — are read only when a reader presses the button named
for them, exactly as while the party of now is on screen. A replay whose record was cut short
says so on the frame itself, in either layout, so a clock that stops early is not taken for the
end of the trip. The frame keeps its amber outline for as long as the past is on screen.

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
| `data-silexgis-play` | Added to a link that names a past trip or a moment: also starts the replay |

**Where the values come from: press copy.** Open the full page, play the past trip to the moment
you mean, choose whom to follow, and press **Copy link to this moment**. The address it copies
names the trip (`past=`), the team or the person (`team=` or `caver=`) and the moment (`at=`) —
the values for `data-silexgis-trip`, `-team` or `-caver`, and `-moment`. `data-silexgis-play`
takes no value; pressed twice it leaves the replay playing rather than pausing it, and a link
without it never pauses a replay that is running. Blocks pasted before this attribute existed
keep working unchanged: copy the block again from the share panel only if you want links that
play.

With two viewers in one article, every link names the one it drives with `data-silexgis-target`
(the id is in the comment at the top of each block).

### What the frame tells your page

Most articles need none of this. It is for a site that writes its own words from what the
viewer shows — greying out the name of somebody nobody has placed, or printing *"the party is
at …"* beside the frame.

The pasted block passes everything the viewer says on to your page as ordinary browser events
on `document`. Listen for them in a **Custom HTML** block of your own, anywhere in the article;
this one can be pasted as it is:

```html
<script>
document.addEventListener('silexgis:ready', function (event) {
  var said = event.detail;
  if (said.pastUnreadable) {
    // The server REFUSED the past trip the frame was asked for: it has been taken back, or is
    // older than this installation keeps. said.pastUnreadable.tripLogId says which trip; there
    // is no title and no moment, and said.past is absent. Say so in your own words and offer a
    // link with data-silexgis-trip="live" - and take the words down again on the next
    // announcement, which no longer carries it. Checked BEFORE loaded, which stays false.
    return;
  }
  if (!said.loaded) return;   // nothing has arrived yet - not the same as "nobody"
  if (said.past) {
    // A REPLAY of a past trip: said.past.title, said.past.tripLogId, and said.past.at
    // (where the replay's clock stood, an instant such as 2019-07-06T13:40:00.000Z).
    // Nothing below is where anybody is now.
  }
  if (said.watching) {
    // ANOTHER PARTY of the cave, picked by the reader inside the frame: said.watching.title
    // and said.watching.tripLogId. It is being followed now, but it is not this article's trip.
  }
  said.party.forEach(function (member) {
    // member.ordinal        the place in the party (1, 2, 3...) - what data-silexgis-caver takes
    // member.name           the name the viewer draws them under
    // member.station        the station they are drawn at, or null
    // member.onOtherSurvey  true: reported, but on another survey of the cave than this one
    // member.notOnDrawing   true: reported at a station this drawing does not contain
  });
});
document.addEventListener('silexgis:focused', function (event) {
  // event.detail.target   { kind, ref } of the link somebody pressed
  // event.detail.found    false: the viewer could not show it - grey the link out
});
</script>
```

What to rely on:

- **`silexgis:ready` is said again whenever what it says changes** — the view finishing loading,
  somebody moving (the minute's refresh while the party is underground, or a replay's clock
  carrying somebody to another station), a replay starting or being left, the reader choosing
  another party of the cave to watch or going back from one. It is **not** said on
  every tick of a replay's clock: `past.at` is the moment the announcement was true at, not a
  running clock.
- **Wait for `loaded: true`.** The first announcement usually arrives before the party has,
  with `loaded: false` and an empty party. An empty party with `loaded: true` is a trip with
  nobody on it.
- **`past` present means a replay**, and always carries `tripLogId`, `title` and `at`. Absent,
  and with `loaded: true`, the party is the block's own trip as it stands now. Never print a
  replayed party as where people are.
- **`pastUnreadable` present means the server refused the past trip asked for** — its link was
  taken back, it is older than the installation keeps, or it was never a trip of this cave. It
  carries `tripLogId` and nothing else; beside it `past` is absent, `party` is empty and
  `loaded` is `false`, because nothing is on screen — so read it **before** the `loaded` check,
  as the example does. It is said once and stands until the next announcement, which no longer
  carries it: the reader went to another trip or back to the party now, or — rarely — the trip
  was published again and a later read was answered, in which case that announcement is an
  ordinary `past`. It is **not** sent for a read that merely did not arrive (a phone without
  signal, a server fault): the frame then goes on announcing `loaded: false` with nothing
  beside it, tells its own reader that the trip could not be read, and reads again when they
  return to the tab. The frame keeps its way back on screen either way; a link with
  `data-silexgis-trip="live"` brings it back from your own prose. A listener written before
  this member existed is not affected by it: `past` is untouched, and it stops at
  `loaded: false` exactly as it did before, when a refused trip was announced as nothing at
  all.
- **`watching` present means another party of the cave.** The reader opened the frame's list
  of parties being followed in the cave and pressed **Watch** on one of them. That party is
  underground now and its members are numbered from 1 exactly as your trip's are, so a page
  that prints *"caver 3 is at …"* must say whose caver 3 it is — or print nothing while
  `watching` is present. Absent, the party is not another party: it is the block's own trip, or
  a replay when `past` is present; the two are never present together. A link with
  `data-silexgis-trip="live"` brings the frame back to the block's own trip from either.

  **A listener written before `watching` existed has to be changed before it is right again.**
  While a party is watched, `party` carries *that* party's people and stations — the same
  member, in the same shape, with nothing else about it different — and a pressed link with
  `data-silexgis-caver` is answered from the party on screen as well. A page that prints names
  or places from `party` without reading `watching` therefore prints another party's people
  under its own trip's heading from the moment its reader presses **Watch** inside the frame.
  Nothing is disclosed that the link did not already hand over, but the words would be wrong.
  If your page prints anything from `party`, add the `if (said.watching)` check above — print
  whose party it is, or print nothing — before relying on the frame's list of parties.
- **`station: null` has three meanings**, and two of them are not "nobody knows". Nobody has
  reported where that person is; or somebody has, on a different survey of the cave
  (`onOtherSurvey`); or at a station the drawing in the frame does not hold (`notOnDrawing`).
  Word each differently. In the last two cases the station's name is deliberately not handed
  over — the frame's own list of people says it.
- **`silexgis:focused` answers a pressed link.** For a station, a branch or a member of the
  party, `found` says whether the drawing held the place (over a replay, a link naming a person
  or a team means *keep up with them*, and answers `true`). For a link that names a past trip,
  `found: true` only says the frame took the request; which trip is actually on screen arrives
  with the next `silexgis:ready`. A `data-silexgis-moment` link pressed while no replay is on
  screen answers `found: false`.
- **`silexgis:listening`** also arrives, carrying nothing: it is the viewer saying it can hear,
  and the pasted block answers it by itself. Ignore it.
- Every `event.detail` also carries `silexgis: "silexgis-trip-embed"`, `v: 1` and `type`.
  Members may be added later; none of these will change meaning under the same `v`.
- **What your page is given is what the frame shows** — the same names and the same stations,
  nothing more. It is given to your page, so any other script running in the article can read
  it as well.

**An old block may never say anything.** The viewer answers only once it has been greeted, and
it starts listening a moment after the frame has loaded. A block pasted before
**29 September 2026** greets it twice, both usually too early, so no `silexgis:ready` ever
reaches your page — while the frame itself works, which is why this goes unnoticed. A block
copied from the publish panel now greets every half second until the viewer answers (for up to
a minute, and again whenever the frame reloads). If your listener hears nothing, paste a fresh
block — and **replace every older block in the article** rather than adding a new one beside
it: the first block on a page runs the conversation for all of them.

---

## How long a published trip stays readable, and taking one off

Publishing gives a trip **two lifetimes**, and they end separately.

**The link's own page — while the party is being followed.** The address answers while **all**
of these hold:

- the watch is running, or was closed less than the grace period ago (two days by default);
- the date the publish panel shows has not passed (by default two weeks after the trip's last
  day, or after the link was made when that is later);
- the link has not been taken back;
- the cave's coordinates are not protected.

When one stops holding, the address answers *"Nothing to show for this link"* — word for word
what an address that never existed answers. A page that was open at that moment keeps the last
party it was given on screen, under *"This link has stopped answering"* and the hour it was
last read, until the reader closes it. A link that ended because the watch was closed works
again if the watch is started again before the link's date; a link that was **taken back**
never works again.

**The cave's past trips — after it is over.** A finished trip is listed, and can be replayed,
through a link to any published trip of the same cave. It is there while **all** of these hold:

- its watch is closed and its own page has stopped answering (so not during the grace period);
- at least one link to it was **not taken back** — a link that merely ran out by date still
  counts;
- the installation offers past trips, and the trip is younger than the limit the installation
  sets, if it sets one (by default there is none: a club's published history stays readable);
- the cave's coordinates are not protected.

**Which links open that list.** A link that was not taken back opens it while its own page
answers, and afterwards for as long as its own trip is itself among the past trips. The second
half is narrower on the page than it sounds: once a link's own page has ended, the address
opened afresh shows *"Nothing to show for this link"* and offers no list. What still answers
is the list and the replays behind it — to a page that was already open when the link ended,
and to a website that reads the published-trip addresses itself.

**An old link and today's parties.** The same link also lists the parties being followed in
the cave now. Where an installation has stopped an old link from doing that while leaving the
cave's past trips open, a page that was given the past trips and was refused the list of
parties for good says *"This link no longer lists who is in the cave today"* where the list
would be. It is said quietly, as a fact about the link and not as a failure: nothing is tried
again, and the past trips are still there to read and play. The server gives no reason for a
refusal, so the page says this only on that evidence — past trips answered, the list of
parties refused for good — and only when the two answers belong to one moment: the past trips
read after the list was refused or a few minutes before it at most, and not before the list
last answered. A list of past trips that is merely still on the page from earlier does not
count, because a link taken back while the page is open leaves exactly that behind; such a
page goes on saying that the link has stopped answering, and says the quieter thing only if
the past trips are read again and answer. Nothing is read just to find out. A list that merely
could not be read just now keeps the wording that invites another try, and where the past
trips are refused as well the page says what it says of any link that has stopped answering.

**What a past trip hands over.** In the list: its name, its dates, the camp it belonged to, how
many were on it, and when its watch was closed. In the replay: the party, the reports with
their times, and the survey they were measured on. **Nothing is copied when a trip is
published** — the replay is read from the trip's own tracking log every time. So people are
named by the rule in force *today* (a caption added now keeps that person's name out of a
replay of last year's trip, and switching names off for the installation does so for
everybody) and a report corrected or deleted in the log is corrected or gone in the replay —
and back in it when the report is put back.

**Running out removes nothing.** A link that has ended, and a trip that has aged out of the
past trips, are only no longer *shown to visitors*: the trip, its tracking log and its reports
stay in the installation exactly as they were, for the members who may read them.

**Taking a trip off.** From the least to the most:

| You want | Do this |
|---|---|
| One person's name off the page and the replay | Give them a **caption** on the trip — it applies from the next read |
| One report gone | Delete it in the tracking log: it leaves the page and the replay at once. It is still kept under **Removed reports** for the trip's coordinators — **Delete for good** there if it must not exist at all |
| The live page closed now | **Take it back**, on each link in **Publish this trip** |
| The trip out of the cave's past trips | **Take back every link the panel lists for that trip**, the ones marked **Not open** included. With none left the trip is no longer listed or replayable, at once |
| Everything of that trip gone | Delete the trip: its links end with it |
| The whole cave closed to visitors | Protect the cave's coordinates: every link to every trip of it stops answering, and so do its past trips, for as long as the protection stands |
| No past trips for anybody, or only recent ones | The operator switches past trips off or sets how long they are kept — see the [install guide](../../INSTALL.md#configuration-reference) |

Taking a link back cannot be undone, and a new link can be made only while the watch is
running — so a finished trip that was taken off is published again only by starting its watch
again. And nothing here reaches what a reader already has: a page left open keeps what it last
showed, and a screenshot is a screenshot.

---

## Everything published, for full administrators

**Administration → Published trips** lists every follow link the installation has handed
out, across every trip, whatever the trip's own access rules say. It is open to **Full
Administrators** and to nobody else: anybody else who types its address is told so, and the
rail does not offer it.

**One row per link**, and a link stays listed after it has been taken back, for as long as its
trip exists. Each row gives the trip and its dates, the cave, who published it and when, when
it runs out (or when it was taken back), and a **status** — what the address opens for whoever
holds it at the moment printed above the table:

| Status | The address opens |
|---|---|
| **Followed now** | The party, as it is reported — the watch is running |
| **Just closed** | The same page, saying everybody is out, for the grace period after closing |
| **In the archive** | The trip as history, and the cave's other finished published trips |
| **Withheld** | Nothing, *for now*: the watch has lost its cave, or the cave's position has been protected since. It answers like an unknown link for as long as that holds, and starts answering again if it stops holding |
| **Opens nothing** | Nothing: it ran out, its watch was stood down, or past trips are switched off or no longer cover it. Nobody took it back |
| **Taken back** | Nothing, and never will again |

The statuses are worked out by the server, by the very rules the published pages are served
by — the list cannot say *Followed now* about an address that answers nothing. The counts
above the table are of the whole installation, the list can be narrowed to one status, and
most columns sort. Press **Refresh** before acting on a list that has been open for a while.

**Tracking left running.** Nothing closes a trip's tracking but a person — the application
never decides by itself that a party is out. The other side of that is that tracking somebody
forgot runs for ever: the party stays on its published page as still underground, and once the
link has run out the trip is in no public list at all, neither followed nor among the past
trips. The **Tracking started** column says when each trip's tracking was last started and,
while it is running, for how many days; a row whose tracking is still running behind a link
that has run out says so. **Tracking running longer than (days)** narrows the list to such
rows: type a number of days that is too long for the kind of trip your club does (`0` keeps
everything whose tracking is running at all). The number is yours each time and is stored
nowhere. **Tracking tab** on the row opens the trip where **Close tracking** is — nothing is
closed from this page, and nothing closes by itself.

**Where the server sees you from.** Under the counts the page prints the address your request
reached the server from. It should be your own. If it is instead the address of a reverse
proxy standing in front of the installation, the installation has been told about too few
proxies: every reader behind that proxy is counted as one and shares one request budget, so
published pages start refusing readers on a busy evening. The line only shows it; the cure is
the proxy count in the [install guide](../../INSTALL.md#enabling-https).

**The page never shows an address.** Only a fingerprint of each link is kept, so there is
nothing to show. The **Log code** column is the short code the server's request log writes in
place of a link's address, so a line in the log can be matched with a row; it opens nothing.

### The three things it can do

| Button | What it does | What it cannot undo |
|---|---|---|
| **Replace link** | The old address stops answering at once and a fresh one is shown, **once**, in a window that closes only on *I have copied it*. Same expiry as the old one; the trip stays in its cave's past trips; nobody is told | The old address is gone for good. An address closed before it was copied is gone too — replace the link again |
| **Unpublish this trip** | Takes back **every** link of that trip in one act, and says how many | Everything below |
| **Unpublish everything** | Takes back every link of the installation — every trip, every cave. The confirmation states how many links the list shows as still standing, says that the links of **deleted trips** are taken back as well, and asks you to type a word before the button works | Everything below, for every trip at once |

**What goes with taking a link back, and is not obvious.** The page stops answering for
everybody holding the address, on this installation and on any website showing it in a frame.
And a finished trip whose last link is gone **leaves its cave's public list of past trips** —
which other trips' links were showing too. There is no way to put a taken-back link back. A
trip is published again only by **starting its watch again** and creating a new link, one trip
at a time, and the new address has to be handed out afresh to everyone who had the old one.

**Why "everything" can take back more links than the list shows.** The list leaves out the
links of deleted trips: nobody can open them while their trip is deleted. But a deleted trip
can be restored, and its links would answer again with it exactly as they stood — so
**Unpublish everything** takes those back too, and an administrator told that every link is
gone is told the truth. The confirmation says so before you type the word. When more links
were taken back than the list showed, the result gives both figures — *Links taken back* and
*Links this list showed* — and the reason; the first is the one to trust about what was
withdrawn. **Unpublish this trip** does not reach a deleted trip: its links are not in the
list.

**The button is offered only while the list shows a standing link.** With none shown it is
greyed out — so when the only links left standing belong to deleted trips, this page cannot
take them back, and they answer again if such a trip is restored. A restored trip is back in
the list, where **Unpublish this trip** takes its links back.

That is also why there is **no "pause" switch**: an address that might start answering again
is not one anybody can call withdrawn. If the aim is to stop showing past trips without
giving up the links, that is an installation setting (see the [install
guide](../../INSTALL.md#configuration-reference)), and it shows on this page as a line above
the table.

Taking links back deletes nothing inside the installation: the trips, their reports and their
surveys are as they were, and each link taken back or replaced is recorded under its trip in
the [audit history](history-and-audit.md), with who did it.

### "Protected cave nearby?"

Publishing a trip hands over its cave's survey file, and whether that is allowed is asked of
**that** cave. A survey file is not bound to one cave, though: an export of a whole system is
filed under one cave and draws its neighbours. So when the position of a **protected** cave
other than the trip's own — or of one of its entrances — lies inside the rectangle spanned by
the survey's stations, the row carries an orange **Protected cave nearby?** tag, and the same
warning is shown beside a link when it is created or replaced.

Read it for what it is:

- **It refuses nothing.** The link exists and works. Open the survey, look, and take the link
  back if it shows what should not be published.
- **It is a check by position and cannot see inside the file.** A cave can lie inside the
  rectangle of a survey that never enters it. And a survey that *does* draw a protected
  neighbour passes unmarked when that cave's recorded point lies outside the rectangle, or when
  the survey has no stored stations to span one (a wall model, a file not yet read).
  **No tag is not a clearance.**
- **It names no cave and gives no position.** On this page, read by people who may place every
  cave, it counts every protected cave. Beside a link on a trip's own panel it counts only
  protected caves the person publishing may already see exactly — somebody who may not see a
  cave's position is not told that one is near.

### When a published page shows nothing

A published page answers a link that is wrong in *any* way with the same words — *"Nothing to
show for this link"* — whatever the reason, so that a stranger holding a guess learns nothing.
An administrator can tell the reasons apart. Start from the link's row on this page (find it by
its trip, or by the **Log code** if whoever runs the server gave you one):

| The row says | Why the page shows nothing | What to do |
|---|---|---|
| **Taken back** | Somebody took the link back, or replaced it | It cannot be put back. Hand out the address **Replace link** showed, or publish the trip again |
| There is no row for that address | The address in the article is not one of this installation's links: cut short when pasted, mistyped, or its trip was deleted | Copy the link again from the trip's publish panel |
| **In the archive** | Nothing is wrong. Tracking was closed and the grace period is over: the link's own page has ended and the trip is among its cave's past trips, which the same link opens | — |
| **Just closed**, and the trip is not among the past trips | Nothing is wrong. A trip just closed stays on its own page, marked *Just finished*, and joins the past trips when the grace period ends (two days unless the installation says otherwise) | Wait for the period to end |
| **Opens nothing**, its tracking still running | The link ran out and nobody had closed the tracking | **Close tracking** on the trip — it becomes a past trip — or **Replace link** |
| **Opens nothing**, otherwise | Past trips are switched off for the installation (a line above the table says so), or the trip is older than past trips are kept | An installation setting: ask whoever runs the server |
| **Withheld** | The cave's position has been protected since, or the tracking lost its cave | Deliberate. It answers again by itself if that stops holding |
| **Followed now**, and readers still see nothing, or are told to try again later | The link is good. Readers are being turned away as too many requests from one address, the server is not answering, or something between them and the server is repeating an old answer | For whoever runs the server, below. Look first at the address printed under the counts on this page |
| The link opens by itself, and the frame in an article is empty | The website is not allowed to frame the page | `SILEXGIS_FRAME_ANCESTORS` — see [Putting it on your website](#putting-it-on-your-website) |

**For whoever runs the server.** Every refused read is written to the server's log with a
one-word reason and the link's **Log code**, never its address; the installation counts the
published reads it answers and the ones it turns away; and a script asks an installation, from
outside, what one link answers. The full table, with the reason word for each cause, is in the
[install guide](../../INSTALL.md#when-a-published-page-or-the-article-showing-it-shows-nothing).

---

## When something is refused

| You see | Why |
|---|---|
| *"Tracking has never been started for this trip, so its log cannot be written to."* | Start tracking first; a closed watch is fine |
| *"That survey belongs to a different cave…"* | A running watch stays in its cave — close it first |
| *"No station matches that depth under this trip's filter…"* | Widen **Where the party said it was going**, or report a station |
| *"A report cannot be about the future."* | Check the time on it |
| *"That report is on the log again — somebody put it back. Take it off first, then delete it for good."* | Somebody else pressed **Put back** on it while your list of removed reports was open. Nothing was destroyed; if it still has to go, delete it from the log again |
| *"Say when this was said. The tracking is closed, so the report cannot be stamped with the present time."* | A report added to a closed watch is being written up afterwards: fill in **When it was said** with the moment it was made during the trip |
| *"This trip has no watch to import reports onto."* | Choose a survey and save the tracking setup first |
| *"Nothing was imported. The trip or its log changed after this sheet was read…"* (headed *The trip changed while this sheet was being checked*) | Between your reading the sheet and pressing **Import**, something the import depends on changed — somebody recorded a report at one of the sheet's moments, a team or a participant was changed, a place was declared in the cave — so the sheet no longer matches what you were shown. Nothing was written and the sheet has been read again: check the rows, tick the overwrite again if you still mean it, and press **Import** |
| *"Publishing this trip hands over its cave's survey drawing, and that takes the right to share the cave."* | Ask whoever looks after the cave |
| *"This trip's cave has protected coordinates, so the trip cannot be published at all."* | Deliberate — the drawing is the cave's position |
| *"That link has already been taken back, so there is nothing to replace. Publish the trip again instead."* | Somebody took it back first — possibly a moment ago, from another screen |
| *"Nothing to show for this link"* (on the public page) | Taken back, replaced, ended, or the cave has been protected since |
| *"Past trips are not offered through this link"* | The installation has switched past trips off |
| *"This link no longer lists who is in the cave today"* (on the public page) | An old link: the list of parties being followed now is refused for good while the cave's past trips, read at about the same moment, still answer. There is nothing to try again — the past trips are still there |

---

Related: [Trips](trips.md) · [Checklists and the callout](checklists-and-callout.md) ·
[Sharing, QR codes and public pages](sharing-and-public-pages.md) ·
[A movie of a tracked trip](tracking-movie.md) ·
[Location protection](../admin/location-protection.md)
