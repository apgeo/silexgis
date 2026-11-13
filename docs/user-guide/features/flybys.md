# Flybys in the 3D view

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/flybys.md)

[← Feature reference](README.md) · Related: [3D view](3d-view.md) ·
[A movie of a tracked trip](tracking-movie.md)

---

A flyby flies the camera of the 3D view for you: in to a cave and around it, from one cave to
the next, around a mountain, or from the survey's plan down to its profile. You choose what it
is about and which ready-made flight to use, press **Play**, and watch.

It is offered wherever the 3D view is — the **3D view** page, the scene beside the map, and the
scene in a window of its own — to anybody who is signed in.

> **What a flyby can do today.** Play in the view, play on a clean screen for you to record
> with another program, and be kept and exchanged as a file. Changing the background maps and
> the caves' colours during the flight, and saving the flight as a movie from SilexGIS itself,
> are not built yet.

---

## Opening it

Press **Flyby** among the camera buttons at the right edge of the scene (on a phone it is in
the camera menu). A panel opens on the left, and a bar to play from along the bottom. Press the
button again, or the ✕ of the panel, to close it.

## What to fly around

| Choice | The flyby is about |
|---|---|
| **What is selected** | The cave, the entrance's cave, or the area (a massif, a karst area, a hill) selected on the map or in the scene |
| **Caves in view** | The caves the scene is drawing now, ticked in the order you want them visited. **Refresh the list** reads the view again after you have moved; **Shortest route** puts the ticked caves in the order that makes the camera travel least |
| **What the view shows** | Whatever the scene is centred on when you press Play — a ridge, a valley, a group of caves — with no need to select anything |

A cave is found from what the scene draws of it: its survey lines when they are loaded, its
entrances otherwise. A cave that is not drawn at all is looked up, the camera goes to look at
it so the scene loads it, and the flight is built from what is then drawn.

## The ready-made flights

| Preset | What the camera does |
|---|---|
| **Around a cave** | Flies in, circles the cave and, if asked, goes underground to its lowest point and back |
| **Cave tour** | Visits each cave in turn — over to it, around it — and ends above all of them |
| **Around several caves** | One circle wide enough to hold all of them |
| **Around a mountain** | Circles the area from above its ridges, looking in |
| **Plan to profile** | Starts straight above, as on the survey's plan, tilts down to the side view and turns |

Only the presets that fit what you chose are offered: a tour needs at least two caves.

## Options

| Option | Does |
|---|---|
| **Turns around each** | How many times the camera goes round |
| **Seconds per turn** | How long one full circle takes |
| **Direction** | Clockwise or counter-clockwise, seen from above |
| **Go underground to the cave's lowest point** | Adds a descent below the surface and the way back up (for *Around a cave* and *Cave tour*) |
| **Speed of the whole flyby** | ×0.25 to ×4: the same flight in more or less time |
| **Load the route first** | Before playing, the camera visits the flight's key points and waits for the map and the caves to load at each, so the flight itself does not show them arriving. It takes a little while and is worth it; switch it off for a quick look |

Above the ground the camera keeps a clearance over the terrain, so a circle around a cave on a
slope does not pass through the hillside. That limit is lifted only for the underground part.

## Sections

Open **sections** to see the flight as a list — approach, circle, over to the next cave, down
into it, back up — each with its length. Every section has its own **seconds** and its own
**speed** (×0.25 to ×4), so one part can be slowed without touching the rest. The section being
flown is highlighted while the flyby plays.

Changing what the flyby is about, its preset or its options builds the sections again, and the
panel says when the sections on screen are ones you opened or changed by hand.

## Playing

| Control | Does |
|---|---|
| **Play / Pause** | Starts the flyby, or holds it where it is |
| **Stop** | Ends the flyby and puts the camera back where it was before |
| **Position** | Drag it to any moment, forwards or backwards, playing or not |
| **Repeat** | Plays again from the start when it ends |
| **Clean screen** | See below |

While the flyby is getting ready the bar says what it is doing — *Finding the caves*, *Loading
the route* — and **Stop** cancels it.

**Pressing or scrolling on the scene pauses the flyby**: the camera is yours again. **Play**
puts it back on its path and goes on.

While a flyby has the camera:

- the layers button is held, and so are the choices the flyby is built from — stop it to change them;
- the flat map beside the scene does not follow the camera, and the address bar keeps the
  position you had before, so a link copied during a flyby is not a link to a moment of it.

When you stop the flyby or close the tool, the camera returns to where you had it.

## Clean screen — for recording with another program

**Clean screen** shows the scene alone: no buttons, no panel, no notices — only the map credits,
which the map sources' licences require on any picture of their tiles. The view takes the whole
screen where the browser allows it. After a three-second countdown the flyby plays from its
start, so you can start your screen recorder first.

- **Space** pauses and resumes.
- **Escape** returns to the ordinary view, with the flyby held where it was.
- Pressing on the scene does nothing here, so a stray click cannot spoil a recording.

## Keeping a flyby

| Control | Does |
|---|---|
| **Name** and **Save** | Keeps the flyby in this browser under that name; saving under a name already used replaces it. Up to 50 are kept |
| **Open a saved flyby** | Brings one back, with its sections, speed and repeat setting |
| **Delete** | Removes the kept flyby whose name is in the name field |
| **Save to file** / **Open from file** | Writes the flyby as a small `.flyby.json` file, and reads one — to move a flyby to another browser or send it to somebody |

A flyby names its caves and areas by reference, not by position. Opened by somebody else, it
flies around what *they* are allowed to see: a cave they cannot read is reported as not found,
and a cave whose exact place is protected is flown around at the approximate place they are
shown. Flybys are kept in the browser, not on the server; clearing the browser's data for the
site removes them, which is what the file is for.

## Things worth knowing

- **Walls.** The walls shown are the ones the scene would show anyway: of the selected cave,
  or of every cave in view if that is switched on in the layers. A tour of caves you have not
  selected shows their survey lines.
- **A slow machine.** A flyby plays in real time. On a machine without a graphics card the
  scene draws only a few frames a second and the flight looks like it — the camera is always in
  the right place for the moment, it is just seen less often.
- **A long tour.** Preparing visits every cave that is not yet drawn, then the route. Twelve
  caves with the route loaded can take a couple of minutes before the first frame.
