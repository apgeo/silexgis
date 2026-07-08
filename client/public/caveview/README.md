# Vendored CaveView.js runtime

Source: https://github.com/aardgoose/CaveView.js — release **2.9.0**, MIT license
(see `LICENSE` in this directory).

CaveView.js is not published on npm; upstream distributes a prebuilt browser bundle per
release. This directory contains the runtime subset the app needs:

- `js/CaveView2.min.js` — the viewer bundle (UMD, exposes the `CV2` global)
- `js/workers/` — web workers the bundle spawns at runtime (paths resolved against the
  viewer's `home` option, which the app sets to `/caveview/`)
- `js/assets/`, `css/caveview.css`, `images/logo.svg` — runtime assets

Loaded on demand by `src/caveview/loadCaveView.ts` — none of this is part of the app
bundle or its startup cost.

To upgrade: download the release zip from the upstream releases page and re-copy the
files above, keeping this README and LICENSE. Do not edit the vendored files.
