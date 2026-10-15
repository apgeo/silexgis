# Vendored LochViewer build (trial)

Source: https://github.com/dankez/CaveView.js — published as "LochViewer", a React 18 +
react-three-fiber rewrite of a cave survey viewer that started from CaveView.js (MIT license, see
`LICENSE` in this directory). It is **not** a build of CaveView.js and shares no code or programming
interface with the CaveView.js runtime vendored under `../caveview/`.

Vendored build: directory `a7127cb1/`, built from the fork's `dev` branch at commit `a7127cb1`
(package version 2.3.0). It is offered as a second viewer on trial, in its own tab of the survey
model window, so it can be tried against this installation's own models. Nothing in the
application depends on it.

## How it was built

Node 24, from a clean clone at the commit above:

```
npm ci --ignore-scripts
npx vite build --config vite.silexgis.config.ts --outDir dist --emptyOutDir
cp public/assets/cave_limestone.png dist/assets/
```

with `vite.silexgis.config.ts`:

```ts
import { defineConfig, mergeConfig } from 'vite'
import base from './vite.config'
export default mergeConfig(base, defineConfig({ base: '/lochviewer/a7127cb1/', publicDir: false }))
```

`publicDir: false` leaves out the ~98 MB of demo models, terrain rasters and screenshots the
repository serves from its `public/`; the one texture the viewer loads at runtime is copied by hand.
The `base` is baked into the build, so a rebuild goes into a new directory named by its commit, and
`LOCHVIEWER_PAGE` in `src/caveview/lochViewer.ts` changes with it.

## Patches applied before building

1. **Parameters are read from the URL fragment** (falling back to the query string) in the three
   places `App.tsx` reads `model`, `embed` and `sidebar`. A survey file is fetched by a signed URL
   whose token is the whole of its protection; in the query string of the viewer's own address it
   would be written to the web server's access log, escaped so the log's redaction does not catch
   it, and sent as the Referer of every asset the page loads. A fragment never leaves the browser.
2. **The Google sign-in script is removed from `index.html`.** It was loaded on every open, only
   to serve an upload-to-Google-Drive feature, which has no place here.
3. **The wall texture path is made relative to the build's base** (`Scraps.tsx`) rather than the
   site root, where it would have hit this application's own `/assets/`.
