# Map services (WMS and WMTS)

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/map-services.md)

[← Feature reference](README.md) · Related: [Map workspace](map-workspace.md) ·
[3D view](3d-view.md) · [Work areas](work-areas.md) ·
Administration: [Map backgrounds](../../INSTALL.md#map-backgrounds)

---

A **map service** is a background or an overlay that is not a set of ready-made pictures stored
somewhere, but a server run by somebody else that is asked for the picture of whatever area is on
screen. National mapping agencies publish their topographic sheets, orthophotos and cadastral
maps this way, which is why this is how a club usually gets the "official" map of its country
behind its caves.

There are two kinds, and as a reader you cannot tell them apart and do not need to:

- **WMS** (Web Map Service) draws one picture for exactly the area and size asked for.
- **WMTS** (Web Map Tile Service) hands out the same map already cut into tiles, like the ordinary
  backgrounds.

Both are used the way every other background and overlay is. This page is about what is different
about them.

---

## Where to find them

In the layer tree of the [map workspace](map-workspace.md#the-layer-tree), under the group your
installation put them in (the shipped file uses a group called *Map services*). A service is a
**background** (one at a time, replacing the one before it) or an **overlay** (drawn over whichever
background is chosen, several at a time), and each has its own **opacity** slider. Which services
are there is decided by your administrator: the installation lists them, and an installation that
lists none shows none. The two examples that ship with the application, a Swiss federal map and
the Austrian basemap, are switched off, and cover only their own countries.

The same services are offered in the layer panel of the [3D view](3d-view.md), draped over the
terrain. What you chose is kept in a [saved view](saved-views-and-windows.md) like any other layer.

## What is different from an ordinary background

- **It depends on somebody else's server.** If the agency's server is slow, down, or unreachable
  from where you are (an installation or a phone with no connection to the outside), the layer
  stays blank or fills in slowly while the rest of the map works. That is not a fault in your
  installation. Try again later, or choose another background.
- **It stops at its own country.** A national service draws its country and nothing beyond; outside
  it the layer is simply empty. Zoom levels beyond what the service can draw are not asked for.
- **It can look different from one scale to the next.** A map service decides for itself what to
  show at each scale; the application does not restyle it.
- **It carries its source's credit and terms.** The credit the administrator recorded for a service
  travels with the layer. The terms under which an agency lets its map be used belong to the
  agency, and the administrator is expected to have read them.
- **It is drawn on the map and in 3D only.** It is never shown on the overview of
  [work areas](work-areas.md), and it is never copied into a document, such as the map in a trip
  write-up or a printed report: whether a service's terms let its pictures be reproduced is a
  question the application does not answer for the agency, so it never does.

## For the administrator

Services are listed in the installation's map-layers file, with a `kind="wms"` or `kind="wmts"`
entry giving the service's address and the few settings that describe it (which layer, which
grid). How to write the entry, the settings each kind takes, and the two switched-off examples
that ship as a model are in the installation guide under
[Map backgrounds](../../INSTALL.md#map-backgrounds). Three points that surprise people:

- The tile grid of a WMTS service is **named in the entry**, not read from the service's
  capabilities document, because the 3D view reads no capabilities and a background that depended
  on them would draw in one view and not in the other. Only the two common grids, web-mercator
  (`EPSG:3857`) and longitude/latitude (`EPSG:4326`), are supported.
- A change to the file takes effect when the application is restarted, and an entry that was
  switched on and is later deleted from the file stays on in the database. Switch a service off
  with `enabled="false"` rather than deleting it.
- An entry this version cannot draw is reported at start-up and stops drawing; it does not stop the
  application.

> **Not yet documented.** Where on screen a service's credit line is shown, and whether the
> installation keeps a service's pictures between visits. Ask whoever runs the installation if a
> particular service seems to go on showing an old map.
