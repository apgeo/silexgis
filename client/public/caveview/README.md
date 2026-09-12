# Vendored CaveView.js runtime

Source: https://github.com/apgeo/CaveView.js — this project's fork of
https://github.com/aardgoose/CaveView.js (MIT license, see `LICENSE` in this directory).

Vendored build: distribution version **2.9.0-slx.3**, built from the fork's `silexgis`
branch at commit `7409df4b` — the upstream **2.9.0 release tag** plus the fork's changes
(each also kept on its own dev-based `feature/*` branch so upstream can take them): the
dispose-handler typo fix, the `crsLookup` configuration option the app uses to resolve
coordinate systems locally instead of via epsg.io, a navigation and hover API
(`focusStation`/`focusSurvey`/`highlightStation` and a `stationHover` event), pictures
held for a station shown over the model, a toolbar of viewer controls for a host to place
outside the scene, markers a host maintains over a loaded model, and touch parity — a tap
reveals what a mouse reveals by hovering, both hover events carry the `pointerType` that
caused them, and controls are sized for whichever pointer is in use.

The base is deliberately the release tag, not upstream `dev` HEAD: the two are
source-identical, but `dev` bumps three.js r171 → r183, and a bundle built on r183 fails
to compile the height-shading line shader (`vColor` became a vec4), leaving centerlines
invisible — verified in a real browser before this choice was made. Rebasing onto a
future upstream release re-tests exactly that.

CaveView.js is not published on npm; it ships as a prebuilt browser bundle. This
directory contains the runtime subset the app needs, under a directory named by the
distribution version:

- `v2.9.0-slx.3/js/CaveView2.min.js` — the viewer bundle (UMD, exposes the `CV2` global)
- `v2.9.0-slx.3/js/workers/` — web workers the bundle spawns at runtime (paths resolved
  against the viewer's `home` option, which the app points at this directory)
- `v2.9.0-slx.3/css/caveview.css`, `v2.9.0-slx.3/images/logo.svg` — runtime assets

The version directory exists for cache correctness: these URLs are fetched outside the
app bundle's hashed-asset pipeline, so a new build must arrive under new URLs or
browsers keep running the old viewer. Loaded on demand by `src/caveview/loadCaveView.ts`
(`CAVEVIEW_HOME` names the current version directory) — none of this is part of the app
bundle or its startup cost.

To upgrade: in the fork checkout, update the `silexgis` branch (rebase or merge its
`feature/*` branches onto the wanted upstream state), bump the distribution version in
`src/js/core/constants.js` and `package.json`, run `npm ci && npm run build`, copy the
subset above from `build/CaveView/` into a new `v<version>/` directory here, and update
`CAVEVIEW_HOME` and this README in the same commit. **Keep the previous version
directory through one release** — a browser tab loaded before the upgrade still asks for
the old paths when its user first opens the 3D viewer, and deleting them immediately
turns that into a load failure until a full reload — then delete it in the release
after. (Earlier `2.9.0-slx.*` directories were removed rather than kept: none reached a release, so no
browser can be holding it.) Do not edit the vendored files in place.
