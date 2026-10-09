# Features

Everything SilexGIS does, in prose. This page is the long answer to *what is it for*; the
[README](../README.md) carries the short one, and the
[user guide](user-guide/README.md) — in English and
[română](user-guide/ro/README.md) — is the handbook for actually using it, with a page per
feature and walkthroughs of whole jobs.

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
  centerlines projected on the map. A cave's **walls** can be uploaded as well, as a binary
  `.stl` exported from Therion: you say which coordinates the file is written in — a projected
  system, or plain metres about a point, which is normally the cave's own entrance and is
  offered pre-filled — and the installation converts it into a model the 3D view draws in place,
  under the terrain, beside that cave's centerlines. By default it is loaded only for the cave
  you select, and its size is shown before it is fetched. The layer list can instead draw the
  walls of **every cave in view**: once you are zoomed in far enough, nearest the middle of the
  view first, up to a number of caves and a total download size the installation sets — and it
  says how many of the caves in view it drew and what kept the rest out, never a silent subset.
  Walls that leave the view are let go, and switching the walls off genuinely lets go of all of
  them. A cave keeps every model ever uploaded, and **one of each kind is marked current** —
  the line plot the map and the measurements read, the walls the 3D view draws. The first stays
  current until you choose another, and choosing a line plot moves the cave's shape on the map
  with it, so the map and the figures never describe two different surveys. Two line plots of one
  cave can be **compared** in the survey viewer: overlaid in two colours where they are in the
  same coordinates, or side by side with linked views where they are not, with each survey's parts
  shown and hidden together or separately. A stored model can be **read again** from its row —
  after a reading that failed, or after an upgrade that reads surveys better — without uploading
  it a second time, and an administrator can ask the same of every line plot at once. A station
  the file gives no name is kept under the label the drawing gives it, so on a Therion survey it
  can be pressed and reported at like any other.
- **Survey closure** — archive the compilation log beside the model and the cave page reports how
  well the survey closes, in the compiler's own words: every loop it found, with both its relative
  error and its absolute error in metres, its length, its stations and its per-axis components. The
  two measures are always shown together and the table says which one it is ordered by, because
  they disagree about which loop is worst. Each set of figures records which file and which revision
  of it was read, and when; a corrected log read again replaces them rather than mixing two runs.
  The figures follow the survey they belong to, so a reader who may not be told a cave's exact
  position is not shown them.
- **3D view** — the configured base layers draped on a globe, on a page that is downloaded
  only when it is opened. Needs WebGL 2; a browser without it gets an explanation rather
  than a dead canvas. No vendor terrain, imagery or geocoding service is contacted and the
  3D engine is served by your own installation; the basemap is whichever base layers you
  configured, so an air-gapped install needs one it can reach. **Tracks from imported GPX, KML
  and GeoJSON files** are drawn there too, as lines at their recorded altitudes — or laid on the
  ground when the file recorded none — sharing the flat map's choice of which files are shown.
  A cave's page opens the view on that cave, selected and framed. When a layer of the scene
  stopped at the installation's per-request limit, the scene says so rather than quietly showing
  fewer features than the map. The view can be **saved as a picture**, with the credits of the
  basemap and of the elevation model written along its bottom edge so they travel with it.
- **Real relief, if you want it** — that globe is a smooth sphere out of the box, needing no
  elevation server and nothing downloaded. An operator who wants the caves under actual
  hillsides bakes free elevation data into a tile pyramid and serves it as static files from the
  same installation — either from the command line in one documented step, or from inside the
  application: draw a rectangle on a map, and it obtains the data, converts it, bakes the tiles
  and checks them, saying where it has got to as it goes. That last route needs one extra service
  a plain installation does not run. Everyone who wants none of it is unaffected. An installation
  with several builds lets each viewer **switch the ground** between them in the 3D view, and
  offers a finer build when the camera moves over one — offers, never swaps, because changing the
  ground under somebody mid-pan moves every cave on screen. A new survey over ground already baked
  does not cost the region again: a build can **extend a finished one**, adding only the new
  rasters to a copy of its tiles.
- **Pictures drawn from that elevation** — shaded relief, steepness, facing, ruggedness,
  topographic position, roughness, a colour relief or contour lines, requested from the terrain
  page, computed once from a finished build and shown to everyone who may read terrain as ordinary
  map layers. Steepness and facing are measured on the ground rather than on the grid of degrees
  the elevation is held in, so a hillside facing east reads as steep as the same hillside facing
  north. Each one belongs to the build it was
  drawn from, so **activating a different build marks every picture of the old one as out of date**,
  where the reader sees it rather than in a log — a shaded relief that disagrees with the heights
  beneath it otherwise looks like a fault in the cave data. They live under their build and are
  deleted with it. Some relief measures the elevation library cannot produce — geomorphons,
  curvature, and flow direction, accumulation and the wetness index — are simply absent rather than
  approximated under a borrowed name.
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
  already written. A **plan** uses the same card for the things a party settles before it sets
  off: when and where to meet and how to find the spot, who is driving, how many seats and where
  they leave from, what gear and rigging to bring, whether a permit is needed and whether it has been
  obtained, what the forecast says, and where the group is doing its talking. A plan can also carry
  **where the party gathers** — a point, or the walk in to it — drawn on the same kind of map as the
  trip's own sketch and shown, like it, to everybody who may read the trip, which the form says out
  loud: a meeting point near a guarded entrance places that entrance for everybody invited. Beside them a trip records the figures worth counting — how deep, how far surveyed,
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
- **Finding the trips you want, and saying what you found** — the trip list is not one search box.
  You narrow it by kind of trip, by lifecycle state, by who may read it, by whether anything went
  wrong, by who was there and by the area worked in; several values in one of those means *any of
  them*, and different ones together means *all of them*. Beside every choice is the number of trips
  it would leave you with — counted over the trips **you** may read, so two people legitimately see
  different numbers beside the same option and neither is being misled. Above the table, a plain
  count of how many the filter left out of how many there are, so a narrowing is never a mystery.
  The whole filter sits in the address, so a view is a link you can send somebody and the back button
  walks the views you chose. You can arrange the result into groups, two levels deep — by year, kind,
  state, audience, incident, area or person — each group saying how many trips, over what span of
  dates, the commonest kinds and the people who appear most, with their counts. Where a trip can
  belong to more than one group at once, the screen says so rather than letting the totals quietly
  add up to more than the number of trips. And the whole filtered set downloads as a spreadsheet,
  showing exactly the trips the screen showed and nothing you may not read; if it is too large to
  hand over whole, the file says so inside itself. Narrowing by a person here is not the same
  question as asking *which trips did this person lead* — the roles stay inside the trip, and the
  page of your own trips is still worked out from whoever is signed in and asked about nobody else.
- **A deleted trip can be put back** — deleting a trip removes nothing. It leaves every list, map,
  calendar, statistic and public follow link at once, and waits on a list of **deleted trips** from
  which it is restored exactly as it was: its roster, the places it names, its files and its place
  in a camp. Whoever may delete a trip may restore it, and the confirmation says for how long — 30
  days unless the installation chose otherwise — after which the trip is removed for good.
  Undoing an import of trips deletes them the same way, so each can be restored on its own.
- **A deleted cave, entrance or surface feature can be put back** — deleting one removes nothing
  either. A cave goes with its entrances, an area with what it contains, and the list of
  **deleted caves and features** shows each such deletion as one row that says what went with
  it; restoring it brings all of it back, and a trip that named the cave names it again. Whoever
  may delete something may restore it. The list holds no positions at all, and a restored cave
  opens showing only the position its reader could see before. Something deleted before the cave
  or area containing it was is restored after that is back; undoing an import deletes its caves
  and features the same way, so each can be restored on its own.
- **What it all adds up to** — a person, a cave and a club each get their totals: trips, hours
  underground, metres surveyed, first visits, how many people, how many trips had an incident. Every
  one of them is counted **over the trips you may read**, and the screen says so, because two people
  with different access legitimately see different totals for the same person and both are right.
  Nothing is stored: hours are worked out from the times recorded, over however many days the trip
  really ran, and a first visit is simply the earliest trip that took somebody somewhere — so typing
  up an older trip from the archive corrects the figures instead of leaving a stale flag behind.
  A figure that would say nothing is left out rather than drawn: on a cave's own totals every trip
  counted went to that cave, so its places could only ever read one and its first visits could only
  repeat its people, and neither is shown there.
  Where trips were tracked, the totals also say how many, and the hours underground that their
  tracking logs come to — each person's reported entry paired with the exit that followed. That is
  a second source beside the hours typed on the roster, labelled as one and never added to them;
  an entry nobody closed adds nothing, and a camp's own write-up layout may print the figure on a
  line of its own.
  Any of the three can be saved as a spreadsheet, which carries exactly what the screen carried and
  says whose totals they are, because a file gets forwarded and read months later.
- **What a club's trips add up to, on one page** — from the trip list, one button opens the same
  trips as charts: how many trips each year, what they were for, where they went and who was on
  them, largest first. Beside the yearly bars runs the figure a trip log cannot otherwise be asked
  for — how many distinct areas the trips had reached by the end of each year. A line still climbing
  says the club is finding new ground; a line flattening under bars that are not says it is going
  back to ground it already knows. *New* there means new among the trips being counted, and the page
  says so under the line: narrow the list to one year and the line starts again from nothing,
  because an area an earlier trip had reached is new to the trips that are left. Everything is
  counted over the trips **you** may read, and each card's title says which trips it is drawing,
  because you can switch between the filter you came in with and everything you may read, and a
  title that did not move with the switch would be a lie.
  Where a trip counts into more than one bar — it went to two areas, it had four people — the page
  says so, and where there are more names than bars it says how many it is showing out of how many
  there are.
- **Years of trips typed up in a spreadsheet become trips** — the club's centralizator is uploaded
  and read on a review screen, not imported blind: every row says what it would create, which people
  and caves it matched, and what it could not settle, and nothing is written until you confirm.
  Sheets saved years ago on the machines of the day are not assumed to be modern text — where the
  file turns out to be written in an older code page the diacritics are recovered rather than turned
  into question marks, and the screen says which reading it used and lets you choose another if it
  guessed wrong. Sheets that name people the way clubs really do — a given name and an initial, or a
  given name alone — can be imported too: that stays switched off, because inventing a person you
  cannot later be sure of is not undone easily, and while it is off such names are counted and shown
  as ones nobody can be made from rather than dropped quietly. A name two people on your roster
  already answer to is never guessed at either way; it waits for somebody to say which.
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
- **Your own trips, soonest first** — one page answers *what am I going on, and when*: the trips you are
  named on, plus the ones you have been asked about and not turned down, ordered by when they happen
  rather than by when somebody last edited them, with the nearest at the top. The dashboard carries the
  next few of them beside the rest of the day's news. It is worked out from whoever is signed in and
  **cannot be asked about anybody else** — there is no way to request one person's trips by name, because
  the answer, and even the number of rows in it, would say where that person has been from records the
  asker may not read. A trip that has been called off stays on the list, and says so, because that is the
  thing you most need to notice; the list can be narrowed to one stage of a trip's life, or to a range of
  dates.
- **The club's own dates** — a meeting, a training session, a maintenance day, a gear check, a
  conference or a deadline is a record of its own, with a day, an optional second day it runs on to,
  optional start and finish times and a place written as an address. Times are the club's own wall
  clock: a 19:00 meeting reads as 19:00 to everybody, wherever they happen to be, which is what a club
  that meets in one place means by seven in the evening. An event goes through the same stages a trip
  or a camp does — drafted, proposed, planned, confirmed, announced, put back, called off — and who may
  read one is decided the same way: yours alone unless you say otherwise, your club's if you are in
  one, and anybody you name individually through the same sharing dialog every other record uses.
  **An event is answered the way a trip is.** Somebody is asked, each person says yes, no or maybe, and
  if the event has a number of places the answers decide who is in and who is waiting — the same list,
  in the order people answered, with the organiser free to pick somebody out of turn. A limit never
  refuses an answer; it only decides where in the queue an answer sits. A deadline takes no answers at
  all — a date to have something in by is not something to sign up for — so it is offered no list. And
  unlike a trip, an event keeps no separate record of who turned up: the answers *are* the record, so
  there is nothing to turn into anything.
- **One window over everything dated** — a calendar page answers *what is happening between these two
  days*, reading trips, expeditions and the club's events together in one list you can sort, group under
  headings (month, week, kind, state, caving group), narrow by family and by kind of event, narrow to
  a club's own, or narrow to your own. The list opens on **today**, with a line where it crosses today
  and the last things that happened and the next things coming on one screen, and everything chosen is
  carried in the page's address so a narrowed calendar is a link. In the month and week readings a
  record lasting several days reads as one record rather than as several sharing a title. It shows what is still to come and what already happened, marks a
  trip that has been called off and lets you switch those off, and lists a postponed one with a
  postponed mark rather than pretending the date it still carries is one anybody is going on. A trip
  that nobody has been shown yet is not on it — but that is only about what gets drawn: it stays exactly
  as readable as it was, on its own page and on the trip lists, to everybody who could read it before.
  **A calendar row never names a cave** — not the cave, not a count of caves left out — so a glance at a
  month can never place a protected entrance; the title is shown as the person wrote it, and you open
  the trip itself to see where it went. You have to say which days you want, the span is capped, and if
  a window somehow holds more than the page will return it tells you **how many rows it left out**
  rather than quietly showing you less than there is.
- **Checklists — what a party settles before it sets off** — anyone can write a list (permit, key
  collected, gear booked, whatever the club actually checks), keep it to themselves, share it with their
  club or make it readable by everyone; an administrator's published default is nothing more than a list
  with a wide audience. A trip purpose points at the list trips of that kind work through, and the trip's
  own page shows it as lines to tick off, with a quiet *3 of 7* on the trip listings. Ticking records who
  said so and when, and rewording a line later does not lose what was already confirmed. **Nothing is
  ever refused because a list is unfinished** — it tells the party where they are, it does not stand in
  their way, and it decides nothing about who may read the plan. Lists you may not read are not named to
  you, and no count of them is shown either.
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
  announcements may travel on a channel that charges for what it sends — it is off unless switched on,
  one person cannot send them in a stream, and a day's spending has a ceiling an administrator sets and
  can see on the delivery page. That ceiling counts the pieces a carrier splits a text message into
  rather than messages: one diacritic makes the same wording cost two of them, so a ceiling means half
  as many messages to members who read Romanian as to members who read English.
- **A trip can say when its party is due back, and be told the party is out** — a plan records when
  the party expects to be out and the hour to raise the alarm if nobody has said so. A pass runs on
  a schedule; when that hour goes by with nothing said, everyone the trip concerns who may read it
  gets an email — one that names the trip, its date and the expected hour in UTC, and **never a
  cave**, because whoever runs a search reads the trip, where the answer is already kept under the
  rules that decide who may have it. Anybody the trip names or has asked can stand the alarm down in
  one tap, whether or not they may edit the record, because the person who knows the party is out is
  the one on the trip. Standing it down disarms the check rather than erasing it, so what was
  arranged stays on the trip. The same pass reminds the people on a trip that it is coming up, once
  — and a trip that has been called off or put back stops both the alarm and the reminder, because
  the date they were set against is no longer one anybody is going on. A trip showing an armed alarm
  also says **when the check last ran**: silence is the good news in a callout, so a watcher that has
  stopped and a party safely underground look alike, and the page comes down on the side of saying
  nobody has looked. Delivery is the ordinary email queue's, which tries several times and then gives
  up — an alarm is a prompt to go and look, not a guarantee somebody was reached.
- **Following a party underground, and showing it to the people waiting** — a trip can be
  *tracked*: whoever takes the calls on the surface records each one as a report — who went in,
  who is at which station or how deep, who came out — and the trip's page draws every person on
  the cave's survey where they were last reported, saying plainly when somebody's place is not
  known, was measured on another survey, or may not be shown to you. It records and raises no
  alarm; that is the callout's job. A cave can declare what its depths mean — that 96 m is the
  Meander, at this station — so a depth phoned out lands on the station people mean rather than
  on whichever one arithmetic finds nearest, and the report card warns when a depth sits far from
  the station it will land on. Wherever a station is typed — declaring a place, reporting,
  correcting — the survey's own station names are offered as the name is typed, and a declared
  place whose station the survey no longer has is marked rather than silently passed over. A
  watch moved to a corrected survey part-way through a trip leaves its earlier reports marked
  **On another survey** in the log, and the model panel can show them on the survey they were
  made on — for looking only: nothing is recorded while another survey is on screen. A report
  written down wrongly is corrected in place, keeping
  everything pinned to it, and a finished trip's log stays open for the write-up. A report
  deleted from the log is kept: it can be put back exactly as it was — by **Undo** on the
  notice, or later from **Removed reports** under the log — and is destroyed only by a second,
  separate **Delete for good**, with one exception made on purpose. **When somebody asks to be
  removed**, one confirmed act on their row destroys every report the trip holds about them, the
  kept ones included, because a report that can be put back is not a removal; the confirmation
  says how many reports go and what stays (their name on the roster until the trip is edited,
  and the trip's history). Nobody else's reports are touched. And a person who cannot be deleted
  because trips still hold them is refused **with the trips named** — each trip the reader may
  open, its date, whether the person is on its roster, how many tracking reports it has about
  them and whether those can be removed from there — and with one line, never a count or a name,
  for anything else that holds them. A coordinator's
  spreadsheet of calls is read onto the log through a preview that says, row by row, what it
  would create, correct or refuse and states how it read the times; each report is filed under
  the person and the moment, so a corrected sheet imported again corrects instead of doubling.
  The sheet may be a file or rows pasted from a phone's spreadsheet, may keep the date and the
  time in one column or two — or times alone, for a sheet kept in a day — and may be read on a
  named time zone's clocks, summer and winter time included, instead of exactly as written.
  A word in its state column that is neither going in nor coming out — *descending*, *rigging*,
  *asleep* — is kept on the report as what the party was doing, and a sheet from a pothole's
  mouth may be told that every depth it writes is below the entrance, whatever its sign.
  A caver has a short name beside the full one — what their party calls them — which a sheet
  may name them by and which published trips show.
  A row that would replace a report shows the report as it stands beside the report as it would
  be left, a replacement changes only what the sheet has a column for, and the import writes
  what the preview showed or nothing: if the trip changed in between, the sheet is read again
  instead. The same sheet imported twice writes nothing the second time. And the log goes the
  other way: anybody who reads the trip can download it as that same sheet — one report on a
  row, each moment written whole with its offset — to correct in a spreadsheet and import
  again; a place the reader may not be told is left out of the file, and its row is marked so
  that importing it back is refused rather than writing nowhere over the place. The log itself is
  paged and can be narrowed to one person, a report that was changed after it was written is
  marked **Corrected**, and a report added once tracking is closed has to say when it was made
  rather than being stamped with the hour it was typed.
  **A report recorded with no signal is not lost.** When the server gives no answer at all, a new
  report is kept in the browser — for the account that recorded it only, and saying in words
  that its text is there — and is sent by itself when the connection returns, after a reload
  included; it lands at the minute it was about and is written once however many times it is
  sent. A sign-in that ran out during the outage is renewed and the report sent again, or the
  page says the reports are waiting for a new sign-in and offers it. A held report the server
  then refuses stays, with the reason, to be sent again or discarded — from the trip, or from a
  list in the top bar that also reaches a report whose trip can no longer be opened. This is
  the one thing the application holds offline: nothing is read or cached without the server,
  and corrections, deletions and imports are never held.
  The tab also tells whoever is coordinating three things it used to leave them to work out:
  somebody underground with **no word for hours** (three by default, an installation setting),
  the trip's **planned hour out and how late** the party is once it has passed with people
  still inside, and a station reported **outside the parts of the cave the party said it was
  going to** — parts the model can be narrowed to. Everybody the log has reports about stays
  in the table, marked, after being taken off the roster, and cannot be taken off it while
  tracking is running. None of this alarms anybody: nothing is sent, the overdue callout is not
  involved, and a published page shows none of it.
  A report can say somebody is **between two stations** of the survey — the log and the party
  table print both, and the model draws a dashed line along the survey from the first to the
  second; a visitor is shown the first station only. And a **note about the cave** — loose rock
  above a pitch, water rising — is a report about nobody, at a station or at none: it moves no
  one and is nobody's last word, it stands on the model as a mark of its own and is listed under
  the party, whoever can read the trip can read it, and no visitor is ever shown one.
  Photographs are hung on a **moment** of the trip rather than on a report, each at the time its
  own file says, with one correction for a camera clock that was out; each can be about a
  different person, and before anything is attached the dialog says, photograph by photograph,
  **where the replay will draw it** — at which station, on another survey, nowhere yet, or on the
  timeline only — read from the log by the replay's own rule and worked out again as the clock
  correction changes. A place withheld from the reader is said to be withheld, never shown.
  **A camp counts all of its parties on one screen**: the camp's page has a *Who is underground*
  tab listing every trip of the camp that is being tracked, or was closed in the last two days,
  with how many of its party are underground, out and not yet heard from, each person by name
  and when they were last heard. It is a head count and deliberately not a map — it carries no
  station, depth, survey or cave for anybody, which is what lets it count a party in a cave
  whose location the reader may not be told — and it raises no alarm: the hour a party plans to
  be out by is printed as a time and compared with nothing. It lists only the trips its reader
  may read, and a trip whose tracking was never started is not on it at all.
  **The log's times can be taken onto the trip's roster**: once somebody has come out, a dialog
  shows each person's first going in and last coming out beside the entry and exit time the
  roster holds, on a stated time zone; only the people ticked are written, a time somebody typed
  is marked and left unticked, and a moment the roster cannot hold — another day than the
  trip's, clocks that changed in between — is said rather than written.
  A trip can then be **published** with a link: anybody holding it, with no account, sees the
  party on the survey, refreshed while they are underground. The page is written for a reader who
  has never followed a trip: it says that a place is where somebody was last *reported* and not a
  live position, since when the trip has been followed, how long ago the page itself was last
  read — and that it has stopped refreshing, when it has — in times that keep moving while the
  page is open and become the hour once the trip is over. It opens in Romanian and reads in
  English from a button on the page or from a link that names the language. A club pastes a
  block into its own website to show the same viewer in an article whose links can move it, and
  the framed viewer tells that article what it is showing. The same link lists
  the cave's other parties being followed now — in a list shut until the reader opens it, from
  which any of them can be watched on the link's own survey under a banner saying whose party it
  is — and its finished published trips, gathered by camp (with the note the camp wrote for
  these readers, where it wrote one), each playable as
  a replay on the survey its reports were measured in; any moment of a replay can be copied as a
  link that opens there, standing or already playing. Three more things are told to a visitor only
  where the installation turns them on, each by a setting of its own that is off as installed:
  the hour the party planned to be out by, the names the cave gives its depths, and — on a
  finished trip's replay — the photographs from the public gallery that were hung on its moments,
  each beside the place in the party of the person it is of. The page names people as the installation
  decides — real names by default, a caption to keep one person off it; where names are off, a
  person's number (*Caver 3*) is theirs from the moment the trip first names them and does not
  move when the roster is edited — a cave with protected
  coordinates cannot be published at all, and a link ends by itself a short while after the watch
  is closed. A link that has ended up in the wrong place is **replaced** in one act — the old
  address stops answering, a fresh one is shown once, and the trip keeps its place in the cave's
  history. The [user guide](user-guide/features/live-tracking.md) has the whole of it.
- **Everything published, on one page for the full administrators** — every follow link of the
  installation with what its address opens right now (followed, just closed, in the archive,
  withheld because of its cave, opening nothing, taken back), worked out by the same rules the
  published pages are served by, so the list cannot disagree with the page. From there a link
  is replaced, a trip is unpublished, or everything is — each confirmation saying what goes with
  it and cannot be undone: a finished trip leaves its cave's public list of past trips and comes
  back only by starting its watch again. A survey file can draw more than its own cave, so a
  publication is **flagged, never refused**, when a protected cave's position lies inside the
  area the survey's stations span — a check by position that cannot see inside the file, names
  no cave, and tells a publisher nothing about a cave they may not place. Tracking that
  nobody closed is not closed for them: the page says when each trip's tracking was started and
  for how many days it has run, narrows to those running longer than a number of days the
  reader chooses, and leads to the trip where closing it is done. It also prints the address
  the server counted the reader's own request under, which is how a reverse-proxy count that
  is too low — otherwise silent — is seen.
- **"The published page shows nothing" has an answer** — a published page tells a visitor the
  same thing for every link it will not open, on purpose, and that used to leave whoever runs
  the installation guessing among a dozen causes. Now each refused read is written to the
  server's log with a one-word reason and the link's short log code (never its address), the
  installation counts the published reads it answers and the ones it turns away, a reader turned
  away as one request too many is told how long to wait, and a script asks an installation from
  outside what one link answers — taking the link on standard input, never as an argument, and
  printing it nowhere. The [install guide](INSTALL.md#when-a-published-page-or-the-article-showing-it-shows-nothing)
  lists the causes in order of likelihood with the reason word for each. Settings for published
  trips that cannot mean anything now stop the application at start, naming the setting, instead
  of quietly producing pages that answer nobody.
- **A tracked trip becomes a movie you can send** — from the trip's tracking tab, or from a survey
  on the cave's page, a signed-in member makes a short film of the party moving through the
  survey: one trip or several, on one calendar or side by side, the model turning or still. It is
  made in the browser and saved as a **GIF, a WebM or an MP4** — nothing is uploaded, the
  installation keeps no copy, and the public pages of a published trip offer no such thing. The
  preview is the movie: what it shows is what the file holds. Because a file outlives the check
  that let you see the trip, the movie starts cautious — first names, no notes, no altitude
  display — and the dialog says what it will be called **before** it is made: the file is named
  after the title the movie shows, and with the title switched off it is called only
  `silexgis-movie` and the day, naming neither trip nor cave. A trip still under way can be
  filmed, and its movie ends when the export starts. An export draws every frame anew — half a
  minute to a few minutes on a computer without a graphics card — so it says how long it has left,
  **Cancel export** stops it at once, and Escape or the X ask before throwing it away. A long
  video — from about 256 MB — is written to a file you choose as it is made, where the browser
  can do that (Chrome, Edge and their kin), instead of being held in memory to the end. The size
  estimate of a GIF corrects itself from the GIFs you have made. Any moment of the preview can be
  saved as a **picture** (a PNG of that frame, captions and legend included, named by the same
  rule); two **presets** set the file up in one press — a small GIF for a chat, or an HD video as
  MP4 or WebM, whichever the browser can write — and change the file only, never who appears, the
  view or the captions; and with the preview or its slider in focus, **Space** plays and pauses
  and **Home** and **End** go to the two ends. Under each ticked trip you choose **who appears** —
  somebody left out has no marker, no trail and no note in the picture, and the choice is for that
  movie only, stored nowhere. After the clock the movie says how many times faster than life it
  runs (**×240**). And where a survey's file carries its own **terrain**, a switch draws the
  surface over the cave — off until you turn it on, because a cave shown under its hills can be
  placed by whoever gets the file. The photographs hung on a trip's moments can be drawn into
  its movie — in a corner or over the whole frame, each for a number of seconds you choose,
  fading in and out, one after the other where they would overlap — off until you turn them on.
  The [user guide](user-guide/features/tracking-movie.md) has the whole of it.
- **Being asked onto a trip does not open the cave, so somebody who can open it is told** — an
  invitation grants nothing, so when a person asked onto a trip cannot read a cave the trip is about,
  the cave's owner and the full administrators get a message, whether the cave was already on the trip
  or was added afterwards. Its link is the quick way to let that person in: it opens the cave's
  permissions with read access for them already drafted — and nothing more than read — so that one
  confirmation grants it, and nothing is granted until that confirmation is given. What is granted is
  an ordinary rule on the cave and lasts until it is removed; access that ends by itself on a date is
  not built. That message is honest about its own reach: it says
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
  still: it is written for whoever may read the trip at all, not for the person who filed it. The
  copy you download carries **a map of the trip** — its sketch, where its party met, and the caves
  it names that you may place — drawn by your own browser out of what the page had already been
  given, over one of the installation's map backgrounds that may be copied into a document, with
  a line under it saying whose view it is and when. Which backgrounds those are is an
  administrator's setting, one switch each, starting from what the installation shipped with and
  kept across restarts. The sketch is still written out in words
  beside it. The copy filed against the trip never carries that map: it shows what one reader may
  see, and a filed copy is opened by everybody who may read the trip. There is no public address
  for a report: it is downloaded by somebody signed in who may read the trip. Where the
  installation runs its optional document converter, the same write-up — a trip's or a camp's,
  map included — can be downloaded **as a PDF**: the Word document is passed through the
  converter inside the request and nothing is stored. Without the converter the button is not
  offered, and the page's print view is the way to a PDF.
  A club can write **its own layout** for the document: download the standard one, which is a short
  text file that explains itself in its own comments, edit it, upload it, and choose it. A layout can
  only ask for things the reader was already given, and a line whose contents turn out to be empty
  simply disappears — so the same layout produces an honest document for a member and for an editor.
  A layout of a club's own may also ask, with the one word `tracking`, for the **journal of a trip
  that was followed underground** — when tracking ran, each person as last reported, every report
  in time order, under a sentence saying it is a journal and not a callout record. The standard
  layout does not print it; a place is printed only to a reader who may be told it, and the copy
  filed against the trip names a place only where every account may be. A filed copy is a file and
  does not change: putting a cave under location protection takes the filed write-ups off the
  trips followed in it, to be filed again, and a report removed later stays in a filed copy until
  then.
  **A camp writes itself up too**, as one document over the trips it gathered — day by day, team by
  team, in a layout of its own kind — and it carries **what each trip wrote about itself**: the
  trip's account, its results and the answers on its form, under the trip's date and title. Each
  trip's text is exactly what that trip's own write-up would show the same reader, decided in one
  place for both documents, so a camp's write-up can never print what a trip's withholds; the copy
  filed against the camp carries each trip only as any account may read it.
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
  A right held only over a caving group's content opens the door it should: a member whose club
  lets them record the club's trips — and who holds nothing wider — is offered the trip form, which
  binds the trip to the club and says so, or asks which club when there are several.
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
  messages the system sends you arrive in it too, not only the screens. Dates included: calendars
  and date pickers show the month and weekday names of the language you are reading in, and start
  the week on the day that language does.
