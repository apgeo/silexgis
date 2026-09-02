# Glossary

🇬🇧 **English** · 🇷🇴 [Română](ro/glossary.md)

[← Back to the guide](README.md)

---

The words this application uses, and exactly what it means by them. Where a word means
something narrower here than in ordinary caving speech, that is said.

---

**Album** — A curated, ordered arrangement of photographs, with a cover, shareable by its own
link. Deleting one does not delete the photographs.
→ [Photographs](features/photographs.md#albums)

**Anchor** — The part of a thing a [link](features/links.md) points at, rather than the whole:
a page, a passage of text, a moment in a recording, a survey station, a waypoint.

**Announcement** — One line sent to every member of a caving group who holds an account, each
in the way they chose. → [People and clubs](features/people-and-clubs.md#announcing-to-a-club)

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

**Containment** — A feature being inside another feature. Drives breadcrumbs, and **inherits
location protection downwards**. Distinct from a **link**.

**Detection rule** — What decides that a waypoint called `P. Ursilor` is proposed as a cave
named *Ursilor*. → [Vocabularies](admin/vocabularies.md#configuration--detection-rules)

**Document kind** — What a document is (permit, survey report, bulletin…), and **what details
documents of that kind are asked for**.

**Draft** — A trip or event that has told nobody. Not a *hidden* record: whoever its
visibility admits can already read it.

**Feature** — The single model everything on the map shares. Its **kind** is one of *Feature,
Cave, Cave entrance, Centerline*.
→ [Core concepts](core-concepts.md#1-everything-on-the-map-is-a-feature)

**Feature set** — A named set of features that access rules can be scoped to. The way to say
"these particular caves". → [Permissions](admin/permissions.md#feature-sets)

**Full Administrators** — The one group whose *membership itself* is the grant, decided before
any rule is consulted. It holds no rules.

**Geofile** — An uploaded vector file (GPX, KML, shapefile…). Becomes a map layer, and can
then be reviewed into registry records. → [Geodata](features/geodata.md)

**Headline picture** — The starred photograph that represents a cave or a trip.

**History** — A single record's change trail, with per-field restore. Protected values read
*"Value hidden"*. → [History and audit](features/history-and-audit.md)

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

**Permission group** — A named ruleset with members. Not a caving group.
→ [Permissions](admin/permissions.md)

**Position quality** — How an entrance's coordinates were obtained: unknown, GPS, from map,
estimated. The difference between "walk here" and "search this slope".

**Printed code** — A QR label bolted at a cave. Scanning it confirms only that the code is
registered. → [Sharing](features/sharing-and-public-pages.md#printed-qr-codes)

**Protected location** — See *Location protection*.

**Report layout** — A club-authored template for the Word document a trip generates. Can only
ask for things the reader was already given.

**Rule set** *(detection)* — A named, ordered set of detection rules, owned by the
installation, a caving group, or you. Exportable as a file.

**Selection** *(sync)* — The set of caves a phone carries offline. Naming caves, not taking
them over. → [Mobile sync](features/mobile-sync.md)

**Share link** — A revocable link to one record, public or sign-in-only. Shown once. Never
reveals a protected location. → [Sharing](features/sharing-and-public-pages.md)

**Survey source** — The `.th`, `.svx`, `.thconfig`, `.log` or survey-app `.zip` a compiled
survey was made from. Archived, never read.
→ [Surveys and models](features/surveys-and-models.md#survey-sources)

**Trip purpose** — What a trip was for. Also decides what its report asks for and which
checklist it works through.

**Visibility** — A record's own audience: private, caving group, authenticated users, public.
One of the three things that decide access.

**Work area** — Ground your club works, drawn as an area and nested, shown with what contains
it so two passages of the same name are told apart. → [Work areas](features/work-areas.md)

---

[← Back to the guide](README.md)
