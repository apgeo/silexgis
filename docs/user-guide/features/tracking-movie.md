# A movie of a tracked trip

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/tracking-movie.md)

[← Feature reference](README.md) · Related:
[Live tracking and published trips](live-tracking.md) ·
[Surveys, centerlines and 3D models](surveys-and-models.md) · [Trips](trips.md)

---

A tracked trip leaves a log: who was reported where, and when. **Make a movie** turns that log
into a short film — the cave's survey turning slowly, each person a marker moving from station to
station as they were reported — saved as a GIF, a WebM or an MP4 file on your own computer.

The movie is made **in your browser**. Nothing is uploaded, the installation keeps no copy, and
there is no sound. It is offered only to somebody who is signed in; the public pages of a
published trip have no such button.

> **A movie is a copy of what you could see, and it travels.** The dialog says so above its
> buttons: *"The movie shows the cave's survey and where the party was. Share it only with people
> who may see both."* The file carries no protection of its own — not the cave's, not the
> trip's. [What the file can carry](#what-the-file-can-carry) says what is in it and how to make
> one that says less.

---

## Opening it

| From | What opens |
|---|---|
| A trip's **Tracking** tab, the **Make a movie** button on the survey panel | The dialog for the survey the trip is tracked on, with that trip already ticked |
| A cave's page, the camera button on a survey's row under **3D survey models** | The dialog for that survey, with no trip ticked yet |

The dialog has the **preview** on one side and the settings on the other. The preview is drawn
by a viewer of its own, so the survey panel underneath — its markers, its camera, a replay in
progress — is exactly as you left it when the dialog closes.

A movie is made on **one survey**. Trips tracked on another survey of the same cave are not
offered in the same movie.

---

## Choosing the trips

**Trips** lists the trips tracked on this survey that you may read, each with its dates and
*Reports: N*. Tick the ones the movie shows.

- **Each trip keeps its colour in the order you tick them**, so untick and tick again to
  reorder the colours.
- A trip whose tracking covers no stretch of time cannot be ticked: *"There is nothing to
  replay"*.
- A ticked trip that could not be read says so under its name, and the movie cannot be exported
  until it is unticked or read again.
- **A trip still under way can be filmed.** Its row says *"Still under way · new reports since
  opening: N"*, and the number moves as other people record reports while the dialog stands
  open. The movie ends at the moment you press **Export**, not at the moment the dialog was
  opened: the trip's log is read again at that moment, so reports that arrived in between are in
  the file. If the log cannot be read then, nothing is exported and the dialog says *"The log
  could not be read"*; press **Export** again.

With several trips, **Several trips** (under *Motion*) decides how they share the film:

| Choice | What you get | The clock shows |
|---|---|---|
| **On one calendar** | The trips play in real time order, one after another or overlapping as they really did | The date and the time |
| **Side by side** | Every trip starts at the same moment, to compare them | *Elapsed*, as hours and minutes — or minutes and seconds when the whole movie covers less than an hour |

**Shorten quiet stretches** cuts short any stretch longer than the length you set (30 minutes to
begin with) in which no chosen trip reports anything, so a night between two days does not take
up half the film. The clock shows the jump.

---

## The preview is the movie

What the preview shows is what the file will hold: the same layers, labels and captions, in a box
the shape of the frame.

- **Drag the model** to choose the view the movie starts from; if the model turns, the camera
  turns from there. **Starting view** offers the plan and four elevations, and **Turn to it
  again** puts the camera back.
- The slider, **Moment in the movie**, shows any moment of it. **Play the preview** runs the
  movie in real time, camera turn included.

---

## The settings that matter

The dialog opens on a small, cautious movie: a GIF, 640 × 360, 10 frames a second, 20 seconds
and 2 still seconds at the end, the model turning 6° a second. Your settings are **remembered
in this browser** for the next movie — all of them except the title you typed, which is never
stored.

| Group | Setting | What it decides |
|---|---|---|
| **Output** | **Format** | GIF, WebM or MP4 — see [Which format](#which-format) |
| | **Frame size** | Wide (16:9) from 320 × 180 to 1920 × 1080, or standard (4:3) from 320 × 240 to 1024 × 768 |
| | **Frames a second** | 10 to 30 for a video; a GIF holds only 10, 12.5, 20 and 25 exactly |
| | **Length (seconds)** | How long the replay lasts, 1 to 300 seconds, however long the trips really were |
| | **Still at the end (seconds)** | The last frame held, so the eye can rest on where everybody ended up |
| | **Quality** | For a GIF, the colours in its palette (64, 128 or 256); for a video, the bits spent on each frame |
| **Motion** | **Turn the model** | At a set speed, or exactly one full turn over the movie; clockwise or not. Off, the camera stands still |
| | **Marker slide (seconds)** | How long a marker takes to move to the next station |
| **Cavers** | **Caver labels** | First name, full name, initials, or no labels |
| | **Time of the last report in the label** | Adds to each person's label the time of their last reported position — with the date as well when that was not today, which for a finished trip is every time |
| | **Marker colour** | Automatic (by team for one trip, by trip for several), by trip, by team, or one colour |
| | **Show cavers who have come out** | Whether somebody reported out stays on the picture |
| | **Trail of the route walked** | A line behind each marker |
| **View** | The survey's layers, **Shading**, **Camera**, **Line width**, **Vertical scale** | What of the survey is drawn and how. A layer the survey does not have is greyed; the depth shadings need a survey that stands on real terrain |
| **Captions** | **Title**, **Clock**, **Legend**, **Progress bar**, **Latest note**, **Caption size** | What is written over the picture |

**Marker colours.** There are twelve. A movie of more than twelve trips coloured by trip — or
of more than eleven teams coloured by team — reuses them in the same order, and the legend then
carries a line saying so: *"Trip colours repeat"* or *"Team colours repeat"*.

---

## Which format

| Format | Pick it for | Limits |
|---|---|---|
| **GIF** | A loop that plays almost anywhere, with no player — a chat, a forum, an e-mail | At most 800 pixels wide and 600 frames; grows large quickly when the model turns |
| **WebM** | A video every browser plays; small for its quality | — |
| **MP4** | A video messaging apps, presentations and phones open | — |

**A format can be greyed out.** WebM and MP4 are written by your browser's own video encoder,
and the dialog asks the browser what it can write at the chosen size and rate. When it cannot,
the format is disabled and says why: *"This browser cannot write MP4 at 1920 × 1080, 30 frames a
second"* — a smaller size or a lower rate may be accepted — or *"This browser has no video
encoder, so only a GIF can be made here."*

**The size is an estimate.** Under the settings the dialog says *"300 frames · about 2.7 MB"*.
For a video the figure is the encoder's target and is usually close. For a GIF it is a guess
until you have made one: a GIF of a turning model on a dense survey can come to nearly twice the
first estimate. Once a GIF has been made in this browser, the estimate follows what your own
GIFs really came to and says *"…, going by the last GIF made here"*. Past about 10 MB the dialog
warns that the GIF is larger than most messaging apps take, and suggests making it shorter,
smaller or slower, lowering its quality, stopping the turn, or making a video.

---

## What the file can carry

Everything in the movie was shown to you under your own rights; the file is then yours to pass
on, to people the installation never checked. So it is worth knowing what is in it.

| In the file | To begin with | To leave it out |
|---|---|---|
| The survey's drawing, and where each person was reported | Always | — this is the movie |
| **People's names**, on the markers | First names. Two people who would read the same gain an initial | **Caver labels**: *Initials* or *No labels* |
| **When each person was last reported**, beside their name | Off. Switched on, each label carries the hour and minute, and the date for a report not made today | **Time of the last report in the label** off |
| **The title**, written over the picture | On. One trip: the trip's title. Several: the cave's name and the days they span. Or the words you type in **Title text** | **Title** off |
| **The legend** — trip titles or team names beside their colours | On | **Legend** off |
| **The clock** — the date and time of each moment | On | **Clock** off, or **Side by side**, which shows elapsed time and no date. Either takes the date out of the clock only — not out of the labels, if the time of the last report is switched on |
| **Entrance names** | On | Untick it under **View** |
| **Station names**, **station comments** | Off | — |
| **The latest note** — free text somebody typed with a report | Off, because *"Notes are free text and can carry safety details"* | — |
| **Compass and altitude display** | Off. Switched on, it *"prints altitudes and a bearing into every frame"* | — |
| **The file's own name** | Follows the title — see below | **Title** off |

A position that is withheld from you draws no marker in the movie, for the same reason it draws
none on the trip's own survey panel.

### The file's name

The name is shown beside the **Export** button before anything is made — *"The file will be
saved as silexgis-…"* — because a name travels further than the picture: it shows in a chat
before the movie is even opened.

| Title caption | The file is called |
|---|---|
| On, with your own **Title text** | `silexgis-<your words>-<date>` |
| On, one trip, no text of your own | `silexgis-<the trip's title>-<date>` |
| On, several trips, no text of your own | `silexgis-<the cave's name>-<date>` (the survey's name when the cave's is not known) |
| **Off** | `silexgis-movie-<date>` — nothing about which trip or which cave |

The words are written in lower case, without accents, with hyphens for everything that is not a
letter or a digit, and cut at 60 characters. The date is the day the file was made, on your own
calendar. The file is saved under exactly the name that was shown when you pressed **Export**.

### A movie that says less

Switch **Title** off, set **Caver labels** to *Initials* or *No labels*, switch **Time of the
last report in the label** off, switch **Legend** off, choose **Side by side** or switch **Clock**
off, and untick **Entrance names**. The file is then
called `silexgis-movie-<date>` and shows markers moving through an unnamed survey. The survey's
own shape remains — a movie cannot hide the cave it is a movie of.

---

## Exporting, and how long it takes

**Export GIF** (or WebM, or MP4) starts the work. The settings are locked while it runs and the
dialog says how far it has got: *"Choosing colours"* first for a GIF, then *"Frame 37 of 220"*,
then *"Writing the file…"*, with the time elapsed and *"about … left"*.

**Every frame is drawn anew, one at a time**, and then encoded. On a computer without a graphics
card the browser draws the survey in software, which is slower, and an export takes as long as
its frames are many and large:

| Measured on one many-core computer with no graphics card, twenty trips on one survey | Took |
|---|---|
| GIF, 640 × 360, 300 frames (30 seconds at 10 a second) | about half a minute |
| WebM, 1280 × 720, 800 frames (32 seconds at 25 a second) | about a minute and a half |
| MP4, 1920 × 1080, 800 frames | about two minutes |

A slower computer takes longer; the dialog's own *"about … left"* is the figure to go by. Before
a long video the dialog warns that it *"may take a long time to export"* — the warning comes at
about 1,100 frames at 1280 × 720 — and that a shorter length, a lower frame rate or a smaller
size is quicker.

When the export ends the browser saves the file and the dialog says *"Saved"* with its name. The
dialog stays open, so the same movie can be made again in another format.

---

## Cancelling

| You press | While an export runs | With no export running |
|---|---|---|
| **Cancel export** | Stops at once. Nothing is saved, nothing is reported as an error, and the dialog stays open with its preview | (the button is **Close**) |
| **Escape**, or the **X** in the corner | Asks first: *"Stop making the movie?"* — **Stop and close** throws away the frames drawn so far and closes the dialog; **Keep going** carries on. Escape again, or Enter, keeps going | Closes the dialog |
| A click beside the dialog | Nothing | Nothing |

Leaving the page, or closing the tab, also ends an export; nothing is saved.

---

## When something is refused

| You see | Why |
|---|---|
| *"No trip has been tracked on this model yet."* | No trip you may read has been tracked on this survey |
| *"There is nothing to replay"* on a trip's row | Its tracking covers no stretch of time |
| *"The log could not be read"* | A chosen trip's log came back incomplete; the movie refuses a partial log, as the trip's own replay does. Untick the trip, or close and try again |
| A format greyed out | Your browser cannot write it at this size and rate — see [Which format](#which-format) |
| *"The movie could not be made."* | The encoder failed; the line under it says at which stage. Try a smaller size, or another format |

---

Related: [Live tracking and published trips](live-tracking.md) ·
[Surveys, centerlines and 3D models](surveys-and-models.md) · [Trips](trips.md) ·
[Location protection](../admin/location-protection.md)
