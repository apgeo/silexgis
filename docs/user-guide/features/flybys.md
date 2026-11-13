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

> **What a flyby can do today.** Play in the view; change the background maps, the layers and
> the caves' colours during the flight; write a title, captions and an end card over it; play on
> a clean screen for you to record with another program; be written to a movie file by SilexGIS
> itself; and be kept and exchanged as a file. Not built yet: stretching the relief during a
> flight, and more ready-made flights.

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

## How the scene looks, and what is written over it

Below the sections, four more parts of the panel. None of them changes your own layer settings:
what a flyby shows is shown in the scene alone, the flat map beside it is not touched, and when
the flyby stops the scene is exactly as you had it.

**Look at the start** — how the scene looks when the flyby begins. Each setting is either set
here or left **as you have it**.

| Setting | Does |
|---|---|
| **Background map** | The background the flyby opens on; only those the 3D view can draw are offered |
| **Set how strongly the background is drawn** | Below 100 % the ground fades and the caves under it show through |
| **Sky colour** | The scene draws no sky of its own — above the horizon is black. A flyby built from a preset opens with a soft blue sky; untick it for black |
| **Colour the cave walls** | The walls mixed with one colour, by a strength; their shading is kept |
| **Survey lines in one colour** | Instead of their colours by depth |
| **Ground over the caves** | Drawn over the caves, or cut away above the cave |

**Ready-made changes** — sets of timed changes written in one press. Adding a set again replaces
the one added before rather than doubling it.

| Set | Does |
|---|---|
| **Cycle the backgrounds** | The backgrounds you pick, one after another for so many seconds each, cut or cross-faded, starting again from the first until the flyby ends |
| **See through the ground while circling** | During each circle the background fades to the strength you give and back |
| **Colour the caves** | From the start of the section you pick, the walls and the survey lines in one colour |
| **Old map, new map** | A georeferenced map fades in over the background during a section and out again |
| **Features in turn** | The filter layers you pick, shown one after another; all of them are hidden when the flyby opens. Only layers already on the map's list of filter layers are offered |

**Changes** — every change as a line in words: what it does, the section it belongs to, the
seconds into that section it starts, and how long it lasts. Correct the numbers or remove a
change. A change belongs to its section, so making a section slower or faster moves its changes
with it. Survey lines and the ground over the caves change at a single moment, not gradually. Two
changes of the same thing at the same time are marked, and the flyby will not play until one is
moved.

**Texts over the picture** — a **title** (with a second line) shown from the first moment, a
**caption** for any section, shown along the bottom while it is flown, an **end card** shown to
the last moment, and the picture rising out of black at the start and sinking into it at the
end. They are drawn over the scene in proportion to its size, and they are part of what the
clean screen shows.

Dragging the position backwards undoes what the flyby changed after that moment, exactly as
playing forwards made it.

## Playing

| Control | Does |
|---|---|
| **Play / Pause** | Starts the flyby, or holds it where it is |
| **Stop** | Ends the flyby and puts the camera back where it was before |
| **Position** | Drag it to any moment, forwards or backwards, playing or not |
| **Repeat** | Plays again from the start when it ends |
| **Clean screen** | See below |
| **Make a movie** | See [Making a movie](#making-a-movie) |

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
- The scene is boxed to the shape of the movie frame last chosen under **Make a movie** (16:9
  until you choose another), with dark bars round it, so a recording of the screen has the shape
  you want.

## Making a movie

**Make a movie** (the camera button on the bar) writes the flyby to an **MP4**, **WebM** or
**GIF** file in the browser — nothing is sent to the server. The flyby panel gives way to the
movie panel, and the scene is **boxed to the shape of the movie's frame** beside it, with dark
bars round it: what you see is what the file will hold. You can still play the flyby and drag its
position to look at any moment.

Every frame is drawn for its own moment of the flyby and handed to the file with its exact time,
so the movie runs smoothly however slowly your computer draws the scene. A slow computer makes a
slow export, never a jerky movie. The one exception is **Live recording**, below, which you choose
when you would rather have the file in the flyby's own time.

| Setting | Does |
|---|---|
| **Ready-made files** | *HD video* (1280 × 720, 30 frames a second — MP4 where the browser writes it, WebM otherwise), *Reels / TikTok* (1080 × 1920 MP4), *Square post* (1080 × 1080 MP4), *For a chat* (a small GIF). A ready-made file sets the format, size, rate and quality only |
| **Format** | MP4, WebM or GIF — only what this browser can write is offered. A browser with no video encoder makes GIFs only, and says so |
| **Frame size**, **Frames a second**, **Quality** | The same sizes and rates as the movie of a tracked trip. A GIF is at most 800 pixels wide and 600 frames long |
| **How each frame is taken** | **Quick**: each frame is taken as soon as it is drawn, as the live view would show it, map tiles still arriving included. **Fine**: before each frame the scene finishes loading what it shows — no half-loaded ground in any frame, but seconds a frame on a computer without a graphics card. **Fine, larger than the screen**: as Fine, and the scene is drawn at the movie's own size even when that is larger than the box on the screen — the sharpest file, and the slowest. Quick and Fine draw the scene no larger than the box and scale the frame up. **Live recording** — offered only in browsers with a recorder of their own (Chrome, Edge, Firefox, Safari) — see [Live recording](#live-recording) |
| **Draft** | Half the size, 8 frames a second (10 for a GIF), taken quickly: to check the timing of a flyby in a fraction of the time |
| **What to film** | The whole flyby, or a stretch of it from one second of the flyby's clock to another |
| **Still at the end** | Holds the last moment for that many seconds |

Under the settings the panel says how many frames the movie will have, how long it runs and
roughly how large the file will be, and the name it will be saved under: the flyby's name when
it has one (`silexgis-<name>-<date>`), otherwise `silexgis-flyby-<date>`.

**Export** makes the file. The panel shows how far it has got and roughly how long is left;
**Cancel export** stops it, and nothing is saved. Closing the panel, or pressing Escape, while a
movie is being made asks first whether to stop it. However the export ends, the camera and the
scene are put back as they were.

After a **Fine** export the panel says how many frames were made, how long it took, and how many
frames — if any — had to be taken before the scene had finished loading, because a map server
was too slow.

A long video (more than about 256 MB) is written straight to a file you choose, as it is made,
in browsers that allow it (Chrome, Edge); elsewhere it is made in memory and then downloaded.

### Live recording

**Live recording** plays the flyby in real time and has the browser's own recorder film it,
texts and map credits included. It takes exactly as long as the flyby — no longer, however slow
the computer — but the file holds **what the screen showed, at the speed this computer drew it**:

- On a slow computer the movie stutters. A frame the computer could not draw in time is simply
  not in the file. After the recording the panel says how many frames were recorded and how many a
  second that came to, and warns when it is well below the rate you chose.
- **Frames a second** is the most the recording takes, not a promise.
- In some browsers (Chrome among them) the file has no length and no seek points: players show
  it without a total time and may not let you jump about in it.
- The file is MP4 where the browser records MP4, otherwise WebM. GIF is not offered, and **Draft**
  makes a frame-exact draft instead.
- The scene is drawn no larger than its box on the screen, and the frame is scaled up to the
  movie's size.

For a smooth file whatever the computer, use **Quick** or **Fine**.

- **The map credits are in every frame** and cannot be switched off: the map sources' licences
  require them on any picture of their tiles.
- **The movie shows where these caves are.** The panel says so beside Export, always: the file
  outlives the check that let you see them, so share it only with people who may see that.

## Keeping a flyby

| Control | Does |
|---|---|
| **Name** and **Save** | Keeps the flyby in this browser under that name; saving under a name already used replaces it. Up to 50 are kept |
| **Open a saved flyby** | Brings one back, with its sections, speed, repeat setting, look, changes and texts |
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
  selected shows their survey lines. A flyby can show or hide the walls, the scans and the
  station photographs, but not fade them: they appear and go at once.
- **A colour for every cave.** *Colour the caves* gives all the caves in the scene one colour. A
  colour for each cave of a tour is not built yet.
- **A slow machine.** A flyby plays in real time. On a machine without a graphics card the
  scene draws only a few frames a second and the flight looks like it — the camera is always in
  the right place for the moment, it is just seen less often.
- **A long tour.** Preparing visits every cave that is not yet drawn, then the route. Twelve
  caves with the route loaded can take a couple of minutes before the first frame.
