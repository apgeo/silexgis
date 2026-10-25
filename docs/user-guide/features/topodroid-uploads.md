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
an upload, and the link you can hand to somebody who has no account.

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

---

## What the phone is allowed to do with your uploads

**Settings → Survey uploads.**

| Setting | What it does |
|---|---|
| **Always produce** | Results you want made from every survey you upload, in addition to what the phone asks for. The choices are the results this installation can produce. With nothing ticked and nothing asked for by the phone, the installation makes its own default set, which the page names |
| **Public links for my uploads** | Whether each of your uploads may get a link that shows its results without signing in (see below). On by default |

Both are yours alone and apply to uploads from now on.

---

## Where an upload ends up

A survey sent from TopoDroid is stored against a **cave**. If the phone names a cave, the survey
goes there (you need to be allowed to write to it). If it names none, the server makes a cave
from the survey's title.

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
- **The log** — shown only to people who may edit the cave.
- **The files sent**, each with a download.
- **What was produced**, with a download for each and, where SilexGIS has a page for it, a way to
  open it.
- **The public link**, with *Copy*, *Make a new link* and *Withdraw*.
- **Run again**, for people who may edit the cave, once the run has ended.

The page asks again every few seconds while the run is going and stops when it ends.

**An installation without a compiler** still receives and reads the files: the run finishes at
100 % and says, in a sentence, that nothing was compiled. That is a note on a success, not a
warning. Whether an installation compiles at all is the administrator's decision.

### There is no "delete this upload" yet

A finished upload cannot be taken back from this page: the files it brought are archived against
the cave the moment they arrive, and removing them is done where they are listed — the cave's
*Survey sources*.

---

## The public link

When the switch under **Settings → Survey uploads** is on and the installation allows it, a
finished upload can have a link of the form `…/p/survey/…`. Anyone who has it can open it **without
an account** and see:

- the survey's title, the **name** of the cave, and when the upload was sent;
- the same progress bar and stages;
- the results that can be opened or downloaded.

### What the public link never shows

- **The TopoDroid archive or the Therion source files.**
- **The log**, the team, the station names.
- **Where the cave is.** The page names the cave and nothing about its position.

### What the link is, and is not

- **It is a secret address.** Treat it as you would a password-less share: whoever holds it can
  see the results. **Withdraw** it on the status page and it stops working at once; **Make a new
  link** replaces it and the old address stops working too.
- **A cave whose location is protected gets no link at all**, and one that becomes protected
  after a link was made stops answering at once. When there is no link the status page says why where the server
  gives a reason — your switch, the installation's setting, no right to share the cave, or the
  cave's protection — and offers no *Make a link* button when the cave's protection is the reason.
  **Open** next to the link shows what a visitor without an account sees.
- A wrong, withdrawn or no-longer-permitted link says only "This link does not work". Nobody can
  tell from the answer which of the three it was.

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
