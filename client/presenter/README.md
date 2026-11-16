# Presenter — scripted demonstration movies

Plays a **scenario** in the real application, the way a person would use it, and makes a movie of
it — or just plays it while you record the screen yourself.

```
cd client
node presenter/record.ts presenter/scenarios/grind-3d-tour.ts --target silexgistest            # movie
node presenter/record.ts presenter/scenarios/grind-3d-tour.ts --target silexgistest --draft    # quick check
node presenter/record.ts presenter/scenarios/grind-3d-tour.ts --target silexgistest --mode play
```

Needs Node 24, the client's dependencies, and ffmpeg (on the PATH, or Docker to run the
`linuxserver/ffmpeg` image). The movie is written to `presenter-output/<scenario>/<time>/movie.mp4`
(or under `$PRESENTER_OUT`, or `--out <dir>`), with `run.json` beside it.

Read `run.json` before using a movie. `unsettledFrames` counts pictures taken before the scene
finished drawing. `failedRequests` lists every other server (map tiles, terrain) that refused or
dropped a request, with the frames it happened on. A map server that fails leaves the ground bare,
but the scene still counts as finished, so only this list shows it. Re-record the affected stretch
with `--from`/`--to --keep-frames` and splice it in.

## Modes

| Mode | What happens | When |
|---|---|---|
| `step` (default) | One picture per movie frame, each taken after the 3D scene has finished drawing every tile and model and its data has stopped loading. Encoded to H.264 MP4. | Any machine. Smooth however slowly the scene draws; on a machine without a graphics card, about 30–60 minutes per minute of 1080p30 movie. |
| `live` | The scenario plays in real time and the browser's frames are kept, then re-timed to a constant frame rate. | A machine that draws the scene at the movie's frame rate (a real graphics card). |
| `play` | The scenario plays in real time in a visible window; nothing is kept. | You record the screen with your own tool. |

Other switches: `--draft` (1280×720, 8 frames a second, for timing), `--fps`, `--width`,
`--height`, `--from <s>` and `--to <s>` (record part of it), `--keep-frames`, `--headed`,
`--probe` (set up, print what the scene holds and save one picture at `--from`), and `--app <dist>`
(serve a locally built client in place of the site's own scripts, to try a client change before it
is deployed).

`PRESENTER_DEBUG=1` prints, for every frame, how long the scene took to settle and the picture
to be taken.

`--target silexgistest` signs in with that instance's published test administrator. Any other
target (`local` for `http://localhost:8080`, or a URL) takes its account from `PRESENTER_EMAIL` and
`PRESENTER_PASSWORD`.

## Scenarios

A scenario is plain data (`presenter/scenarios/*.ts` exporting it, or a `.json` file). Its `setup`
is done before the first frame and not filmed; its `items` run on one clock, in seconds, and may
overlap — that is how "the camera turns while the basemap changes" is written. Two items that steer
the same thing (two camera moves, two pointer actions) may not overlap; the recorder refuses such a
scenario before it starts.

Item kinds (`client/src/presenter/scenario.ts` is the reference):

- `camera` — moves to a pose described **around the drawn cave model**: `heading`/`headingBy`,
  `pitch`, `range` (in radii of the model's bounding sphere), `lift`. A zero duration jumps; the
  first one must be at 0 s. A range below about 1 with a low pitch puts the camera underground.
  `aboveGround` keeps the eye that many metres above the ground (null lifts the limit). The range
  changes in equal proportions over equal times, so an approach from far away keeps its pace.
  Camera moves given `ease: 'smooth'` and placed back to back are flown as one curve through their
  poses, never stopping at the joins: a fly-in that runs straight on into circling the cave.
- `modelColor` — recolours the cave's walls from their present colour to `to` (`#rrggbb`, or
  null for their own colours) over the item's duration, mixed in by `strength` so the shading
  still shows. Done through the recording handle, not the interface.
- `caption`, `title`, `fade` — drawn over the picture.
- `layerPanel`, `basemap`, `layer`, `basemapOpacity` — done **through the interface** with a
  visible pointer: open the 3D layer panel, pick a basemap (opening its group), tick a layer, drag
  the basemap's opacity slider through a list of percentages.

Setup: `cave` (opened in 3D with its walls; among caves matching the name, one that has walls),
or `file` (an imported geodata file, found by name, opened in 3D with the file drawn: the camera
poses are then described around its extent, whose middle on the ground is the centre and half its
diagonal one radius), `ground` (a terrain build to draw instead of the default, picked in the
layer panel's Ground list by text its row contains, such as `Level 13`), `basemap`, `layersOff`,
`layersOn`, `hide` (CSS selectors left out of the picture).

## How it works

The application offers a small recording handle on the window **only when the browser is driven
by automation** (`navigator.webdriver`): set the camera, read where the drawn models are, tint
them, and wait until the scene has finished drawing. The scenario runner (`client/src/presenter/`) is bundled by
the recorder and put into the page; it moves the camera, the pointer and the overlay from one
clock, which the recorder either steps frame by frame or lets run in real time.
