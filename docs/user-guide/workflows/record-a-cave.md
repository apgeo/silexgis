# Workflow: record a new cave

[← Workflows](README.md) · Reference: [Caves and entrances](../features/caves-and-entrances.md)

---

There are three routes in. Pick the one that matches what you actually have.

| You have | Use |
|---|---|
| A position and a name, sitting at a desk | **Route A** — the map |
| A GPS file with dozens of waypoints | [Import a GPS file](import-a-gps-file.md) |
| A memory card of photographs | [Photographs to places](photographs-to-places.md) |
| A paper file card and no coordinates | **Route B** — the form |

---

## Route A: from the map

This is the fastest way to put a real hole into the registry.

1. Open the **Map** and navigate to roughly the right place. The search box takes place
   names as well as record names.
2. **Right-click** where the entrance is → **New cave here**. (Or use the edit toolbar's
   *New cave here* tool and click the map.)
3. A form opens with the coordinates already filled in from where you clicked.
4. Give it a **name**. Everything else can wait.
5. Decide two things now, because they are awkward to change later in people's heads:
   - **Visibility** — private, caving group, authenticated, public.
   - **Protected location** — tick it if the exact position should be withheld from people
     without the specific right. See [Location protection](../admin/location-protection.md).
6. Save.

You now have a cave with one entrance. Everything below is refinement.

## Route B: from the form

**Caves → New cave**. Same form, without a position. You can add entrances afterwards from
the cave's page, either by drawing on the map or by typing coordinates.

## Filling the cave in

A cave's form is grouped into sections. None of it is mandatory beyond the name.

| Section | Holds |
|---|---|
| **Identification** | Name, other toponyms, identification code, cave type, description, website |
| **Localization** | Region, hydrographic basin, valley, tributary river, closest address, land registry number, location notes |
| **Geology** | Rock type, rock age |
| **Morphometry** | Surveyed and estimated length, real and projected extension, positive/negative/potential depth, altitude, volume, area, ramification index, cave age |
| **Status** | Exploration status, protection class, show cave and show-cave length |
| **Discovery** | Discovery date, discoverer |
| **Access** | Visibility, protected location |

Do not feel obliged to fill morphometry by hand. If you upload a survey, the application
computes those figures from the line work and shows them beside whatever is written in the
record — including when the two disagree, which it says out loud rather than picking a
winner. See [Measurements and statistics](../features/measurements-and-statistics.md).

## Adding the other entrances

From the cave's page: **Add entrance**. Each entrance carries:

- its own **coordinates** (drawn on the map or typed),
- **altitude**,
- an **entrance type**,
- a **position quality** — unknown, GPS, from map, estimated — which is worth being honest
  about, because it is the difference between "walk here" and "search this slope",
- **surveyed** date,
- a **main** flag; the main entrance is what several other things default to.

## What to attach next

Once the cave exists, its page grows sections as you feed it:

- **Photographs and documents** — drag them onto the page. A photograph can be starred as
  the cave's *headline picture*. See [Photographs](../features/photographs.md).
- **Centerlines and 3D models** — upload Therion `.lox`, Survex `.3d`, or walls as `.stl`.
  See [Surveys, centerlines and 3D models](../features/surveys-and-models.md).
- **Survey sources** — archive the `.th`, `.th2`, `.thconfig`, `.svx`, compilation `.log` or
  survey-app `.zip` the compiled survey was made from. Nothing reads them; they are kept so
  the survey can be compiled again when the tools have moved on.
- **Tags** — free-form, and filterable on tables and map layers.
- **Links** — to the report that describes it, to the cave next door, to the record that
  turned out to be the same hole. See [Links](../features/links.md).
- **Trips** — you do not add these here. A cave's *Trips* section fills itself from the trips
  that named it.
- **Printed codes** — if your club bolts QR labels to cave walls, this is where you publish
  or withdraw them. See [Sharing and public pages](../features/sharing-and-public-pages.md#printed-qr-codes).

## Correcting a mistake

Every change is recorded. A cave's **History** section shows what changed, when, and by whom,
and lets you **restore a single value** or **restore everything from before a change**.
Values that are part of a protected location are shown as hidden rather than exposed through
the history. See [History and audit](../features/history-and-audit.md).

## Two things worth deciding as a club, once

**When do you mark a location protected?** Decide the rule once and write it in your club's
own notes, because it is much easier to apply consistently than to fix afterwards.

**What goes in the identification code?** The application does not impose a scheme. If your
national cadastre has one, use it; the code is searchable and is what a QR label resolves
against.

---

Next: [Import a GPS file](import-a-gps-file.md) ·
Reference: [Caves and entrances](../features/caves-and-entrances.md)
