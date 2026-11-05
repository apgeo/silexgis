# Uploading surveys from TopoDroid

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/topodroid-uploads.md)

[← Feature reference](README.md) · Related:
[Surveys, centerlines and 3D models](surveys-and-models.md) ·
[Your account and settings](account-and-settings.md) ·
[Location protection](../admin/location-protection.md)

---

**TopoDroid** is the Android application many cavers survey with. It can send a survey straight
to SilexGIS from its export window, so the files reach the cave's page without a laptop in
between. This page is your side of that: the key the phone holds, where you watch what happens to
an upload, and the link you can hand to somebody else.

The phone-side steps (where the *SilexGIS* entry sits in TopoDroid's export window and its
settings) are described in the TopoDroid documentation for that feature; nothing here depends on
them beyond the address of this installation and the key.

---

## An access key for the phone

The phone does not hold your password. It holds an **access key**: a long random text that can do
one thing — upload surveys as you.

**Settings → Access keys.**

1. Type a **name** for the key — one per device is the useful habit, because a lost phone then
   costs one revocation and nothing else ("TopoDroid on my phone").
2. Choose how long it is **valid for**. The choices come from the installation, which also sets
   the longest life a key may have.
3. Press **Create key**.

The key is shown **once**, with a *copy* button and a warning. Paste it into TopoDroid's SilexGIS
settings straight away. Once you close the message nobody — you included — can read the key
again; the list keeps only the first characters of it, which are there to tell your keys apart.
If it is lost, revoke it and make another.

The list shows each key's name, how it starts, what it is for, when it was made, when it was last
used, when it expires and its **state**: *Active*, *Expired* or *Revoked*.

### What a key never does

- **It cannot read the rest of your account**, change anything outside survey uploads, or make
  or list other keys. Anyone who finds a key can upload surveys as you and nothing more.
- **It cannot be used to open this page.** Access keys are managed only when you are signed in
  with your password; from a device that holds a key the page refuses.
- **It is not recoverable.** The server keeps a fingerprint of it, not the text.

### Revoking

**Revoke** next to a key, then confirm. The phone holding it is refused at its next upload.
A revoked key stays in the list so you can see what was withdrawn and when.

**Changing your password does not revoke your keys unless you ask it to.** The password change
form (**Settings → Security**) and the *reset your password* page each have a switch, **Also
revoke my access keys**, off by default. Your signed-in sessions end either way; with the switch
on, every key of yours is revoked too and each phone needs a new one. Turn it on when you change
the password because you think somebody else may have had it, or a phone was lost.

An administrator who resets your two-factor sign-in revokes all your keys, always.

### Keys are personal

A key belongs to your account and acts as you. There is no club or caving-group list of keys, and
an administrator neither sees nor manages your keys on this page: each person makes and revokes
their own.

---

## What the phone is allowed to do with your uploads

**Settings → Survey uploads.**

| Setting | What it does |
|---|---|
| **Always produce** | Results you want made from every survey you upload, in addition to what the phone asks for. The choices are the results this installation can produce. With nothing ticked and nothing asked for by the phone, the installation makes its own default set, which the page names |
| **Public links for my uploads** | Whether each of your uploads gets a link to its results (see below). On by default. The phone can still ask for a link, or for none, for a single upload |
| **Default link audience** | Who a new link opens for: **Anyone with the link** (the default) or **Signed-in accounts only** — any account of this installation, asked to sign in first. The phone can choose differently for a single upload |

All three are yours alone and apply to uploads from now on.

---

## Where an upload ends up

A survey sent from TopoDroid is stored against a **cave**. If the phone names a cave, the survey
goes there (you need to be allowed to write to it). If it names none, the server makes a cave
from the survey's title.

**A cave made from the phone starts with its position unprotected**, unless the phone asks for
protection when it sends, and it is open to the audience the phone chose — which may be
*public*. If the cave's position should be protected, choose that on the phone before sending,
or protect it on the cave's page as soon as it appears.

**A cave the upload made takes its first survey as its shape.** When the upload itself created the
cave, and the cave has no other model, the first upload's Therion model becomes the cave's current
model and its centreline the line the cave is drawn with. Later versions, and uploads to a cave that
already existed, never take that place on their own: which survey draws the cave stays somebody's
choice on the cave's page. The administrator can switch this off (**Administration → Messaging →
Survey uploads**).

A cave can hold **many surveys**, and none is "the" survey. Each time the same survey is sent
again, it becomes a new **version** of it, stamped with the moment it was sent; older versions
are kept.

On the **cave's page**, a **Surveys** card lists the surveys that arrived from a device:

| Column | |
|---|---|
| **Survey** | Its title |
| **From** | The tool that sent it |
| **Versions** | How many times it was sent |
| **Newest version** | When the latest one arrived |

Open a survey to see its versions, newest first, each with its state and who sent it. Open a
version to see the files it was sent (the TopoDroid archive and any Therion files, each with a
download) and what was made from them. **Status page** opens the page described next.

When the older versions of a survey hold a lot of results — more than the installation's warning
size, 500 MB unless the administrator changed it — the card shows a warning with their size and the
setting that decides how many versions keep their results (`KeepArtefactVersions`: 0 keeps every
version's, 1 only the newest's). Only the administrator can change it; nothing is removed because of
the warning.

The card appears only on caves that have had an upload, and **only for people who may see the
cave's position exactly**: for everyone else the section is simply not there, like a cave with
no uploads. The *Survey sources* card and the list of models lower down the page are the cave's
whole set; the Surveys card says which upload each piece came from and does not repeat them.

---

## The status page of an upload

Every upload has a page of its own, reached from **Status page** on the cave, from the
notification you get when the run ends, or from the link the phone shows.

- **A progress bar** with the percentage, and **the stages** of the run, each with its own
  state — *Archiving*, *Reading the archive*, *Preparing the compilation*, *Compiling*,
  *Storing the results*, *Reading the results*. A stage the run did not need is marked *Not
  needed*, and a run that stopped early leaves the later stages plainly unvisited.
- **What the server last said**, and, if the run failed, **the error** with its code. The code is
  worth quoting when you ask for help.
- **The log** — shown only to people who may edit the cave and see its position exactly.
- **The files sent**, each with a download.
- **What was produced**, with a download for each and, where SilexGIS has a page for it, a way to
  open it.
- **The public link**, with who it opens for, *Copy*, *Make a new link* and *Withdraw*. Everyone
  who may see the upload sees the address.
- **A size warning**, as on the cave's card, when the survey's older versions hold a lot.
- **Run again**, for people who may edit the cave, once the run has ended.

The page asks again every few seconds while the run is going and stops when it ends.

**If you lose the right to see the cave's exact position** after sending, you still see how your
own upload went — its progress, stages, message and error — but not what would place the cave:
the downloads, the log, the files you sent and the public link's address are left out.

**An installation without a compiler** still receives and reads the files: the run finishes at
100 % and says, in a sentence, that nothing was compiled. That is a note on a success, not a
warning. Whether an installation compiles at all is the administrator's decision.

### There is no "delete this upload" yet

A finished upload cannot be taken back from this page: the files it brought are archived against
the cave the moment they arrive, and removing them is done where they are listed — the cave's
*Survey sources*.

---

## The page of results

Once a run has ended, **View results** on the status page — and on the public link's page — opens
one page with everything the survey produced, at the same address with `/view` added. It works at
phone width too.

- **At the top**, the survey's title and cave, **Copy link**, and a way into SilexGIS. *Copy link*
  copies the upload's public link when it has one — anybody holding it can open the results — and
  otherwise this page's own address, which opens only for accounts allowed to see the upload. On the
  signed-in page the button opens the cave; on a public link's page a signed-in reader who may see
  the upload gets **Open in SilexGIS**, and anybody else is offered to sign in.
- **The 3D model**, in two tabs: the embedded viewer, and the same file in LochViewer (on trial).
- **Map and geodata**: when the survey was placed in the world (it has a fixed point or a coordinate
  system), the centreline to download as **KML**, **GeoJSON** or **GPX**, and a map with the
  centreline — on the signed-in page over the installation's default background (OpenTopoMap when
  there is none), on a public link's page over OpenTopoMap. A survey that could not be placed says so
  instead.
- **Figures from the compiler**: surveyed length, length after loop adjustment, plan and vertical
  length, depth, north–south and east–west extent with the station at each end and its height,
  northing or easting, stations, shots, loops and the average loop error, the coordinate system and
  the magnetic declinations used, a chart of each loop's closure error and a table of the loops with
  the stations they run through. When the survey has no fixed point the page says its heights and
  coordinates are relative.
- **This upload's survey analysed**: statistics, network, orientation and depth profile, measured over
  this upload's own survey — not over the survey the cave is currently drawn from, which can be
  another upload. A survey that could not be placed, or whose reading has not finished, has nothing to
  analyse, and the page says so.

**What a public link shows.** A public link's page shows everything above: the map, the downloads
and the figures with their station names, heights and grid coordinates. Whoever holds the link can
already download the compiled model and the compiler's log, which contain all of it. It shows only
what comes from this upload's own files — never the cave's recorded entrance position or anything
else from the cave's record, which stay protected as everywhere else in SilexGIS. Withdrawing the
link, or protecting the cave when the link was made without acknowledging protection, closes all of
it at once. Inside SilexGIS the same holds: anybody allowed to open and download this upload's sent
files sees the map, the downloads and the figures. The analyses are never compared with the length
and depth recorded for the cave: an upload may cover only part of the cave, or be an older or newer
partial survey.

---

## The public link

When the switch under **Settings → Survey uploads** is on and the installation allows it, a
finished upload gets a link of the form `…/p/survey/…`. The phone may also ask for a link, or for
none, for a single upload. Each link has an **audience**:

- **Anyone with the link** — opens **without an account**.
- **Signed-in accounts only** — whoever opens it is asked to sign in with an account of this
  installation, then sees the page. No right on the cave is needed beyond holding the address.

Whoever opens it sees:

- the survey's title, the **name** of the cave, and when the upload was sent;
- the same progress bar and stages;
- the results that can be opened or downloaded;
- **the compiler's log**: its last lines on the page, and the whole log to download — after a
  compile that failed, where it says what went wrong, as after one that worked.

### What the public link never shows

- **The TopoDroid archive or the Therion source files.**
- The team.
- **Where the cave is.** The page names the cave and gives no position of its own. The results
  themselves — the model and the drawings — and the compiler's log, which can name stations and
  repeat the survey's fixed point, are for whoever holds the link: that is what the link is for.

### What the link is, and is not

- **It is a secret address.** Treat it as you would a password-less share: whoever holds it can
  see the results. **Withdraw** it on the status page and it stops working at once; **Make a new
  link** replaces it and the old address stops working too.
- **A cave whose location is protected gets a link only when you ask for it explicitly.** The
  results — the 3D model and the drawings — show where the cave's passages are. On the phone you
  ask for it for that one upload; on the status page, **Make a link** shows the same warning and a
  box to tick, *I understand, and I want a link to this protected cave's results*. No setting does
  this for you. A link made that way is marked *Protected cave — linked knowingly*, records who
  asked and when, and keeps working while the cave stays protected. An ordinary link stops
  answering at once if the cave becomes protected after it was made.
- When there is no link the status page says why where the server gives a reason — your switch,
  the upload asked for none, the installation's setting, no right to share the cave, or the cave's
  protection. **Make a link** lets you choose the audience, and the phone's result screen offers
  *Make a new link* and *Withdraw link* too.
  **Open** next to the link shows what a visitor sees.
- A wrong, withdrawn or no-longer-permitted link says only "This link does not work". Nobody can
  tell from the answer which of the three it was. A link for signed-in accounts, opened without
  signing in, asks the visitor to sign in instead.

---

## Things that go wrong, and what they mean

- **"Access keys can only be managed when you are signed in with your password"** — you opened
  the page from a device that holds a key. Sign in normally.
- **The phone is refused** — the key is revoked, expired, or was copied incompletely. Make a new
  one.
- **The Surveys card is missing from a cave you know has uploads** — you may not see the cave's
  position exactly. See [Location protection](../admin/location-protection.md).
- **"This upload was not found, or you may not see it"** — the address is wrong, or the cave is
  not open to you. The two are not told apart on purpose.

---

[← Feature reference](README.md) · [Surveys, centerlines and 3D models](surveys-and-models.md) ·
[Your account and settings](account-and-settings.md)
