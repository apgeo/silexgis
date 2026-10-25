# Photographs and albums

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/photographs.md)

[← Feature reference](README.md) · Workflow:
[Turn a trip's photographs into places](../workflows/photographs-to-places.md)

---

**Library → Photographs** is the gallery: everything you may see, searchable and filterable.
**Library → Albums** is the curated arrangements made of them.

The gallery sits beside the map data rather than under the filing tree, on the principle that
photographs are *browsed* and paperwork is *filed*.

---

## The gallery

Search **captions and places** — accents do not matter, so a word typed without its
diacritics finds the word written with them. Filter to those **without a position**, or to a
**position** — and if you arrived from the map, it says *"Showing photographs inside the map
extent you came from"*. An extent holding more photographs than one search carries says so rather
than looking complete: *"Not every photo in this map extent is listed here — it holds more than one
search can carry. Zoom in on the map to see the rest."*

Open one and you get a viewer with **previous / next**, **zoom in / out**, and an
**About this photograph** panel: the photographer, the place, the licence, and what the
camera recorded.

### Conditions and saved gallery views

Beside the search, the tag and the dates, **Conditions…** opens the
[condition editor](search-and-filters.md#the-condition-editor) as **Photograph conditions**:
caption, place name, the day it was taken, the day it is dated (taken, else uploaded), tags and
the rest, in AND and OR groups. The button reads **Conditions… · on** while any are applied.

A set of conditions can be kept as a **saved gallery view** — a
[saved filter](search-and-filters.md#saved-filters) about photographs:

- **Save as view…** keeps the search, the tag, the dates and the conditions;
- **Apply a saved gallery view…** picks one of yours, or one shared with your caving group;
- **Saved filters** opens the list of all your saved filters.

A saved view does **not** keep the photographer, the album, *"without a position"*, or what a
link opened the gallery on — a cave, a trip, a camp, a map extent. Those are set afresh each
time. Like every saved filter, a shared gallery view grants nothing: each person who applies it
sees only the photographs they may see.

If the conditions cannot be applied, the gallery says so: change them, or stop naming the saved
view.

### Selecting and acting in bulk

**Select** turns on multi-select. With a selection you can:

- **Rotate** — corrects a whole card of sideways pictures in one press,
- **Add to album…**,
- **Delete** — *"They leave every listing at once and can be restored for a while
  afterwards."*

A bulk action that partly fails says so: *"n changed; m could not be."*

### Credit and licence

Per photograph: a **caption**, a **photographer** (chosen from the caver roster, or a name
typed in for somebody else), and a **licence**:

CC0 (public domain) · CC BY · CC BY-SA · CC BY-NC · CC BY-NC-SA · CC BY-ND · CC BY-NC-ND ·
All rights reserved · *Nobody has said*

Set these before you publish anything. *Nobody has said* is an honest default and a bad thing
to publish under.

### Downloading the original

**Download the original** is there — unless the photograph's own capture point would place a
cave whose exact position you may not see. Then you are told:

> *"You may not download the original of this photo: it records where it was taken, and you
> do not..."*

The rendering is still shown. See [Location protection](../admin/location-protection.md).

Where the application can rewrite the picture, you are not refused: you get the **full-size
picture with its location data taken out** (the GPS position and any place names written into the
file; camera and exposure details stay), and the picture is marked **Location removed**. The same
happens to everybody when the administrator has switched off *Keep the location inside photo
files*. The stored file is never changed — people who may see where the photograph was taken
still get it exactly as it was uploaded.

Videos, recordings and pictures in formats the application cannot rewrite have no such copy; for
those the original is still refused unless the administrator has allowed it.

---

## Albums

**New album**, give it a title, add photographs from the gallery.

Inside an album you can:

- **arrange the order** — drag a photograph where you want it, or use **Move earlier** /
  **Move later**,
- **choose a cover** — *Use as the cover*,
- **take a photograph out** without deleting it.

A large album says *"Showing the first n of m photographs. Arranging applies to these."*

Deleting an album does not delete the photographs in it, and says so.

### Sharing an album

An album can be handed out by a **share link**. The link is shown **once** — copy it then;
you cannot recover it, only revoke it and mint a new one.

---

## The public gallery

A separate decision from who may read a photograph inside the application:
**Show in the public gallery** puts it on the page anyone can reach without signing in.

That page (`/gallery/public`) shows renderings and nothing else. Withdrawing a photograph
takes it straight back off.

---

## Photographs elsewhere in the application

**On a cave, feature or trip.** Attach them directly. One can be starred as the
**headline picture** — *Use as the headline picture*.

**On a trip.** A trip has its own gallery, showing *"the photographs you may see. Somebody
else may see more, or fewer."*

**On a camp.** A camp's *Photographs* tab shows the pictures filed against the camp and the
ones on the trips it gathers that you may read — again *"the photographs you may see"*, because
a trip you may not open contributes none. *Open in the gallery* narrows the gallery the same way.
Albums about the camp are on its *Files* tab.

**On the map.** The **Geotagged photos** layer draws photographs that carry a position. Where a
view holds more than the layer draws at once, the layer panel says *"Not every photo is shown here
— zoom in to see the rest."* while the layer is on.

**In a cave's survey.** A photograph taken underground has no position of its own, so its place is
the survey station it is linked to. The survey viewer and the 3D view mark each such station with a
camera; press it to see that station's photographs. See
[Surveys and models](surveys-and-models.md#3d-survey-models) and
[3D view](3d-view.md#photographs-at-their-stations).

**As places.** A memory card can become records on the map — see
[Turn a trip's photographs into places](../workflows/photographs-to-places.md). That
workflow reads the camera's fix, matches unplaced pictures against a track by time, draws the
direction the camera was facing, and offers what is already nearby.

---

## The photo library

Some installations run a separate photo library beside them — **Immich** or **PhotoPrism**, a
product of its own with its own accounts. **Library → Photo library** looks through it from
inside SilexGIS. Nothing there is copied into the gallery above: the pictures stay in that
library, under that library's rules.

**Who sees it.** The page is in the sidebar only when the installation has a photo library it
is currently using, and only for the accounts the installation lets reach it. That is one
installation-wide setting, not a permission rule: as shipped it is
[Full Administrators](../glossary.md) only, and it can be opened to every signed-in account
(the `SILEXGIS__PhotoLibraries__Audience` setting in the
[configuration reference](../../INSTALL.md#configuration-reference)). An administrator can stop
the installation using a library for a while — see
[Messaging → Photo libraries](../admin/messaging.md#photo-libraries).

**What you can do there.**

| | |
|---|---|
| **Choose the library** | Only when more than one is connected |
| **Search** | The box says what the library does with your words: *Search titles, captions and labels* looks the words up in what somebody wrote down; *Describe the picture* puts the whole library in order of how close each picture is to your description, so there is no count of what matches. The sentence under the box says which |
| **Narrow to an album** | *The whole library*, or one of the albums the library keeps. A search always goes to the whole library, and the page says so when an album is chosen |
| **Page through** | **Previous** · **Next**, with how many photographs the library holds when it says |
| **Open a photograph** | Its title, when it was taken, the camera, lens, aperture, exposure, ISO and focal length, and which library holds it. A video shows only a still |

> *"Where these pictures were taken is not shown here. The map is where a library's photographs
> are placed."*

**Elsewhere.** The same library also appears as a map layer, **Photo library (…)**, which draws
the pictures that carry a position and offers **Create a feature here** from one of them to whoever may create features; and on
a trip, as **Photographs from the photo library** — the pictures it holds from the days the trip
was out, a day either side included because a camera's clock has a time zone and a trip's dates
do not.

If the library does not answer, the page says so rather than showing an empty grid.

---

## What is never done to your files

- Nothing is ever **written back into a photograph file**. Positions, names and decisions
  live in the records. (A reader who may not be given a photograph's location is handed a
  separate copy with it removed; the stored file keeps it.)
- A photograph published or shared goes out as a **rendering**, with camera metadata
  stripped — never the original.

---

Workflow: [Photographs to places](../workflows/photographs-to-places.md) ·
Related: [Uploads](uploads.md) · [Sharing](sharing-and-public-pages.md)
