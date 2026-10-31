# SilexGIS client

The single-page application of SilexGIS: React 19 and TypeScript, built with Vite, Ant Design for
the components, OpenLayers for the 2D map and Cesium for the 3D globe. It talks to the API in
[`../server`](../server) and to nothing else. The project as a whole is described in the
[repository README](../README.md); this page is only about working in `client/`.

## Requirements

**Node.js 24 or newer** (`engines` in `package.json`). `@terrestris/react-geo` declares it, and on
an older engine a few component tests fail on a difference in `Blob.stream()` without naming the
engine as the cause. The production image (`Dockerfile`) builds on `node:24-alpine`, so the bundle
that ships is built on the engine the repository declares.

```bash
npm ci
```

## Everyday commands

Run these from `client/`.

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server on `http://localhost:5173`, proxying the API routes to `http://localhost:5080` |
| `npm run typecheck` | `tsc -b`, the type check the build also runs. When the answer looks too good, `npx tsc -b --force`: the incremental build information can answer green without reading your files |
| `npm run lint` | oxlint with the project's rules (`.oxlintrc.json`, accessibility rules included). The lint configuration is the style authority |
| `npm run lint:deps` | dependency-cruiser over `src/`: the module boundaries between the folders. Run it after your last edit; a file put in the wrong folder passes the type check and fails here |
| `npm test` | the unit and component tests (vitest, jsdom), beside the source as `*.test.ts(x)` |
| `npm run build` | type check, then the production bundle in `dist/` |
| `npm run build:trips` | the published-trip fold alone, compiled a second time as a single script for a club's plain-JavaScript page; the application itself does not use this output |
| `npm run generate:api` | regenerates `src/api/schema.d.ts` from a running API, see below |

Prettier is configured (`.prettierrc.json`) and there is a `format` script, but formatting is
neither run nor checked in this repository. Do not reformat files in passing.

### Pointing at another API or port

The dev server and the preview server read two environment variables:

- `SILEXGIS_API_TARGET`, where the API is (default `http://localhost:5080`);
- `SILEXGIS_DEV_PORT`, the port the client is served on (default `5173`). A busy port is an error
  rather than a silent move to the next one, so that one checkout's pages are never served against
  another checkout's API.

In a POSIX shell: `SILEXGIS_API_TARGET=http://localhost:5099 npm run dev`. In PowerShell:
`$env:SILEXGIS_API_TARGET='http://localhost:5099'; npm run dev`.

## The generated API client

`src/api/schema.d.ts` is generated from the API's OpenAPI document and is **never edited by hand**.
After any change to a route or a DTO, build and start the API and run

```bash
SILEXGIS_API_TARGET=http://localhost:5080 npm run generate:api
```

The first line it prints names the address it read the contract from; if that is not your API,
stop. The change to `schema.d.ts` should be explainable by your server change alone.

## Browser tests

The end-to-end specs are in `e2e/` and run through the repository's runner, `../scripts/e2e.mjs`,
which stands up its own database container, a migrated API with the settings the specs expect and
the dev server, then takes them down again:

```bash
npm run e2e                           # the whole suite
npm run e2e:fast                      # desktop project only, no 3D scenes: a quick look, not a verdict
node ../scripts/e2e.mjs trips         # only the named spec files
node ../scripts/e2e.mjs --affected master
```

The verdict is the runner's closing lines, not Playwright's first pass. Rules for writing a spec:

- import `test` from `./consoleGuard.ts`, not from Playwright. It records every uncaught error,
  unhandled rejection and `console.error` across the browser context, so a spec importing
  Playwright's `test` directly is silently unwatched;
- the application opens in **Romanian**. The guard's contexts record English as the choice; a
  context you create yourself must do the same (see `ownContext` in `e2e/consoleGuard.ts`);
- go to a screen with `gotoRoute(page, '/map')`;
- choose the options of an Ant Design select with the shared keyboard helper `chooseOption` from
  `e2e/helpers.ts`, never by clicking an option of a list that is still opening;
- never add Playwright `retries`: a flaky spec is a defect to fix.

The 3D globe renders in software on a machine without a GPU, so the 3D specs are slow rather than
broken; run anything with the viewer at `--workers=1`.

## How the source is arranged

`src/` is organised by area, not by kind: `pages/` (a screen each), `components/`, `map/` and
`scene3d/` (the 2D map and the 3D globe), `api/` (the generated client and the TanStack Query
hooks), `auth/`, `i18n/`, `stores/`, `filters/`, `workspace/` and so on. The rules that matter
when adding to it:

- **Server state is TanStack Query; UI state is Zustand.** OpenLayers objects live in the map
  context module and never in React state.
- **Every user-facing string goes through `t()`**, with the English and the Romanian text added in
  the same change (`src/i18n/locales/en.json` and `ro.json`). A test fails if the two files do not
  define exactly the same keys, and another if a key the code asks for by name does not exist.
- **Ant Design is the default component library.** Use `components/Button.tsx` rather than
  importing antd's `Button`, and give an icon-only button an `aria-label`.
- Theme through the tokens in `src/theme.ts`; no inline-style sprawl.
- Follow the neighbouring files before inventing a new pattern, and check what is already
  installed before proposing a new dependency.
