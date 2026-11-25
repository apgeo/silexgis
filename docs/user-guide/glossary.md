# Glossary

🇬🇧 **English** · 🇷🇴 [Română](ro/glossary.md)

[← Back to the guide](README.md)

---

The words this application uses, and exactly what it means by them. Where a word means
something narrower here than in ordinary caving speech, that is said.

---

**Access key** — A long secret text a field device (TopoDroid on a phone) holds instead of your password,
able only to upload surveys as you. Shown once when made, revocable at any time, and managed only
when you are signed in with your password. → [Uploading surveys from TopoDroid](features/topodroid-uploads.md#an-access-key-for-the-phone)

**Account** — Somebody who can sign in. Distinct from a **caver**.

**Account invitation** — How an account is opened by somebody else: **Invite someone** on
*Administration → Accounts* takes a name and an email address, and the owner receives a link to
choose their own password. The link works once and for 24 hours. Not the same as being asked onto
a trip, which grants nothing. → [Accounts](admin/accounts.md)

**Album** — A curated, ordered arrangement of photographs, with a cover, shareable by its own
link. Deleting one does not delete the photographs.
→ [Photographs](features/photographs.md#albums)

**Anchor** — The part of a thing a [link](features/links.md) points at, rather than the whole:
a page, a passage of text, a moment in a recording, a survey station, a waypoint.

**Announcement** — One line sent to every member of a caving group who holds an account, each
in the way they chose. → [People and clubs](features/people-and-clubs.md#announcing-to-a-club)

**AUC** — The area under a *rate curve*, between 0 and 1: about 0.5 is a map that ranks no better
than chance. It belongs to one area, one cell size, one draw of kept-back entrances and one
account's rights, and is not an accuracy. → [Cave potential](features/cave-potential.md#what-the-auc-does-not-mean)

**Audit trail** — The installation-wide record of what happened. Distinct from a record's own
**history**. → [History and audit](features/history-and-audit.md)

**Cabinet** — A shelf in the filing tree that documents live in, and the unit permissions are
granted on. A document can sit in several at once.
→ [Documents and cabinets](features/documents-and-cabinets.md#cabinets)

**Callout** — The arrangement whereby a trip records when its party is due out and the hour to
raise the alarm if nobody has said so.
→ [Checklists and the callout](features/checklists-and-callout.md#the-callout)

**Camp** *(expedition)* — A multi-day, multi-trip effort that gathers trips, with its own
roster, working area and leads board. → [Camps](features/camps.md)

**Categories** *(statistics)* — The screen under **Statistics** that compares one measurement
across cave types, or across rock types, drawing one box per group. → [Measurements and
statistics](features/measurements-and-statistics.md#the-whole-registry-explored)

**Cave-potential model** — A saved judgment of which evidence suggests a cave and how much each
kind counts: evidence layers, how each is scored, and their weights. A model can instead *learn
from known entrances* (by *frequency ratio* or *weights of evidence*), and then holds only its
layers, their class bounds and how the entrances are parted for testing. Either kind names no
place. Run over an area, it gives a *favourability map*. → [Cave potential](features/cave-potential.md)

**Caver** — An entry in the club's roster. **May or may not have an account.** A caver with no
account can be named on trips but cannot be granted access or sent anything.
→ [People and clubs](features/people-and-clubs.md)

**Caving group** — A club, group or organisation. Matters for visibility, for permission
scoping, and for announcements. Not the same as a **permission group**.

**Centerline** — Survey line work, projected onto the map and drawn at depth in 3D. Either
uploaded, or extracted from a compiled survey.
→ [Surveys and models](features/surveys-and-models.md#centerlines)

**Checklist** — A list a party works through before setting off. **Advisory only** — nothing
is ever refused because a line is unticked.
→ [Checklists](features/checklists-and-callout.md)

**Closest approach** — The shortest distance between two caves' line work, with its horizontal
and vertical components — *the two parts of that same line*, not two separate measurements.
→ [Measurements](features/measurements-and-statistics.md#closest-approach)

**Consistency ratio** — For a model weighed by pairwise comparisons: how far the comparisons
contradict each other. 0 is none; above 0.1 is warned about, never refused. It says nothing about
whether the judgments are right. → [Cave potential](features/cave-potential.md#pairwise-comparisons-and-the-consistency-ratio)

**Containment** — A feature being inside another feature. Drives breadcrumbs, and **inherits
location protection downwards**. Distinct from a **link**.

**Detection rule** — What decides that a waypoint called `P. Ursilor` is proposed as a cave
named *Ursilor*. → [Vocabularies](admin/vocabularies.md#configuration--detection-rules)

**Document kind** — What a document is (permit, survey report, bulletin…), and **what details
documents of that kind are asked for**.

**Draft** — A trip or event that has told nobody. Not a *hidden* record: whoever its
visibility admits can already read it.

**Evidence layer** — One surface a cave-potential model weighs, a value per cell: from the shape
of the ground (elevation, slope, landform…) or from what has been recorded (rock type, distance to
faults or sinkholes, entrance density). → [Cave potential](features/cave-potential.md#the-evidence-layers)

**Favourability map** — What a cave-potential model gives when run over an area: every cell scored
from 0 to 1 by the model's weights, on a date. A weighing of evidence, not a probability and not a
finding. Who may read one is decided by what it was made from, not by its author. A map learnt
from known entrances is stretched between its own least and most favourable cell, so its colours
rank the ground of that one map. → [Cave potential](features/cave-potential.md#the-map)

**Frequency ratio** — For one class of an evidence layer: its share of the cells holding a known
entrance over its share of the ground. 1 is in proportion, above 1 more than its share, 0 none
known. Also the method that draws a map from the sum of those ratios — an index, not a
probability. → [Cave potential](features/cave-potential.md#frequency-ratio)

**Filter** — A question about your records: which kinds of object, the conditions they must
meet, and the order of the answer. Always answered with what the person asking may see — it narrows,
never widens. → [Search and filters](features/search-and-filters.md#filters)

**Feature** — The single model everything on the map shares. Its **kind** is one of *Feature,
Cave, Cave entrance, Centerline*.
→ [Core concepts](core-concepts.md#1-everything-on-the-map-is-a-feature)

**Feature kind** — What a feature records on the map — sinkhole, spring, pitch, bivouac, karst
area… — and with it the shapes it may be drawn as, how it shows when its location is protected,
and what details it asks for. Kept on *Configuration → Feature kinds*, where a club can add its
own. Narrower than the four kinds above (feature, cave, entrance, centerline).
→ [Vocabularies](admin/vocabularies.md#configuration--feature-kinds)

**Feature set** — A named set of features that access rules can be scoped to. The way to say
"these particular caves". → [Permissions](admin/permissions.md#feature-sets)

**Full Administrators** — The one group whose *membership itself* is the grant, decided before
any rule is consulted. It holds no rules.

**Geofile** — An uploaded vector file (GPX, KML, shapefile…). Becomes a map layer, and can
then be reviewed into registry records. → [Geodata](features/geodata.md)

**Geological unit** — An outline of ground that is all one rock class, from a geology map, with
its lithology chosen from a fixed list. Not the same as a cave's declared *rock type*.
→ [Geology](features/geology.md#geological-units)

**Headline picture** — The starred photograph that represents a cave or a trip.

**History** — A single record's change trail, with per-field restore. Protected values read
*"Value hidden"*. → [History and audit](features/history-and-audit.md)

**Karstifiable** — Said of a lithology that dissolves readily enough to host caves: the
carbonates, the evaporites and calcareous conglomerate. Fixed by the application for each class
of its list. → [Geology](features/geology.md#the-lithology-list)

**Karstification index** — A single reading, *very low* to *very high*, of how karstified an area is: the mean of its cave density and its depression cover, each against a stated reference. A reading that is missing is left out, not counted as nought.
→ [Karst analyses](features/karst-analyses.md#the-karstification-index)

**Lead** — Something a trip left open. Collected onto a camp's leads board, with a promise
grade and a state (open, checked, dead end, continues).

**Link** — A named relation between two or more things of any kind, optionally pointing at a
part rather than the whole. Distinct from **containment**. → [Links](features/links.md)

**Link-annotated text** — A document whose passages are highlighted, and where following a
passage moves the open map, 3D scene and viewers.
→ [Documents](features/documents-and-cabinets.md#link-annotated-text)

**Location protection** — The mechanism that withholds a record's exact position while keeping
the record readable. Its permission action is **Exact location**.
→ [Location protection](admin/location-protection.md)

**Main member** — In a directed link, the item the relation reads *from*.

**Morphometry** — Two different things, unfortunately. On a **cave**, the declared figures
somebody typed in (length, depth, volume…). On an **area feature**, the *measured shape*
computed from the outline (area, circularity, long-axis bearing…).

**Own basemap** — The installation's own vector basemap: OpenStreetMap shapes of a region an
administrator downloaded, served by the installation alone. → [Own basemap](admin/messaging.md#own-basemap)

**Passage network** — A cave's survey read as stations joined by passage legs, counted for junctions, dead ends, independent loops and the ratios of karst studies. → [Karst analyses](features/karst-analyses.md#passage-network)

**Permission group** — A named ruleset with members. Not a caving group.
→ [Permissions](admin/permissions.md)

**Point cloud** — A laser scan of a cave: millions of measured points of its walls, kept among the
cave's surveys as a **scan**. Drawn in its own metres until it is placed on the map.
→ [Point clouds](features/point-clouds.md)

**Position quality** — How an entrance's coordinates were obtained: unknown, GPS, from map,
estimated. The difference between "walk here" and "search this slope".

**Position condition** — A filter condition on where something is: *within* a distance of an
object, a point or a saved filter's results, or *inside* a named area or a pasted polygon. Answered
on the positions you are shown, never on positions withheld from you.
→ [Search and filters](features/search-and-filters.md#position-conditions)

**Printed code** — A QR label bolted at a cave. Scanning it confirms only that the code is
registered. → [Sharing](features/sharing-and-public-pages.md#printed-qr-codes)

**Processing profile** — A named set of settings a scan-processing step runs with (HDMapping's
odometry, thinning, tiling); personal, a caving group's or the installation's.
→ [Processing profiles](features/point-clouds.md#processing-profiles)

**Protected location** — See *Location protection*.

**Quick filter** — A saved filter pinned as a one-click chip on the map, the cave registry and
the feature list. Up to twelve; they follow your account.
→ [Search and filters](features/search-and-filters.md#quick-filters)

**Rate curve** — How well a map learnt from known entrances ranks them: taking the ground from the
most favourable cell down, the share of entrances found against the share of ground taken. The
**success rate** is drawn against the entrances the model learnt from and always flatters; the
**prediction rate** is drawn against entrances kept back and is the one to read. Summed up by the
*AUC*. → [Cave potential](features/cave-potential.md#the-two-curves-and-the-auc)

**Relations graph** — A page that draws one item and everything related to it, one step at a time,
with filters per kind of relation. Shows, never edits. → [Relations graph](features/relations-graph.md)

**Report layout** — A club-authored template for the Word document a trip generates. Can only
ask for things the reader was already given.

**Rule set** *(detection)* — A named, ordered set of detection rules, owned by the
installation, a caving group, or you. Exportable as a file.

**Saved filter** — A filter kept under a name, for yourself, a caving group or everyone signed in.
Sharing one gives the question only, and it never keeps a place to measure from.
→ [Search and filters](features/search-and-filters.md#saved-filters)

**Saved view** — A named map configuration: extent, layers, filters. Shareable by a link that
shows only what is public in its own right.
→ [Saved views](features/saved-views-and-windows.md#saved-views)

**Scan** — See *Point cloud*. A *raw recording* is the folder a MANDEYE scanner wrote, which the
server turns into a point cloud.

**Second factor reset** — What an administrator does for a member who has lost both their
authenticator and their recovery codes: **Reset second factor** switches every second factor
off, stops the recovery codes working, signs the member out everywhere and tells them by email.
They then sign in with their password alone and set one up again. Refused on your own account.
→ [Accounts](admin/accounts.md) ·
[Two-factor authentication](features/account-and-settings.md#two-factor-authentication)

**Selection** *(map)* — What is selected on the map: one thing, or several. Filled by clicking,
or — up to 200 — from a list's *Select all found on the map*. It lasts for the session and is not
a **feature set**. → [Map workspace](features/map-workspace.md#selecting-things)

**Selection** *(sync)* — The set of caves a phone carries offline. Naming caves, not taking
them over. → [Mobile sync](features/mobile-sync.md)

**Share link** — A revocable link to one record, public or sign-in-only. Shown once. Never
reveals a protected location. → [Sharing](features/sharing-and-public-pages.md)

**Short name** *(a caver's)* — What the party calls somebody — the name a surface log is
written in. Shown on published trips in place of the full name, and matched whole when a sheet
of reports names somebody by it. Empty, the full name is used.
→ [People, cavers and clubs](features/people-and-clubs.md#the-caver-roster)

**Stay** — One stretch of days somebody was at a [camp](features/camps.md), in one role. It is
not the person: somebody in two roles, or somebody who left and came back, has two stays.
→ [Camps](features/camps.md#who-was-there)

**Stereonet** — A circle on which many structural readings are plotted at once, each as the pole
of its plane (equal-area, lower hemisphere), shaded by how densely the poles crowd.
→ [Geology](features/geology.md#the-stereonet)

**Structural measurement** — One reading of a plane taken at a point — bedding, a joint, a fault
plane — stored as dip direction and dip, and enterable as strike and dip by the right-hand rule.
→ [Geology](features/geology.md#structural-measurements)

**Survey source** — The `.th`, `.svx`, `.thconfig`, `.log` or survey-app `.zip` a compiled
survey was made from. Archived, never read.
→ [Surveys and models](features/surveys-and-models.md#survey-sources)

**Trip purpose** — What a trip was for. Also decides what its report asks for and which
checklist it works through.

**Vector background** — A map background drawn by your browser from shapes in a style rather than
from finished pictures: sharp at every zoom, labels in your language. Carries no relief.
→ [Map workspace](features/map-workspace.md#base-layers)

**Visibility** — A record's own audience: private, caving group, authenticated users, public.
One of the three things that decide access.

**Watson's U²** — The test that compares a cave's passage directions with the strikes of the
structural readings around it, giving a probability that the two come from one distribution.
→ [Geology](features/geology.md)

**Weights of evidence** — A way of counting, from known entrances, what each class of an evidence
layer says: a positive weight **W+**, a negative weight **W−**, their **contrast** C, and the
*studentised* contrast C / s(C) that says how far the contrast can be trusted (above about 2, with
some confidence). The map drawn from them is a probability per cell, too high where layers repeat
each other. → [Cave potential](features/cave-potential.md#weights-of-evidence)

**Work area** — Ground your club works, drawn as an area and nested, shown with what contains
it so two passages of the same name are told apart. → [Work areas](features/work-areas.md)

---

[← Back to the guide](README.md)
