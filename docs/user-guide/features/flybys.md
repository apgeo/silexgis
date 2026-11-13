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
> with another program, be written to a movie file by SilexGIS itself, and be kept and
> exchanged as a file. Changing the background maps and the caves' colours during the flight is
> not built yet.

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
slow export, never a jerky movie.

| Setting | Does |
|---|---|
| **Ready-made files** | *HD video* (1280 × 720, 30 frames a second — MP4 where the browser writes it, WebM otherwise), *Reels / TikTok* (1080 × 1920 MP4), *Square post* (1080 × 1080 MP4), *For a chat* (a small GIF). A ready-made file sets the format, size, rate and quality only |
| **Format** | MP4, WebM or GIF — only what this browser can write is offered. A browser with no video encoder makes GIFs only, and says so |
| **Frame size**, **Frames a second**, **Quality** | The same sizes and rates as the movie of a tracked trip. A GIF is at most 800 pixels wide and 600 frames long |
| **How each frame is taken** | **Quick**: each frame is taken as soon as it is drawn, as the live view would show it, map tiles still arriving included. **Fine**: before each frame the scene finishes loading what it shows — no half-loaded ground in any frame, but seconds a frame on a computer without a graphics card. **Fine, larger than the screen**: as Fine, and the scene is drawn at the movie's own size even when that is larger than the box on the screen — the sharpest file, and the slowest. Quick and Fine draw the scene no larger than the box and scale the frame up |
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

- **The map credits are in every frame** and cannot be switched off: the map sources' licences
  require them on any picture of their tiles.
- **The movie shows where these caves are.** The panel says so beside Export, always: the file
  outlives the check that let you see them, so share it only with people who may see that.

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
