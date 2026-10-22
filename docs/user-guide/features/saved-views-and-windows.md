# Saved views, panels and multi-window

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/saved-views-and-windows.md)

[← Feature reference](README.md) · Related: [Map workspace](map-workspace.md)

---

The application is built for a large screen, and for more than one of them. This page covers
saving a map configuration, popping panels into their own windows, and arranging the
selection panel.

---

## Saved views

A **saved view** is a named map configuration: its extent, its layers, its filters.

| Action | |
|---|---|
| **Save current view** | Name it and it is yours |
| **Share link** | Hand the view to somebody with no account — its link to copy, and the same link as a QR code |
| **Export map image** | A picture to paste into a document |

Your saved views appear on the **dashboard** under *My saved views*.

**A place typed into the map's filter is never saved in a view** — a typed point, a pasted
polygon, or the place *Nearest* is measured from. Saving such a view is refused with the reason:
*"…a saved view never stores one. Measure from an object or a named area instead, or apply a saved
filter."* See [Filters on the map](search-and-filters.md#filters-on-the-map).

**Sharing a view.** Pressing **Share link** on a view makes it shareable and shows its link. The
dialog says what the link shows, and warns when the view is narrowed by a filter, which leaves
the caves and features off the link — *"This view is narrowed by a saved filter or by a filter
from a link, which a link cannot run…"* — or draws layers from saved filters, which a link never
shows. A view keeps the same link: pressing **Share link** again shows it again.

A shared view opens for an anonymous visitor over the **background it was saved with** — or,
when that one needs an access key or is a source the installation keeps off links (the Google
layers, the non-commercial Sentinel-2 imagery), over the installation's default background.
If the view had them switched on, it also shows **cave entrances and surface features, on four
conditions**:

- **only records that are themselves Public.** A cave counts by its own visibility, and its
  entrances follow it; a feature counts by its own. A Private cave filed inside a Public karst
  area is *not* shown, even though every account can read it through the area;
- **only inside the view's own area**: what the saved view shows on a large screen, plus about
  one screen's width of margin on each side, and at most two zoom levels further out than it was
  saved at. The map on the shared page stops at that area;
- **narrowed as the view was**: a view saved with a tag filter shows only what the filter let
  through, and a view narrowed by a saved filter or any other filter shows no caves or features
  at all, because a link has nobody to run that filter as. Layers drawn from saved filters are
  never shown through a link;
- **the protections hold harder than for any account**: a protected cave or feature — protected
  itself, or lying inside something protected — is not shown at all, not even approximately, and
  is not counted in a cluster's number, even where your installation lets every account see it
  exactly. The page shows only the unprotected public records, as the camp map and the photo map
  do, so nothing on it tells that anything else is there. Nothing on the page identifies a record
  or opens it either.

Clicking a marker shows its name; clicking a cluster zooms in. Nothing on the page is drawn at
an approximate position. Survey lines, photographs, trips and imported files are never shown on a
shared view, and nothing about the saved view beyond its name, where it was looking and which of
the two overlays it had on is sent to the visitor.

**Deleting the view ends its link**, at once; the list of saved views has no separate control
for withdrawing a link while keeping the view. An administrator can also switch data on shared
views off for the whole installation (`SILEXGIS__Map__SharedViewData=false`), which leaves every
link showing its background only. If a link's view has been deleted, or never existed: *"This
shared view does not exist or was revoked."*

Saved views can also be [linked](links.md) to other records like anything else.

---

## Pop-out panels

Several parts of the workspace open in **a window of their own**, and stay synchronised with
the main window:

| Panel | |
|---|---|
| **Cave registry** | The table, beside the map |
| **Selection** | The details panel |
| **3D viewer** | The survey viewer (CaveView) |
| **3D scene** | The globe |
| **A link-annotated text** | To read beside the map it drives |

The canonical multi-monitor arrangement: **the map in one window, a synced table and 3D view
in another.** In the popped-out cave registry, pointing at a cave rings it on the map in the other
window, and picking something on the map marks its row — see
[Between a list and the map](search-and-filters.md#between-a-list-and-the-map).

**What you arrange in a pop-out window is kept on your account**, exactly as in the main window,
and a change made just before you close a page is kept too rather than lost with it.

The 3D viewer window shows *"Select a cave with a 3D model to display it here"* until you
select one in the other window. If the 3D scene is already open in another panel of the same
window, it says so rather than starting a second one.

---

## Arranging the selection panel

The details panel has sections: **Details · Tags · Files · Links · History · Permissions ·
Text**.

You can:

- **reorder** them — drag, or hold Alt and press the up/down arrows,
- **hide** a section individually, and show it again (hidden ones are listed),
- **restore the default arrangement**,
- set the **spacing** (density),
- **pin** the panel so it pushes the map aside, or **unpin** it so it floats over the map.

And you can **save named arrangements** and switch between them — one for surveying, one for
archive work. They are listed under **Saved arrangements**.

**Arrangements follow your account** to every browser you sign in to — the section order, what is
hidden, the spacing, the pin and the named arrangements alike. Your account keeps a limited amount
of such preferences; if it was full when you saved an arrangement, that one is kept in this
browser only and marked **This browser only** in the list, with the reason when you point at it.
Delete an arrangement you no longer use and the marked one goes to your account with that change.

If you hide everything it says so — *"Every section is hidden."* — rather than showing a blank
box.

## Navigating a selection

**Back** and **Forward** move through your selection history — *"Back to the previous
selection"*.

With several things selected: **n selected**, with *Readable here* / *Mixed* markers, and
**Zoom to all** and **Clear the selection** (**Tag all** is shown but not available yet). *"Hold
Ctrl or Shift while clicking the map to add to the selection."*

A selection filled from a list's **Select all found (n) on the map** holds at most 200, says so
when the list found more — *"The first 200 of n found by the list."* — and, while it is still
exactly that result, offers **Save as a filter…**. See
[Between a list and the map](search-and-filters.md#between-a-list-and-the-map).

If none of what you selected can be read here, it says so rather than showing an empty panel.

---

## The dashboard

**Dashboard** is the other whole-installation view:

- **Counts** — caves, features, trip logs, geodata files.
- **Recent activity** — what has been added, by kind (cave, trip log, feature, cave entrance,
  centerline, camp).
- **Coming up** — your next few trips. *"Nothing coming up. A trip appears here once somebody
  invites you to one or puts you on the party."* With a link to *All my trips*.
- **My saved views**.
- **Quick actions**, including **Import file**.

You can make the dashboard the page the application opens on — Settings → Accessibility →
*Open the app on*. That choice stays on the device you set it on.

---

Related: [Map workspace](map-workspace.md) · [3D view](3d-view.md) ·
[Sharing](sharing-and-public-pages.md)
