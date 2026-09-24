// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

const here = path.dirname(fileURLToPath(import.meta.url));

/**
 * A second build, whose whole output is the published-trip fold as one file a browser can load.
 *
 * <b>Why a separate build rather than a chunk of the application.</b> The consumer is a club's own
 * article: a page with a plain-JavaScript cave viewer on it, no bundler, no package manager and no
 * build step of any kind. It cannot import from this application's module graph, and this
 * application's chunks are named by content hash and assume its own runtime. What that page can do
 * is load one script. So the fold — which both pages must agree about, because it decides where a
 * party was at a given moment and which of its reports may be drawn at all — is compiled a second
 * time from the same source into one self-contained file.
 *
 * <b>The application does not consume this output.</b> It keeps importing the TypeScript directly,
 * exactly as it did before this file existed. That is the property that makes the arrangement
 * honest: there is one source of the rule and two compilations of it, rather than one source and
 * one copy. A change to the fold cannot reach one page and miss the other.
 *
 * <b>Two formats, on purpose.</b> `iife` for a plain `<script>`, which is what a WordPress page
 * enqueues today; `es` for a `<script type="module">` or anything that later grows a build. Both
 * are emitted from the same entry so they cannot disagree.
 *
 * <b>No externals, and nothing to resolve at runtime.</b> The entry's transitive imports are all
 * within this repository and every import that leaves the cluster is `import type`, which is erased.
 * So the output has no bare specifiers, needs no import map, and pulls in neither React nor the
 * generated API client. The Node smoke test beside this config is what holds that true.
 */
export default defineConfig({
  // Nothing in the fold reads an env var or a base URL; declared so a stray `import.meta.env`
  // added later fails the build here rather than shipping `undefined` to somebody else's page.
  define: { 'import.meta.env': '({})' },
  // Off, or the application's whole `public/` tree — icons, avatars, the web manifest, the cave
  // viewer's own runtime files — is copied in beside the one script this build exists to produce,
  // and then carried to somebody else's web server by whoever deploys it. The output of this
  // config should be exactly what a page needs to load and nothing else.
  publicDir: false,
  build: {
    outDir: path.resolve(here, 'dist-trips'),
    emptyOutDir: true,
    // Read by a browser that is not this application's; the floor is what the viewer beside it
    // already requires rather than what this application's own toolchain targets.
    target: 'es2020',
    sourcemap: true,
    lib: {
      entry: path.resolve(here, 'src/publictrips/index.ts'),
      name: 'SilexGisTrips',
      formats: ['iife', 'es'],
      fileName: (format) => (format === 'es' ? 'silexgis-trips.mjs' : 'silexgis-trips.js'),
    },
    rollupOptions: {
      // Said explicitly because the default for a library is to externalise nothing, and a future
      // reader should not have to know that: this output must be complete on a page that can
      // resolve no module specifier at all.
      external: [],
    },
  },
});
