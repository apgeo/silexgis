# Relations graph

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/relations-graph.md)

[← Feature reference](README.md) · Related:
[Links between records](links.md)

---

The relations graph draws **one item and everything related to it** as dots and lines, so you
can see how a cave, a trip, a report or a person sits among the rest of your records. You open
it from an item and explore outwards one step at a time.

It only **shows** relations. Nothing is created, changed or deleted from the graph; you make and
edit relations where you always did, on the item's own page.

---

## Opening it

| From | How |
|---|---|
| Any page with a links section (a cave, a document, a trip, a camp, …) | **Show as graph** in the section's header |
| The map's selection panel for a cave or another feature | **Show as graph** beside the other buttons |
| The side menu | **Tools → Relations graph**, then search for the item to start from |

Opening it from an item makes that item the **starting item**.

## What the graph shows

Each item is a coloured dot with a small picture of what it is: a cave, an entrance, a survey
line, a pin for any other place, a document, a trip, a person, a club, a map view, a survey model,
a geodata file, a cabinet, an expedition's tent, a tag or an attached file. An item you may not
open is an empty dashed ring with a padlock, and has no name.

At first: the starting item and what is directly related to it. To go further, choose a
neighbour and press **Expand selected** (or double-click it). Everything already on screen stays
where it is, and the new items appear beside the one you expanded.

Every kind of relation is drawn, in its own colour:

> Links between resources · Links between features · Hierarchy · Attachments · Trip
> participants · Expeditions · Cabinets · Tags · Group membership · Survey models

When an item has more relations of one kind than fit in one step, a **+N more** dot is drawn
beside it. Press it to load the next batch. A **Legend** under the drawing explains the pictures
and colours.

Select an item, then **Details** in the toolbar opens a drawer with the item's title, a button that
opens its page, and the relations it has in the graph. Selecting a line shows how the two ends
are related, and for a link, a button to open the link's own page.

**Collapse to the first item** goes back to the starting item. **Remove from graph** hides one
item you do not want to look at; it deletes nothing.

## Filters

The **Filters** panel lists every kind of relation and every kind of item that is loaded, each
with a count. You can switch **several on or off at once**:

- untick a kind of relation to hide its lines, and any item that is then cut off from the
  starting item;
- open a kind to switch off a single relation within it, such as one link relation;
- untick a kind of item to hide all of that kind;
- **Only** keeps just one kind and hides the others; **Show all** brings everything back.

Filters hide, they do not delete: switch them back and the items return. The counts describe
what is loaded so far, not everything the register holds.

## Search

- **Search the graph** finds items already in the graph by title or subtitle. Capitals and
  accents do not matter. Matches are highlighted, and **Enter** moves to the next one.
- On the empty page, **Find an item to explore** searches the whole register for a starting
  item. You only see items you are allowed to read.

## The list beside the drawing

The **Items in the graph** list shows the same items grouped by kind, each with its relations.
It is the way to use the graph with a keyboard or a screen reader: choosing a row is the same
as pressing the dot, and it has its own **+N more** buttons. It follows the filters.

## Restricted items

If a link joins your item to something **you may not see**, the graph draws a grey
**Restricted** dot. It has no title and **cannot be expanded**; the graph tells you only that
the link has one more member. Two links that end at different hidden items show two dots, and
what is behind them is not revealed.

## Why something does not appear

- **You may not read it.** Items outside your access are never drawn, and they are not counted in
  "+N more" either.
- **A filter hides it**, or hides the only route to it. Check **Filters**.
- **It is further away.** Only one step is loaded at a time; expand the neighbour.
- **Its location is protected.** The graph carries no coordinates, and a protected cave's
  position-revealing relations stay hidden from anyone who may not see its exact position.
- **It is not a relation.** Two records that merely sit near each other on the map are not
  related unless a link, a hierarchy or one of the listed relations says so.

## What the graph never does

- It does not change any record.
- It does not show anything you could not already open.
- It does not show positions or coordinates.

---

Related: [Links between records](links.md) · [Search and filters](search-and-filters.md)
