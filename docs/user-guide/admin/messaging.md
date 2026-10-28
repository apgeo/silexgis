# Messaging and delivery

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/messaging.md)

[← Administration](README.md) · Related:
[Notifications](../features/notifications.md)

---

**Administration → Messaging** configures where email and text messages go, what signing in
requires, and a handful of other installation-wide answers. **Administration → Message
delivery** shows whether any of it is actually working.

> These can also be set from the deployment's environment, and the page says so. Setting them
> here is the route that does not need a restart.

The page has eleven tabs:

| Tab | Decides | Below |
|---|---|---|
| **Email** | The mail server | [Email](#email) |
| **Text messages** | The SMS gateway | [Text messages](#text-messages) |
| **Sign-in policy** | Confirmed addresses, which second factors are offered, how long a code lives | [Sign-in policy](#sign-in-policy) |
| **Location protection** | Two switches about how far protected places and the club's dates travel | [Location protection](#location-protection) |
| **Vector import** | Whether an import may skip its review, and how duplicates are looked for | [Vector import](#vector-import) |
| **Phone sync** | What the phone app is given and how long unused files wait | [Phone sync](#phone-sync) |
| **Notifications** | How long notifications are kept, and what an announcement may cost | [Retention](#retention) · [The money guard](#the-money-guard) |
| **Photo libraries** | Stopping and restarting the use of a photo library the installation already has | [Photo libraries](#photo-libraries) |
| **Survey uploads** | Whether a cave made by a survey upload takes that upload's first model as its shape | [Survey uploads](#survey-uploads) |
| **Workspace** | Which arrangement of the map workspace a new account starts with | [Workspace](#workspace) |
| **Maps in documents** | Which map backgrounds a document may copy | [Maps in documents](#maps-in-documents) |
| **Own basemap** | The installation's own vector basemap: what is served, and downloading a region | [Own basemap](#own-basemap) |

---

## Email

| Field | |
|---|---|
| **Enabled** | |
| **Server** · **Port** | |
| **Connection security** | Automatic · TLS on connect (465) · None (plain text) |
| **Username** · **Password** | The page says whether a password is stored; leave blank to keep it |
| **Sender address** · **Sender name** · **Reply-to address** | |
| **Timeout** | Seconds |
| **Accept an invalid certificate** | *Only for an internal relay with a self-signed certificate.* It removes the protection on the path |

Status is stated plainly: **"Email is configured"**, or:

> *"No mail server configured. Messages are written to the server log instead of being sent."*

**Configure nothing and the application still runs.** Sign-in links, confirmation links and
codes go to the server log. That is a legitimate way to run a small installation — as long as
whoever needs a link can read the log.

## Text messages

Any SMS gateway that speaks HTTP. Same shape: configured, or *"Texts are written to the
server log instead of being sent."*

### The money guard

An installation can allow **announcements** to travel by text. It is **off unless switched
on**, one person cannot send them in a stream, and a day's spending has a **ceiling** an
administrator sets and can see on the delivery page.

> The ceiling counts **the segments a carrier bills**, not messages. **One diacritic makes the
> same wording cost two of them** — so a ceiling means roughly **half as many messages to
> members who read Romanian as to members who read English**.

Set the ceiling with that in mind, and tell whoever writes announcements.

## Sign-in policy

What signing in requires — including which two-factor methods this installation offers.

| | |
|---|---|
| **Send a confirmation message when an account is created** | |
| **Require a confirmed address to sign in** | An unconfirmed account is refused at sign-in and offered a new confirmation message. *"Has no effect until a mail server is configured — otherwise nobody could ever confirm."* |
| **Allow authenticator apps** · **Allow emailed codes** · **Allow texted codes** | Emailed codes need a mail server, texted codes an SMS gateway. *"The weakest of the three: a number can be moved to another SIM by persuading the mobile operator."* |
| **Code lifetime (minutes)** · **Wait between codes (seconds)** | For emailed and texted codes |

Methods not enabled here are marked *"Not enabled on this installation"* on members' security
pages.

The same codes are asked for after a sign-in through an external provider: a provider proves
who somebody is at the provider, not that they hold the account's second factor.

## Location protection

Two switches, both described in full under [Location protection](location-protection.md):

**"Show attachments of protected caves to everyone who can read them."**
([more](location-protection.md#the-attachment-association-setting))

Documents attached to a protected cave are **always served** — they are never hidden or
stripped out. What this switch controls is whether the *association* between the document and
that particular cave is disclosed.

Turning it on **never reveals a position**: following the link still gives the protected view.

**"Let members subscribe to their calendar from outside."**
([more](location-protection.md#the-calendar-feed-setting)) Off by default. On, each member can
create secret addresses a phone or desktop calendar polls without signing in; switching it off
again stops every address already handed out. See
[Subscribing to your calendar](../features/calendar-feed.md).

## Vector import

An imported file is [reviewed](../features/geodata.md#imports) before anything is created. This
tab is where that step can be removed, and how far duplicate detection looks by default.

| | |
|---|---|
| **Let a trusted user create objects without reviewing them** | Off by default. Turning it on never widens who may create — the importer still needs the right to create features — it only removes the step in between |
| **Look for something already there within** | The starting radius for a new review; the importer can change it. Only objects whose exact position the reviewer may see are ever measured against |
| **Names count as the same above** | How alike two names must be, 0 to 1, before a nearby object is called the same thing rather than merely close to it |

## Phone sync

What this installation does with the SpeleoLoc phone app (see [Mobile and offline
sync](../features/mobile-sync.md)). A change applies at once to every phone that syncs. Saving
replaces all five settings together.

| | |
|---|---|
| **Give phones the list of people** | On by default. A phone can download a list of cavers — names only, never an address or a phone number — so its user can say who they are and who was on a trip. Off: phones get no list and cannot say which caver one of their users is. Mappings already made on the [Phone users](../features/people-and-clubs.md#phone-users) page stay, and what a phone records about its own users is still kept |
| **Whose names the list holds** | *Cavers in the phone account's caving groups, and the account's own caver* (the default) or *Every caver on the roster*. A phone can only say its user is somebody it was shown, so this also decides whom a phone may name. *Every caver* shows the whole roster to every account that syncs a phone, whatever their group; choose it only when the roster is no secret from the people who sign in |
| **Make trips from recordings that have problems** | On by default. A trip recorded on a phone is turned into a trip report by itself. On: this also happens when some of it could not be matched, such as a station nobody can place or a phone user not mapped to a caver; the trip is flagged as needing attention and says why. Off: such a recording waits on the cave page until somebody converts it by hand |
| **Keep photos sent ahead of an upload for (days)** | 7 by default, from 1 to 365. A phone sends the bytes of a photo before the upload that uses them; if no upload claims them within this many days they are deleted. A change applies to files that arrive after it. Waiting files count against the sending account's storage, so allow room for about this many days of a phone's photographs |
| **Keep the files of a turned-down upload for (days)** | 30 by default, from 1 to 365. When an upload held for review is decided without its files being used — turned down, replaced by a later one, or accepted without them — the files are deleted after this many days. While an upload still waits for a reviewer its files are kept however long that takes |

The same five settings can be given a starting value in the deployment's environment; a value
saved here wins over it.

## Photo libraries

For an installation that runs a photo library beside it, each library has one switch: **Stop
using this photo library**. While it is on, nothing in the application asks the library
anything — no map layer, no browsing, no search and no pictures. It changes nothing about what
the library itself holds or who can sign in to it directly.

The switch cannot *connect* a library: which libraries an installation has is decided when it is
deployed, and the page says so — or says that there is nothing to stop.

## Survey uploads

The **Survey uploads** tab holds choices about surveys sent from a field device such as TopoDroid
(see [Uploading surveys from TopoDroid](../features/topodroid-uploads.md)).

**A cave made by an upload takes its first model** — on unless switched off. When an upload
itself created the cave, and the cave has no other model and no centreline yet, the Therion model
the upload produced becomes the cave's current model and its centreline the line the cave is drawn
with, so a cave made from the phone is not shapeless until somebody chooses. Later versions of the
survey, and uploads to a cave that already existed, never take that place on their own. Off, a
model from an upload never becomes current by itself; somebody chooses it on the cave's page.

Two related choices are not on this tab: how many versions of a survey keep the results their runs
produced (`SILEXGIS__SurveyUploads__KeepArtefactVersions` — 0 keeps every version's, 1 only the
newest's) and the size at which the cave's *Surveys* card and the status page warn that older
versions hold a lot (`SILEXGIS__SurveyUploads__OlderVersionsWarnMegabytes`, 500 MB by default).
Both are set in the deployment's environment; the tab names them.

## Workspace

The **Workspace** tab chooses the **starting arrangement**: which of the map workspace's
arrangements — *Map*, *Map + 3D*, *Survey review* or *Trip planning* — a new account starts with
(see [Panes and arrangements](../features/map-workspace.md#panes-and-arrangements)). It is applied
once, the first time an account that has arranged nothing opens the map on a desktop; from then on
the arrangement is the member's own, and nobody who has already arranged their workspace is moved.
*Two screens* is not offered: it opens a second window, which a browser allows only when somebody
presses something. *Map (as shipped)* removes the choice.

The choice is kept in the installation's starting arrangement, the same document as the panels'
default sections, which this tab leaves as it is.

## Maps in documents

The **Maps in documents** tab decides **which map backgrounds a document may copy**. A trip's
[downloaded write-up](../features/trips.md) carries a map of the trip drawn over one background:
the default background if it is switched on here, otherwise the first one that is.

Every background this installation publishes is listed with **one switch**.

| Column | What it shows |
|---|---|
| **Background** | The source, the group the catalogue files it under, and the credit written under the picture |
| **In documents** | The switch |
| **As shipped** | What the installation shipped with — and *Decided here* where an administrator chose otherwise, with **Use the shipped answer** to take the decision back |

- **Switching a background on is a statement about its terms.** Showing a map on screen and
  putting it into a file that is then mailed and printed are different uses, and each provider
  decides the second separately. Switch a source on only after reading its terms; the group it is
  filed under is shown for that reason.
- A decision takes effect **at once** and is **kept across restarts and upgrades**. The shipped
  answer goes on applying to every background nobody has decided about.
- A source with **no credit** cannot be switched on: there would be nothing to write under the
  picture.
- With **nothing switched on**, a write-up still gets its map — drawn on a plain ground, which
  the picture says under itself. The page warns when that is the case.
- Each decision is recorded in the [audit trail](../features/history-and-audit.md).

Which backgrounds exist at all, and their addresses, stay in the installation's catalogue file —
see the install guide.

## Own basemap

The **Own basemap** tab manages the installation's **own vector basemap**: OpenStreetMap shapes of
a region, served by your installation alone and drawn on the map as the background *Self-hosted
basemap (light)* — sharp at every zoom, with labels in each reader's language, and no request to
anybody else's server.

> An OpenStreetMap vector basemap served from this installation alone: download a region of a
> Protomaps planet build, and the map backgrounds drawn from it are offered on the map from then on.

| Part | What it shows |
|---|---|
| **Served now** | The build the basemap was cut from, its size, when it was downloaded, the region and the last zoom level |
| **Latest download** | Queued, running (with how far it has got and **Cancel download**), finished, failed or cancelled — and why it failed, in words |
| **Download a region** | **Romania**, **Apuseni (karst region)** or **A box of my own** (west, south, east and north, in degrees), the **last zoom level** (15 is the deepest), and the **build** — the latest by default |

- **Until the first download, the background is not offered on the map at all**, and the page says
  which backgrounds are waiting for one.
- **Estimate size** first: it works out what the download would weigh without fetching it. Romania
  to zoom 15 is about 1.3 GB, the Apuseni box about 36 MB. A region larger than your installation
  accepts, or one the disk has no room for, is refused before anything is downloaded.
- **One download at a time**, in the background: you can leave the page and come back. A new
  download **replaces** the basemap being served only once it has finished; until then — and if it
  fails or is cancelled — the current one goes on being served unchanged.
- The tiles have no contours or relief: keep the topographic backgrounds for reading karst.
- Where the region is cut from is set in the installation's configuration, not on this page: you
  choose a date and a box, never an address. If the page says self-hosting is switched off, the
  installation runs no tile server for an own basemap — see the install guide.
- Each download asked for, and each one cancelled, is recorded in the
  [audit trail](../features/history-and-audit.md).

---

## Message texts

**Configuration → Message texts.** The wording of every message, editable per language.
See [Vocabularies](vocabularies.md#configuration--message-texts).

---

## Watching the mail go out

**Administration → Message delivery.**

The numbers at the top:

- how many messages are **waiting**,
- how many are **held for a summary**,
- how many have been **given up on**,
- and the one that matters: **how long the oldest one that has not left has been waiting.**

Below, the ones worth looking at: what each was about, **who it was for — under the name they
may be shown under and never their address** — and what the far end said, with anything
address-shaped taken out of it.

### Putting one back by hand

A message that has given up can be retried. Retrying **asks every question again before it
sends**:

- somebody who has since said they do not want that kind of message **does not get one**,
- somebody who asked for a daily summary **gets it in their summary**,
- and **every such act is recorded against the person who made it**.

So a retry is not a replay. It is a fresh send under current rules.

### Retention

How long notifications are kept — **Keep notifications for (days)**, on the **Notifications**
tab — is a number an administrator can change, over whatever the deployment configured. A
notification is deleted after it, read or unread alike, and the record of how it was sent goes
with it.

---

## The quiet window

An installation can set a window during which **nothing leaves the server** — read in **each
recipient's own time zone**, so it means the same hour of the night whatever the season.

What is waiting sits in members' in-app lists the whole time, because a list wakes nobody.
Warnings a member may not switch off ignore the window — as does the overdue-party alarm.

---

## Announcements

Whoever is entrusted with it can write one line to a caving group. See
[People and clubs](../features/people-and-clubs.md#announcing-to-a-club).

> **Being allowed to edit a club's roster is not the same as being allowed to write to
> everyone on it.** The two rights are granted separately.

---

## Troubleshooting checklist

| Symptom | Look at |
|---|---|
| Nobody is getting anything | Is email *Enabled*? Does the page say *"written to the server log"*? |
| Some people get it, some do not | Their own [notification grid](../features/notifications.md) — a member may have switched that kind off |
| Messages queue and never leave | The **oldest waiting** figure on Message delivery, and what the far end said |
| Texts stop partway through a day | The **daily ceiling** — and remember it counts segments |
| Somebody says a message named a cave | It cannot have. Messages never name caves. Check whether they mean the *page* they followed the link to |

---

Related: [Notifications](../features/notifications.md) ·
[People and clubs](../features/people-and-clubs.md)
