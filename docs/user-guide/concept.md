# What SilexGIS is

🇬🇧 **English** · 🇷🇴 [Română](ro/concept.md)

[← Back to the guide](README.md) · Next: [First steps](first-steps.md)

---
cece
## In one paragraph

SilexGIS is a web application for storing, viewing and editing cave and karst data. A caving
club, a small exploration team or an individual installs it on a server they control, creates accounts
for the people who should have them, and from then on it is that group's cadastre, archive,
map and logbook in one place. It is free software (AGPL-3.0-or-later), and it comes from a
real club — Silex Brașov — which had all of this on paper and in spreadsheets first.

## It is not a public website

This is the single most important thing to understand about how it behaves, because it
explains a dozen decisions that would otherwise look paranoid.

The application is **default-deny**. You have to be signed in to reach almost anything.
Account creation is closed unless whoever runs the installation deliberately opens it. There
is no browsing mode, no "public map", no directory of caves a stranger can page through.

What a visitor *without* an account can reach is a short, named list of doors, and each one
is opened by a deliberate act:

- a **share link** somebody minted and handed out,
- a **published album** or the installation's curated **public gallery**,
- a **QR code** printed on a label at a cave, which confirms only that the code is registered
  and says nothing else,
- the **unsubscribe** page from a notification email.

An installation that publishes nothing shows a visitor the sign-in page and nothing more.

## Why so careful about locations

Caves get vandalised, looted, and killed by traffic. In a lot of places the position of an
entrance is genuinely dangerous information. So the application treats **where a cave is** as
a separate thing from **that a cave exists**, and it can hand out the second without the
first.

A cave marked as having a *protected location* is still readable — its name, its
description, its documents, its history. But its exact coordinates are withheld from anybody
without the specific right to see them, and the withholding happens on the server, in every
path that could leak it: the map, the tables, the exports, the shared views, the photographs
(whose own capture position would give it away), the notification emails, the reports.

This is covered in full in [Location protection](admin/location-protection.md). It is worth
reading even if you are not an administrator, because it explains why two people looking at
the same screen can honestly see different things.

## What it holds

A rough inventory of the kinds of thing that live in an installation:

- **Caves** — with names and other toponyms, an identification code, localization
  (basin, valley, nearest address), geology, morphometry, discovery data, protection class,
  exploration status, and one or more **entrances** with precise coordinates.
- **Surface features** — sinkholes, faults, springs, walls, karst areas, anything you can
  draw. Typed, symbolised, with properties of their own.
- **Surveys** — Therion `.lox` and Survex `.3d` models, cave walls as `.stl`, extracted
  centerlines projected onto the map and drawn in 3D, and an archive of the raw survey
  sources they were compiled from.
- **Geodata** — GPX, KML/KMZ, shapefiles, GeoJSON, WKT, CSV of positions; and georeferenced
  raster maps served as overlays.
- **Photographs** — a gallery, albums, credit and licence, and a route from a memory card
  to actual records on the map.
- **Documents** — the club archive: reports, permits, bulletins, scans, audio, video —
  filed in a tree of **cabinets**, searchable by what is written inside them.
- **Trips** — planned, run, and written up, with who was there, what they did, what it
  measured, and what it left open.
- **Events and camps** — the club's meetings, training and expeditions.
- **People** — accounts, a roster of cavers (including ones with no account), and caving
  groups.

## What it is deliberately not

- **Not multi-tenant.** One installation serves one group. There is no notion of separate
  organisations sharing a server.
- **Not a survey processing package.** It displays Therion and Survex output; it does not
  replace Therion or Survex. It keeps your sources so you can recompile later.
- **Not offline-first in the browser.** The web client expects a connection. Offline is what
  the companion phone app is for — see [Mobile and offline sync](features/mobile-sync.md).
- **Not pixel-perfect on mobile.** It works on a phone, genuinely, including drawing
  geometry with a finger. But it is designed for a large screen and it shows.

## Who does what

There are no fixed job titles. Instead there are **permission groups** — named sets of rules
that somebody adds you to — plus what you own, plus the visibility each record carries. In
practice most installations end up with something like:

| Kind of person | Typically can |
|---|---|
| A member | Read what the club shares, log their own trips, upload photographs, answer invitations |
| An editor / surveyor | All of that, plus create and correct caves, features and surveys |
| An archivist | Plus file documents into cabinets and curate the library |
| A committee / access officer | Plus see exact locations of protected caves |
| A full administrator | Everything, including accounts, permissions and installation settings |

Those are conventions, not built-in roles. See [Permissions explained](admin/permissions.md).

## Where the application came from

| Version | Years | State |
|---|---|---|
| v1 | ~2014–2022 | PHP/MySQL, the club's original working tool, still live |
| v2 | 2022 | A partial React/Laravel rebuild, abandoned |
| **v3** | 2026– | This one: a full rebuild, the version this guide describes |

v3 is a fresh implementation. Where it does something differently from v1, that is usually a
decision rather than an omission.

---

Next: [First steps](first-steps.md) · [Core concepts](core-concepts.md)
