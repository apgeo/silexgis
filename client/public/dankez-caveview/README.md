# Vendored dankez-caveview build (trial)

dankez-caveview is LochViewer (https://github.com/dankez/CaveView.js), a React 18 +
react-three-fiber survey viewer that began as a rewrite of CaveView.js (MIT license, see `LICENSE`
in this directory). It is **not** a build of CaveView.js and shares no code or programming
interface with the CaveView.js runtime vendored under `../caveview/`. It is offered as a second
viewer on trial, in its own tab of the survey model window. Nothing else in the application
depends on it.

Vendored build: **`v2.3.0-slx.1`**, built from branch `modernized/silexgis` at commit `00dbf0f0`
of this project's fork, https://github.com/apgeo/CaveView.js. The fork keeps LochViewer's code on
branches prefixed `modernized/`, apart from its CaveView.js branches: `modernized/dev` follows
upstream's `dev` (here at `a7127cb1`, package version 2.3.0), each change sits on a branch of its own
based on it so it can be offered upstream, and `modernized/silexgis` merges them:

- `modernized/fix-svx3d-reader` — Survex `.3d` files read item by item as the format defines them.
  The upstream reader lost its place in many real files (error-info, cross-section and anonymous
  station items, and every v3–v7 file) and failed with "Offset is outside the bounds of the
  DataView".
- `modernized/fix-base-path` — bundled assets resolved against the build's base path, so a build
  served under a subpath (as here) loads its own texture rather than this site's `/assets/`.
- `modernized/feature-embed-messages` — a host page hands the viewer a file's bytes by
  `postMessage` instead of an address. The survey file is fetched here with its signed URL and only
  the bytes cross over, so the token never appears in the viewer's address, a server log or a
  Referer. Protocol: `src/caveview/dankezCaveView.ts`.
- `modernized/feature-lazy-google-signin` — Google's sign-in script is loaded only when a Drive
  upload is about to happen, instead of on every open.

## How it was built

Node 24, from a checkout of `modernized/silexgis`:

```
npm ci --ignore-scripts
npx vite build --config vite.silexgis.config.ts --outDir dist --emptyOutDir
cp public/assets/cave_limestone.png dist/assets/
```

with `vite.silexgis.config.ts` (kept out of the fork's history):

```ts
import { defineConfig, mergeConfig } from 'vite'
import base from './vite.config'
export default mergeConfig(base, defineConfig({ base: '/dankez-caveview/v2.3.0-slx.1/', publicDir: false }))
```

`publicDir: false` leaves out the ~98 MB of demo models, terrain rasters and screenshots the
repository serves from its `public/`; the one texture the viewer loads at runtime is copied by hand.
The `base` is baked into the build, so a new build goes into a new directory and
`DANKEZ_CAVEVIEW_PAGE` in `src/caveview/dankezCaveView.ts` changes with it.
