# Vendored CaveView.js runtime

Source: https://github.com/apgeo/CaveView.js — this project's fork of
https://github.com/aardgoose/CaveView.js (MIT license, see `LICENSE` in this directory).

Vendored build: distribution version **2.9.0-slx.15**, built from the fork's `silexgis`
branch at commit `5647a5fd` — the upstream **2.9.0 release tag** plus the fork's changes
(each also kept on its own dev-based `feature/*` branch so upstream can take them): the
dispose-handler typo fix, the `crsLookup` configuration option the app uses to resolve
coordinate systems locally instead of via epsg.io, a navigation and hover API
(`focusStation`/`focusSurvey`/`highlightStation` and a `stationHover` event), pictures
held for a station shown over the model, a toolbar of viewer controls for a host to place
outside the scene, markers a host maintains over a loaded model, and touch parity — a tap
reveals what a mouse reveals by hovering, both hover events carry the `pointerType` that
caused them, and controls are sized for whichever pointer is in use. Markers sharing a
station collapse into one, whose label the host chooses, so a party standing together no
longer draws its names on top of each other.

**What slx.7 to slx.9 added over the previously vendored slx.6**, none of which this
application calls yet — the upgrade is to stop the vendored copy drifting behind the fork,
not to take up a feature:

- `focusStation(ref, { keepView: true })` — centre a station by translating the camera
  rather than swinging it round, so a reader who chose a view keeps looking from that
  direction. Off by default, so every existing caller behaves exactly as before.
- `highlightStation(ref, { popup })` — re-assert a station's mark and popup after framing a
  survey section, which otherwise replaces the selection and closes the popup.
- A **trail** primitive — `addTrail`/`updateTrail`/`setTrailProgress`/`removeTrail`, drawing
  a route between named stations by walking the survey's own legs rather than joining the
  points with straight lines through rock, and reporting a pair the survey cannot connect as
  a named gap it draws dashed instead of inventing a path.
- A marker move that can be **timed** (`{ duration }`, zero meaning place rather than send),
  which is what lets a host scrub a replay without each step fighting the previous tween.
- A programmatic reveal, so a host can show what a hover would show without a pointer.
- A marker label's **plate and writing are worked out from the background** the viewer draws
  on, rather than being a fixed colour. A fixed black plate has no edge against the black
  background nearly every viewer uses, so the label it was added to make readable read as text
  floating in space. Measured composited at the default opacity: on black the plate lands at
  `#bebebe` with its writing 11.3:1 against it, and stays past 4.5:1 over survey grey, over a
  bright surface and over white. Both stay configurable — a theme naming a colour gets exactly
  that colour, and `'auto'` is what asks for the derivation.
- **A label's first line is drawn in the colour of the marker it belongs to**, where it has one, so
  an application putting a group on the first line and its members under it has them told apart —
  and two markers' labels no longer read alike. `liveMarkers.labelHeadingFromMarker` turns it off.
- A **Romanian catalogue** (`lib/lang-ro.json`). Vendored since slx.13, when this application
  started passing its interface language to the viewer: the viewer fetches `lib/lang-<code>.json`
  from this directory for any language but English, which is built in. The catalogue is a tracked
  file in the fork's `build/CaveView/lib/` that the build never writes, so it is copied by hand
  when vendoring — and a unit test (`src/caveview/vendoredRuntime.test.ts`) fails when the current
  directory lacks one for an interface language the application offers. Every viewer the
  application builds is given the language by one function (`caveViewerOptions` in
  `src/caveview/loadCaveView.ts`), because the catalogue is one object shared by all the viewers on
  a page: a single viewer built without the option chooses the browser's language and changes it
  under every other.

**What slx.12 adds over slx.11**, for rendering the viewer into a movie and for labelling people
by their own names:

- A **capture session** — `beginCapture({ width, height, background })`, `captureFrame({ azimuth,
  polar, advance, into })`, `endCapture()` and `capturing`. Frames are drawn synchronously, only
  when asked, at exactly the size given, opaque, and as the container shows the view: everything
  sized in pixels is scaled by `width / container width`, as on a display of that pixel ratio.
  While a session is open the pointer and keyboard move nothing, an auto rotation is suspended,
  camera animation is held, and the markers move only by the milliseconds each frame advances
  them — a move of 600 ms is half-way after 300 of advancing and has arrived after 600, however
  much real time passed. `endCapture()` puts back the size, pixel ratio, clear colour, controls,
  auto rotation and marker clock. The same state gives the same pixels.
- `getCameraAngles()` / `setCameraAngles({ azimuth, polar })`, reading and turning the camera
  at once without animation. The angles are worked out from where the camera is, so they are right
  straight after a move animated to one of the toolbar's views, to a station or to a model's first
  view — the orbit controls' own angles are those of their last update, which such a move does not
  make. A session also brings the viewer to its container's size if the container was restyled
  without a resize, accepts a container whose sides were each rounded to a whole pixel, lets go of a
  station the pointer was over, refuses the animated `azimuthAngle`/`polarAngle` turns, and gives
  the marker labels back the size they had rather than one worked out again.
- The label glyph atlas holds the **accented letters of European names**: Latin-1, the common
  letters of Latin Extended-A, and Romanian `ĂăÂâÎîȘșȚț` with the cedilla forms `ŞşŢţ` it is often
  typed with — 225 glyphs, under the 256 cells of the largest text, so the largest label size is
  still 45. A marker's text is put in its composed form first, so a name typed with combining
  accents draws from the same cells, and a glyph arriving once the atlas is full is drawn as a box
  instead of failing the label.
- `getSnapshot()` now puts the view back as it was: the camera, line widths and resolution,
  entrance dots and scales were left sized to the snapshot image before.

Checked in a real browser (Chromium, software GL) against this directory's bundle with the e2e
fixture model: two captures of one state are byte-identical; a capture at twice the container,
scaled back down, differs from the on-screen frame by a mean of 1.2 of 255 per channel (a
capture turned 30° differs by 7.8); the timed move is at 0.50 of its way after 300 ms of
advancing and arrived after 600; and the on-screen frame after `endCapture()` is byte-identical
to the one before. After each of the toolbar's elevation and plan views, with nothing turning the
camera first, `getCameraAngles()` matches the camera to a millionth of a radian, and a capture
asked for those angles is byte-identical to one asked for none; a container restyled from 640 by
480 to 640 by 360 with no resize captures byte-identically to one resized; a container of
852.48 by 479.52 page pixels (852 by 480 once rounded) is accepted for 1280 by 720; an animated
turn asked for during a session changes no frame and leaves the controls off; and labels of 13.2
set by a pixel ratio of 1.1 are 13.2 again after a capture made at a ratio of 1. Each of those
checks fails against the build this one replaced.

**What slx.13 adds over slx.12** — maintenance the fork had been carrying as debt. The fork's
smoke test (`npm test` there, headless Chromium) had 16 assertions at the time, and several of
them said less than this list does: reduced motion was a property read, the keyboard one text
input, fullscreen a getter, the catalogue nothing. What the test asserts now is listed in the
fork's `test/README.md`, and slx.14 is what it found:

- A late `renderView()`, `resize()` or second `dispose()` after `dispose()` is a no-op rather than
  a `null` dereference, and `getSnapshot()` after it throws a named error.
- The keyboard shortcuts leave a key alone when it was typed into an input, a textarea, a select
  or an editable element — the viewer used to swallow every key while the pointer rested on it.
- A station picture whose load fails is dropped from the strip (`mediaError` event, with a
  `console.warn` naming the address when unhandled) instead of an empty frame.
- `prefers-reduced-motion: reduce` is honoured: flights jump to their target, a flight never
  starts the auto rotation, and `focusStation(ref, { animate: true })` forces the animation.
- `frameLiveMarkers({ margin, animate })` frames the live markers currently placed; `false` when
  there are none.
- A `fullscreenElement` option names the element that goes fullscreen (the container by default),
  and the fullscreen getter reads `document.fullscreenElement` rather than comparing sizes.
- The help page states the viewer's version. `npm run lint` passes in the fork (flat config) and
  its lock file agrees with `package.json`, so `npm ci` works in a clean clone.

**What slx.14 changes over slx.13** — defects in what slx.13 added, and three older ones the
same test turned up once it drove the viewer as a reader does. Only the bundle differs; the
workers, the stylesheet, the logo and the catalogue are byte-identical to slx.13's.

- **The shortcuts work again after one of the viewer's own controls was used.** slx.13 left a key
  alone whenever it was typed into any `input` or `select`. The toolbar's shading chooser is a
  select, and the side panel's settings are tick boxes, sliders and choosers; each keeps the focus
  after use, so no key reached the viewer until the reader clicked outside it. A key is now left to
  its target only for real text entry (a textarea, an editable element, an input whose type takes
  text) and for a chooser or tick box of the host page; a control inside the viewer's container, or
  inside a toolbar wherever the host put it, is the viewer's own.
- **A fullscreen the browser refused is undone.** The class that displays the element large was
  set before the browser was asked, and after a refusal nothing took it off: the state read false,
  every further press asked again, and each left a rejection unhandled. The viewer now reads as
  fullscreen while the class alone is displaying it large, the next press takes the class off,
  `dispose()` does too, and a request made while another element is in fullscreen does nothing.
- **A viewer disposed under a load ends the load silently.** A load completing after `dispose()`
  — the long case is one waiting on `crsLookup` — built a survey from a context already let go of
  and reported the error in an `alert()`; one disposed before the file was read alerted a failure
  to load. Both now return at once, and `dispose()` aborts what is still being fetched or read.
  This application disposes a viewer under a load whenever a panel is closed or pointed at another
  model before the first has arrived.
- **The toolbar's shading chooser follows the language.** It read the mode names once, when built,
  while the catalogue is fetched as the viewer is created and often lands later — so a Romanian
  viewer kept an English chooser. It now writes its list again when the language arrives.
- **A disposed viewer stops listening for a change of language.** Each viewer subscribed to the
  page-wide catalogue and never unsubscribed, so a later viewer changing the language made every
  disposed one rebuild its scales from nothing and throw, and none could be collected.
- **`frameLiveMarkers()` signals its end after it has returned**, including when the move takes one
  frame (all a reduced-motion reader gets) or the camera already takes the markers in. `moved`
  used to be dispatched inside the call, before a host told to wait for it could listen.
- **Framing markers that share one station leaves the zoom a number.** With `margin: 0` under the
  orthographic camera the box had no size, the fit was infinite and the zoom became NaN, which
  nothing recovered.

The fork's test went from 16 assertions to 38 with these: 26 pass and 12 fail against the slx.13
bundle, and all 38 pass against slx.14's.

**What slx.15 changes over slx.14** — defects slx.14 still had, fixed before it left the branch
it was vendored on. Again only the bundle differs.

- **A chooser or a slider of the viewer's own is stepped with the arrow keys again.** slx.14 took
  every key pressed in one of the viewer's own controls for the viewer, so that its shortcuts
  survive a control that keeps the focus — and cancelled each one, wanted or not. A slider of the
  side panel or the toolbar's shading chooser, once pressed, could not be moved with the arrows for
  as long as the pointer was over the viewer. The arrows, Home, End, Page Up and Page Down, which
  the viewer does nothing with, are now left to a chooser, a slider or a radio button of its own.
- **A fullscreen request counts from the moment it is made.** The class that displays the element
  large is set before the browser is asked, and slx.14 knew the class was covering the page only
  once the refusal had arrived: `fullscreen` set to false, or `dispose()`, in the turn of the
  request left the class on an element that went on covering the page, with no viewer to take it
  off. The viewer now reads as fullscreen from the request; taking the request back or disposing
  takes the class off at once; and a fullscreen the browser grants to a request nobody is waiting
  for any more is left as it arrives.
- **A toolbar beside the container follows a refused fullscreen into it.** A bar a host mounted
  outside the viewer's container was moved into it on the document's fullscreen event, which a
  refusal never raises, so the covering container hid the bar and the one button that would have
  uncovered the page. It now moves on the viewer's own report as well. This application mounts its
  bar inside the container, where the case does not arise.

The fork's test went from 38 assertions to 49 with these — four of the new ones hold behaviour
slx.14 already had and nothing checked (a framing's signal given before a focus started in the
same turn, a later real fullscreen taking over from a refused one, `dispose()` taking the class
off, `dispose()` aborting the request of a load): 42 pass and 7 fail against the slx.14 bundle,
and all 49 pass against this one.

Not taken: moving the `.3d` and `.lox` readers onto the worker path the `.ply` reader uses. They
write into a shared survey graph (stations shared by identity between legs, a tree with methods,
CRS resolution through the host's `crsLookup` during the parse), so a worker would need a flat
intermediate format and a main-thread rebuild — a redesign of the survey's input contract, not a
move. The fork's `feature/line-shader-r183` branch carries the three-line fix for upstream's r183
line shader; it is offered upstream and not merged into `silexgis`, which stays on r171.

The base is deliberately the release tag, not upstream `dev` HEAD: the two are
source-identical, but `dev` bumps three.js r171 → r183, and a bundle built on r183 fails
to compile the height-shading line shader (`vColor` became a vec4), leaving centerlines
invisible — verified in a real browser before this choice was made. Rebasing onto a
future upstream release re-tests exactly that, and the fork now carries the fix to offer.

CaveView.js is not published on npm; it ships as a prebuilt browser bundle. This
directory contains the runtime subset the app needs, under a directory named by the
distribution version:

- `v2.9.0-slx.15/js/CaveView2.min.js` — the viewer bundle (UMD, exposes the `CV2` global)
- `v2.9.0-slx.15/js/workers/` — web workers the bundle spawns at runtime (paths resolved
  against the viewer's `home` option, which the app points at this directory)
- `v2.9.0-slx.15/css/caveview.css`, `v2.9.0-slx.15/images/logo.svg` — runtime assets
- `v2.9.0-slx.15/lib/lang-ro.json` — the Romanian catalogue, fetched when the interface is Romanian

The version directory exists for cache correctness: these URLs are fetched outside the
app bundle's hashed-asset pipeline, so a new build must arrive under new URLs or
browsers keep running the old viewer. Loaded on demand by `src/caveview/loadCaveView.ts`
(`CAVEVIEW_HOME` names the current version directory) — none of this is part of the app
bundle or its startup cost.

To upgrade: in the fork checkout, update the `silexgis` branch (rebase or merge its
`feature/*` branches onto the wanted upstream state), bump the distribution version in
`src/js/core/constants.js` and `package.json`, run `npm ci && npm run build`, copy the
subset above from `build/CaveView/` into a new `v<version>/` directory here (the workers without
their `.min` twins, which nothing asks for; `lib/lang-*.json` for each interface language), and update
`CAVEVIEW_HOME` and this README in the same commit. **Keep the previous version
directory through one release** — a browser tab loaded before the upgrade still asks for
the old paths when its user first opens the 3D viewer, and deleting them immediately
turns that into a load failure until a full reload — then delete it in the release
after. (Earlier `2.9.0-slx.*` directories were removed rather than kept: none reached a release, so no
browser can be holding it.) Do not edit the vendored files in place.

`v2.9.0-slx.12/` is kept beside the current one under that rule: it is the build the last release
loads, so it is the one a browser can still be holding. `v2.9.0-slx.13/` went with slx.14 without
being kept, because it never reached an installation, and `v2.9.0-slx.14/` went with slx.15 the
same way, replaced on the branch it was vendored on before that branch was merged;
`v2.9.0-slx.9/` went with slx.13 (it had
been kept through slx.12); and `v2.9.0-slx.10/` and `v2.9.0-slx.11/` were each replaced before
reaching a release, so no browser could have been holding either and neither was kept. There are
never more than two version directories here, and a unit test counts them. **A build is never replaced in place, either:** once a version directory has been vendored,
any further change to the fork — a fix found while vendoring included — is a new distribution
version and a new directory. slx.12 was rebuilt in its own directory once, from `563b763b` to
`5f14d910`, before it reached a release; that was the last time.

**This build was verified to reproduce.** `v2.9.0-slx.15/js/CaveView2.min.js` (SHA-256
`0baf1d80911199e054e85e065ce8f821bd108f930f7d595414e82a91f2b8b387`) is byte-identical to a
fresh `npm ci && npm run build` of commit `5647a5fd` in a clean clone of the fork, made
separately from the build it was copied from — as is every other file that build writes; the
workers, the stylesheet and the logo are byte-identical to slx.12's, and the catalogue to the
fork's tracked file (slx.14's bundle, `7b6b4024…`, reproduced the same way from `4ff3ecbd`,
slx.13's, `31a2d6cd…`, from `f4c94bb2`, and slx.12's, `b12cb7da…`, from `5f14d910`). (An earlier build, from `ed0322e5`, was checked the same way against the bundle
serving the club's public pages.) That is worth re-checking on the next upgrade: it is the
cheapest evidence that the vendored bytes are the fork's source and not a local accident.
