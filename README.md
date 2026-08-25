# SilexGIS

Web application for storage, viewing and editing of cave / karst topographic, geographic
and other associated data. Built for caving clubs, researchers and individuals to self-host.

**v3 (2026):** full rebuild on a modern stack —
ASP.NET Core minimal API (.NET, LTS) · PostgreSQL/PostGIS · EF Core + NetTopologySuite ·
React + TypeScript · Ant Design · OpenLayers · Docker-first deployment.

Previous versions:
- **v1** (PHP/MySQL/OpenLayers 3) — live at [speosilex.ro/silexgis](https://speosilex.ro/silexgis/en/index.php)
- **v2** (2022, React/Laravel, partial) — archived on the [`v2-archive`](../../tree/v2-archive) branch

## Features

- **Cave registry** — caves, entrances with precise coordinates, taxonomies, morphometry,
  discovery data, and protection of sensitive locations.
- **Map workspace** — OpenLayers map with base layers, cave/feature/geofile overlays,
  geometry editing with snapping and undo, measurement, and search (internal + Nominatim).
- **Surface features, files & media** — typed symbols and properties, photo galleries,
  documents, audio and video, and georeferenced raster (COG) map overlays. A document has a
  kind (survey report, permit, trip report, …) and each kind decides which details its
  documents are asked for, so an archive can be organized the way its club actually files
  things. Uploads are accepted up to 512 MB by default, and the limit is a setting. The text
  of an uploaded document is read in the background — PDF, Word, Excel, PowerPoint, LibreOffice,
  Rich Text, plain text, Markdown and CSV, including the pre-2007 Word and PowerPoint files a club
  archive is full of and files written in the older Central European code pages Romanian archives
  are full of. A password-protected document is reported as locked rather than read into nonsense.
  Nothing recognises text in a photograph, so scanned
  pages and image-only PDFs have no text to read, and the document panel says exactly that
  instead of leaving you waiting for words that are never coming.
- **Search inside documents** — the same search box that finds caves and trips also finds
  documents by what is written in them, quoting the sentence that matched. Searching is
  accent-insensitive in both directions: "pestera" finds "peșteră", and the result is quoted back
  spelled the way its author wrote it. Words are stemmed in the language a document is written in,
  Romanian and English out of the box and any language PostgreSQL has a stemmer for by adding one
  row. The language is worked out from the document's own text when it is read — and left unset
  rather than guessed when the text does not say clearly — and anyone who may edit the document
  can correct it, which re-indexes it on the spot.
  You only ever find documents you are allowed to read, and replaced versions of a document
  are searchable only by the people who could replace it — a paragraph removed in a new version
  does not stay findable in the old one.
- **Read a document without downloading it** — a document has its own page, saying what it is,
  which version is current, where it is filed and how far reading its text has got, and showing
  the document itself: PDFs a page at a time as pictures drawn by the server, photos, plain text
  and Markdown, and audio and video played in the browser. A search hit opens that page at the
  page the phrase was found on. Nothing is transcoded and no scanned page is invented: where a
  format cannot be shown, the codec is not one your browser plays, or the file simply holds no
  text, the page says which of those it is and offers the download — you are never left looking
  at a blank panel wondering. It works on a phone, and someone allowed to read a protected cave's
  report can read it there without ever being able to download the file.
- **Talk about a document where it lives** — every document page carries a discussion: who
  recognises the cave in an unlabelled 1987 photograph, which survey superseded which. Replies go
  one level deep, so a thread stays readable; the person who wrote a remark can correct it, and
  they or an administrator can take it down. Whoever may read the document may join in — and
  nobody else can even tell the conversation is there, because a remark is only ever reachable
  through the document it sits on. Remarks are plain words, shown as the words that were typed.
- **Cabinets** — a filing tree documents live in ("Club archive / Bulletins / 1987"), and the
  unit permissions are granted on: one rule can hand a committee the whole archive instead of
  one rule per document. A document can sit on several shelves at once, so there is no
  move-versus-copy question and nothing is orphaned by belonging in two places. Filing a
  document moves who can read it, and the app says so where the control is.
- **One model for everything on the map** — caves, entrances, centerlines and surface
  features share a single feature model, so any of them can contain another (a karst area
  holding caves, a cave holding its entrances) and be linked to another by a named
  relation. Containment drives breadcrumbs and inherits location protection downwards.
- **Links between anything, not just between features** — a cave and the report that describes
  it, a trip and the photographs it produced, two records that turned out to be the same hole:
  a link names two or more things of any kind — features, documents, trips, cavers, clubs,
  cabinets, 3D models, saved views — and says how they are related. The wording comes from a
  list ("Contains", "Documented by", "Duplicate of", …) that an administrator can extend with
  whatever this archive actually says. A relation that reads one way reads correctly from both
  ends, so a single link says "Contains" on one page and "Contained in" on the other. A link
  can point at a part rather than the whole — a page or a run of pages of a document, a moment
  or a stretch of a recording; other kinds of part (a survey station, a passage of text) are
  recorded and shown, but choosing one needs a viewer that can select it, and those arrive with
  their viewers. A link can also name a spot on the map that had no record yet, marking the
  point as the link is written. Every link has a short address of its own to paste into a
  message or a report, and it shows each reader only the ends they are allowed to see: a link
  is never the thing that discloses a protected cave.
- **Vector import/export** — GPX, KML, KMZ, Shapefile, GeoJSON, WKT, and a spreadsheet of
  positions in CSV. A file drawn on the map is only half of it: a review screen turns the
  waypoints in it into caves, entrances and surface features, proposing what each one is from
  the club's own naming habits — a waypoint called `P. Ursilor`, `Aven`, `Izbuc` or `Doline` is
  recognised, and the term is taken back out of the name. Nothing is created until you confirm,
  the review survives closing the tab, each candidate says whether there is already something
  within fifty metres, and the whole confirmation comes back out again in one press if the
  mapping turns out to have been wrong. The rules are yours to edit, and to send to another club
  as a file.
- **Photographs become the places they show** — drop a trip's worth of pictures and the ones taken
  at the same hole arrive as one candidate with a gallery, not as forty points to reject one at a
  time. Each place says how it was placed and how much that is worth: the fix the camera recorded,
  a position worked out by matching the picture's clock against a track you walked (with a
  camera-clock offset, because a camera left on the wrong zone is out by hours while looking
  perfectly plausible), or one you gave it by dragging it onto the map. It offers what is already
  in the registry nearby, so filing a picture on the cave it belongs to is one press. Nothing is
  created until you confirm, and the whole confirmation — including the pictures it hung on caves
  that were already there — comes back out again in one press. A picture that recorded which way
  the camera was facing draws that on the map, which is what turns "somewhere on this slope" into
  a hole you can walk back to. Nothing is ever written back into the file itself.
- **3D survey models** — Therion `.lox` / Survex `.3d` via CaveView.js, plus cave
  centerlines projected on the map.
- **3D view** — the configured base layers draped on a globe, on a page that is downloaded
  only when it is opened. Needs WebGL 2; a browser without it gets an explanation rather
  than a dead canvas. No vendor terrain, imagery or geocoding service is contacted and the
  3D engine is served by your own installation; the basemap is whichever base layers you
  configured, so an air-gapped install needs one it can reach.
- **Real relief, if you want it** — that globe is a smooth sphere out of the box, needing no
  elevation server and nothing downloaded. An operator who wants the caves under actual
  hillsides bakes free elevation data into a tile pyramid in one documented step and serves it
  as static files from the same installation. Everyone who does not is unaffected.
- **Trips** — a trip is logged over the days it actually ran, with who was there, what came of it
  and how long was spent underground, and it can be sketched on a map: a point, a line or an area
  for where it happened. That sketch is shown exactly to everyone who may read the trip, including
  on the trip map, so the editor says so beside it — a sketch dropped on a protected entrance
  discloses that entrance, whatever protection the caves the trip names carry. A trip starts as a
  **draft**: you can write it up over several sittings without anybody being told, and publishing it
  is what announces it to the people who were there — and only to those of them who may actually
  open it. A draft is not a hidden trip, though: whoever the trip's visibility admits can read it
  from the moment it exists. A trip's page also records **what it did to what it names** — the
  areas worked in, the passages surveyed, dug, photographed or discovered, the leads left for next
  time — each as a labelled field you fill in as the trip earns it, rather than a form of empty
  boxes to stare at. A work area shows what contains it, so two passages of the same name are told
  apart. What one person records, everybody who may maintain the thing it is about can correct. Those
  fields are also the *only* way a trip is tied to a place — there is no separate cave list to keep in
  step — and a cave's own page returns the favour with a **Trips** section listing the trips that named
  it, newest first, counted by the same rule that decides what the list may show. A trip **names only the
  caves its reader is allowed to open**, and tells them how many it is not naming rather than leaving a
  list quietly short — on the page, in the report and in the document it generates — because naming a
  cave, even by its bare identifier, is as good as showing it to somebody a trip invited in. **What a trip was
  for is a list your club owns**, not a list we chose: the eight kinds we ship are there from the
  start and cannot be renamed out from under the software, but a club that runs something nobody
  thought of adds it themselves, and it is filterable and countable from that moment. Each kind also
  decides **what a report of that kind asks for** — three sections, field data, logistics and safety,
  drawn from a form the club describes once and can change later without invalidating a single report
  already written. Beside them a trip records the figures worth counting — how deep, how far surveyed,
  how many stations, how much rope — in metres, once, shown in your own language's numbers. It also
  records **whether anything went wrong**, which anyone who may read the trip can see and search for,
  while the account of *what* went wrong is shown only to the people who may correct the trip: a
  near-miss names somebody's mistake, and it is not gossip for the whole club. **Who was there also
  says what they did**: the person who led it, drove, surveyed, took the photographs, was being
  trained or sat by a phone as the callout contact — from a list your club owns and extends, with
  attending and proposing there from the start and not removable. Somebody who did two jobs is
  recorded doing both rather than made to choose. Each person can also carry their own hours, for
  the one who came out early or stayed on, and a line of their own — *turned back at the pitch
  head* — so nobody has to invent a job to record a circumstance. That detail is part of the trip
  and goes no further than the trip does: you cannot search for the trips somebody led, because that
  is a question about a person, assembled out of records the asker may never be allowed to read.
- **What it all adds up to** — a person, a cave and a club each get their totals: trips, hours
  underground, metres surveyed, first visits, how many people, how many trips had an incident. Every
  one of them is counted **over the trips you may read**, and the screen says so, because two people
  with different access legitimately see different totals for the same person and both are right.
  Nothing is stored: hours are worked out from the times recorded, over however many days the trip
  really ran, and a first visit is simply the earliest trip that took somebody somewhere — so typing
  up an older trip from the archive corrects the figures instead of leaving a stale flag behind.
  Any of the three can be saved as a spreadsheet, which carries exactly what the screen carried and
  says whose totals they are, because a file gets forwarded and read months later.
- **A trip that has not happened yet asks people, and the answers keep their own order** — a trip's
  page has a list of who was asked and what each of them said: coming, not coming, or not answered
  yet, with a line of their own beside it. Whoever may read the trip answers for themselves; whoever
  may correct it answers for somebody who phoned in. If the trip has a number of places, the list
  says how many are taken and how many are waiting, in the order people said yes — first come, and a
  change of mind goes to the back, because that is what everybody assumes is happening and the
  alternative has to be explained. The organiser can still hand a place to somebody further down,
  and that pick does not outlive the answer it was made about. Nobody is written into the trip's
  roster by saying yes: once the trip has happened, one deliberate act turns everybody holding a
  place into people who were there, and it tells nobody, because everybody it writes in asked to be
  there and was told when they were asked.
- **A plan tells the people it concerns, and can be told to stop** — being asked onto a trip, a trip
  you are on changing or being moved along, and a trip being called off all send an email, and every
  one of them goes only to somebody who may read that trip, decided for each person separately at the
  moment it is sent. A message about a trip names the trip and its date and **never a cave**, because
  an email is as much a copy of a protected location as anything on the screen is. A draft tells
  nobody, since telling you a draft changed would be telling you it exists, and a write that changed
  nothing sends nothing. All of it is a switch on the notifications page, on to begin with, and off
  the moment you say so.
- **Somebody answering you, or leaving a remark on something of yours, tells you** — an answer to
  your comment and a comment on a document you uploaded are two separate things to be told about, so
  switching off the traffic on a busy document never costs you the answers meant for you. You are
  never told about your own remark, and if it is both an answer to you and a remark on your own
  upload you hear about it once. Whether you may still read the document is decided for you
  personally at the moment the message is written, so somebody who has lost access to it is told
  nothing. **No message ever repeats what was said** — it names the document and links to it, and you
  open it and read the conversation there. If the remark itself is deleted the link still takes you to
  the right page; if the document goes, the line in your list says the subject is no longer available
  rather than sending you to a dead link.
- **You choose what you are told about and where you are told it** — the notifications page is a
  grid: the kinds of thing that happen down one side, the ways of reaching you across the top. For
  each pair you say nothing, as it happens, or once a day in a single summary — and a daily summary
  of something you are not told about by mail is not a thing you can accidentally ask for. Warnings
  about your own account cannot be switched off, and the page says so. Switch every way of reaching
  you off for one kind of event and that is allowed, but the page tells you plainly that it will now
  reach you nowhere. A way of reaching you that this installation has not set up is marked as such,
  and what you chose is kept for the day somebody sets it up. There is also a quiet window an
  installation can set — nothing leaves the server during your night, read in your own time zone, so
  it means the same hour of the night whatever the season; what is waiting is in your list the whole
  time, because a list wakes nobody, and a warning you may not switch off ignores the window.
- **A link in an email speaks only for email** — clicking "stop sending me this" in a mail client
  switches that kind of message off for mail alone and leaves your inbox in the application exactly
  as it was, and the page you land on says which of the two it changed.
- **Everything that was sent to you is also a place you can go** — a bell in the top bar counts what
  you have not read yet, and the inbox behind it lists what happened, newest first, filterable by the
  kind of event, marked read as you open it or all at once. A line is worded by the installation, in
  the language you are reading the site in, so wording an operator has rewritten is the wording you
  see. And a line about something you can no longer open says exactly that, in place of a name and a
  link: you are still told that it happened — hiding that would be its own kind of leak — but nothing
  about the thing itself survives the fact that you lost access to it.
- **A club can be written to, once, and it reaches each member the way they chose** — somebody
  entrusted with it can send one line to a caving group, and every member with an account gets it in
  their own list, by their own choice of mail or application, rather than as a mailing list nobody can
  leave. Being allowed to edit a club's roster is not the same as being allowed to write to everyone on
  it, and the two are granted separately; a club's founder can write to their own club from the start.
  Before you send it you are told how many people it reaches, and confirming is a step of its own, so a
  message to two hundred people is never one careless click. An installation can also say that
  announcements may travel on a channel that charges per message — it is off unless switched on, one
  person cannot send them in a stream, and a day's charged messages have a ceiling an administrator sets
  and can see on the delivery page.
- **Being asked onto a trip does not open the cave, so somebody who can open it is told** — an
  invitation grants nothing, so when a person asked onto a trip cannot read a cave the trip is about,
  the cave's owner and the full administrators get a message with a link to the cave, whether the cave
  was already on the trip or was added afterwards. That message is honest about its own reach: it says
  it went to the owner and the administrators and to nobody else, that somebody who could grant access
  another way — through a club, say — has **not** been told, and asks the reader to pass it on if it
  is not theirs to act on. Somebody on the list with no account on this installation gets nothing and
  can be granted nothing; they have to be reached another way.
- **A trip has a gallery and a cover** — the photographs taken on it, the albums made of them, and a
  headline picture chosen with the same star that names a cave's.
- **A trip writes itself up** — every trip has a report view: the whole trip laid out as a document,
  with a print stylesheet over it, and a **Word document** you can download or file against the trip
  itself. The write-up is built from *your* reading of the trip and nothing else, so it can never
  contain something the page would not have shown you — no cave you may not place, no account of an
  incident if you are only a reader of the trip, and photographs go in as the same renderings the
  gallery shows you, with their camera metadata stripped. A copy filed against the trip is narrower
  still: it is written for whoever may read the trip at all, not for the person who filed it. There
  is no map picture in it — the trip's sketch is written out in words instead — and there is no
  public address for a report: it is downloaded by somebody signed in who may read the trip.
  A club can write **its own layout** for the document: download the standard one, which is a short
  text file that explains itself in its own comments, edit it, upload it, and choose it. A layout can
  only ask for things the reader was already given, and a line whose contents turn out to be empty
  simply disappears — so the same layout produces an honest document for a member and for an editor.
- **Tags, saved & shareable map views**, and **multi-window** pop-out panels.
- **Share links** — hand out a revocable link to one feature and what it contains, either
  public or sign-in-only. A share never reveals a protected location.
- **Works on a phone** — the map workspace adapts to touch, including full geometry editing by
  finger, and the app installs to a home screen.
- **Accounts & permissions** — editable permission groups instead of fixed roles: named
  rulesets of allow/deny rules per resource and scope (everything, own content, a caving
  group's content, a feature subtree, a named feature set, a cabinet and everything filed
  below it, one object), per-object grants
  with the same reach, and an explainer that answers "why can this person see that?".
  Two-factor sign-in (authenticator app, emailed code or texted code), optional external
  login (Google/GitHub/OIDC), and location protection for sensitive caves — which keeps a
  protected cave's documents readable while withholding the fact that they point at *that*
  cave (an administrator can switch that disclosure on), and never hands out a photo whose
  own capture point would place the cave.
- **Email and SMS that an operator controls** — any SMTP server, any SMS gateway that speaks
  HTTP, and the wording of every message editable per language from the admin pages. Configure
  nothing and the app still runs: links and codes go to the server log instead.
- **You can see whether the mail is actually going out** — a page for whoever runs the installation
  shows how many messages are waiting, held for a summary or given up on, and — the number that
  matters — how long the oldest one that has not left has been waiting. Below it, the ones worth
  looking at: what each was about, who it was for **under the name they may be shown under and never
  their address**, and what the far end said, with anything address-shaped taken out of it. One that
  has given up can be put back by hand, which asks every question again before it sends: somebody
  who has since said they do not want that kind of message does not get one, somebody who asked for
  a daily summary gets it in their summary, and every such act is recorded against the person who
  made it. How long notifications are kept is a number an administrator can change, over whatever
  the deployment configured.
- **English and Romanian** throughout — and the language you pick follows your account, so the
  messages the system sends you arrive in it too, not only the screens.

## Quick start

```bash
git clone https://github.com/apgeo/silexgis.git
cd silexgis/deploy
cp .env.example .env          # set the DB password, admin account and public URL
docker compose up -d          # → http://localhost:8080
```

See **[docs/INSTALL.md](docs/INSTALL.md)** for HTTPS, encryption at rest, backups, upgrades,
external login providers, and the non-Docker (Linux/Windows) install path.

Word, Excel and OpenDocument files are stored, searched and downloaded out of the box, but
have no pages to show in the browser — those formats have none until something lays them out.
An optional converter service gives them real pages; see
[Showing Word and Excel files](docs/INSTALL.md#showing-word-and-excel-files).

SilexGIS stores uploaded files and database rows unencrypted, and expects the operator to
encrypt the volume or disk beneath them. What that protects against, what it does not, and how
to set it up is in [Encryption at rest](docs/INSTALL.md#encryption-at-rest).

## Repository layout

```
server/   ASP.NET Core API (.NET solution: Api / Domain / Infrastructure + tests)
client/   React + TypeScript SPA (Vite, Ant Design, OpenLayers)
deploy/   Docker Compose, TLS and terrain overlays, reverse-proxy configs, backup/restore
          and terrain pre-bake scripts
docs/     Installation and operations documentation
```

## Development

```bash
docker compose -f deploy/docker-compose.dev.yml up -d db   # PostGIS only
dotnet run --project server/src/SilexGis.Api                # API on :5080
cd client && npm ci && npm run dev                          # Vite dev server
```

## License

AGPL-3.0-or-later — see [LICENSE](LICENSE). Third-party bundled components are listed in
[NOTICE](NOTICE).
