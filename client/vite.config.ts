// SPDX-License-Identifier: AGPL-3.0-or-later
/// <reference types="vitest/config" />
import { existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import { viteStaticCopy } from 'vite-plugin-static-copy';

// Dev server proxies API + OIDC endpoints to the local API so the SPA is same-origin
// (matching the production nginx topology).
//
// Both ends are overridable because a machine may hold more than one checkout of this
// project, and the defaults collide: a second dev server silently reusing the first one's
// port serves the other checkout's application against the wrong API, which is worse than
// refusing to start. The two variables must be set together — setting only one drives this
// checkout's SPA against the other's data. An API reached from a moved origin must also
// accept `http://localhost:<port>/auth/callback` as a redirect address, or signing in from
// there is refused. Unset, both are the ordinary local stack, so `npm run dev` is unchanged.
const apiTarget = process.env.SILEXGIS_API_TARGET ?? 'http://localhost:5080';
const devPort = Number(process.env.SILEXGIS_DEV_PORT ?? 5173);
const proxiedPaths = ['/api', '/connect', '/health', '/openapi', '/.well-known'];

// The 3D engine loads shader workers, decompression modules and icons at runtime rather than
// bundling them, so those files are copied out of the installed package and served from a fixed
// path the engine is told about. Copying from the package (instead of vendoring the files into
// this repository) keeps them in lockstep with the version in the lockfile.
const cesiumSource = 'node_modules/cesium/Build/Cesium';
const cesiumAssetTrees = ['Workers', 'ThirdParty', 'Assets', 'Widgets'];
// The copy plugin appends each matched file's whole path, relative to the project root, onto the
// destination. Stripping the leading segments of the source directory is what turns
// `.../node_modules/cesium/Build/Cesium/Assets/...` into `.../Assets/...`; derived from the path
// rather than hard-coded so the two can never drift apart.
const cesiumStripDepth = cesiumSource.split('/').length;
// These files keep the names their library gave them, so unlike the rest of the build they carry
// no content hash. Putting the library version in the path gives them the same property by other
// means: an upgrade moves every one of them, so a browser can never serve a cached decoder from
// one version to engine code from another, and the web server can cache them indefinitely.
const cesiumVersion = createRequire(import.meta.url)('cesium/package.json').version as string;
const cesiumOutputDir = `cesiumStatic/${cesiumVersion}`;
/** Public path the engine resolves its runtime files against; no trailing slash. */
const cesiumBaseUrl = `/${cesiumOutputDir}`;

// The PDF renderer reads two data tables it does not bundle: character maps for documents that
// address their glyphs by a published encoding, and metrics for the standard fonts a PDF is
// allowed to name without carrying. Some image encodings are decoded by a compiled module it
// loads the same way. All three are copied out of the installed package for the same reason as
// above — an installation that fetched them from a vendor's network would show nothing on one
// that cannot reach it, and would tell that vendor which documents are being read.
const pdfjsSource = 'node_modules/pdfjs-dist';
const pdfjsAssetTrees = ['cmaps', 'standard_fonts', 'wasm'];
const pdfjsStripDepth = pdfjsSource.split('/').length;
const pdfjsVersion = createRequire(import.meta.url)('pdfjs-dist/package.json').version as string;
const pdfjsOutputDir = `pdfjsStatic/${pdfjsVersion}`;

/**
 * Fails the build if the engine's runtime files did not land where the engine will look for them.
 *
 * This check is here because the failure it catches is invisible without it: a request for a
 * missing file under this path is answered by the single-page fallback with someone else's HTML
 * and a 200 status, so the engine reports no error, the network panel shows no error, and the
 * only symptom is a black globe.
 */
function assertRuntimeAssetsCopied(): Plugin {
  const probes: [string, string][] = [
    [cesiumOutputDir, path.join('Assets', 'approximateTerrainHeights.json')],
    [pdfjsOutputDir, path.join('standard_fonts', 'LiberationSans-Regular.ttf')],
  ];
  return {
    name: 'silexgis:assert-runtime-assets-copied',
    apply: 'build',
    closeBundle(this: { environment?: { config?: { build?: { outDir?: string } } } }) {
      const outDir = this.environment?.config?.build?.outDir ?? 'dist';
      for (const [dir, probe] of probes) {
        const expected = path.join(outDir, dir, probe);
        if (!existsSync(expected)) {
          throw new Error(`Runtime files are missing from the build output: ${expected}`);
        }
      }
    },
  };
}

export default defineConfig({
  plugins: [
    react(),
    viteStaticCopy({
      targets: [
        ...cesiumAssetTrees.map((tree) => ({
          src: `${cesiumSource}/${tree}`,
          dest: cesiumOutputDir,
          rename: { stripBase: cesiumStripDepth },
        })),
        ...pdfjsAssetTrees.map((tree) => ({
          src: `${pdfjsSource}/${tree}`,
          dest: pdfjsOutputDir,
          rename: { stripBase: pdfjsStripDepth },
        })),
      ],
    }),
    assertRuntimeAssetsCopied(),
  ],
  define: {
    // The engine reads this bare global to find the files copied above. Replaced at build time
    // rather than assigned at runtime, because the engine resolves it while its own modules are
    // still initialising — earlier than any code of ours could run. Left unset it silently
    // resolves against the hashed bundle directory, where nothing is: every request then returns
    // the SPA's index.html with a 200, so the failure looks like a blank globe and no errors.
    CESIUM_BASE_URL: JSON.stringify(cesiumBaseUrl),
    // The PDF renderer is told where its tables are the same way, and for the same reason: it
    // resolves them while loading, and a missing one is answered by the single-page fallback
    // with HTML and a 200, so a document would silently lose its accented characters rather
    // than report anything. Trailing slashes are required — the library appends file names.
    PDFJS_CMAP_URL: JSON.stringify(`/${pdfjsOutputDir}/cmaps/`),
    PDFJS_STANDARD_FONT_URL: JSON.stringify(`/${pdfjsOutputDir}/standard_fonts/`),
    PDFJS_WASM_URL: JSON.stringify(`/${pdfjsOutputDir}/wasm/`),
  },
  server: {
    port: devPort,
    // A busy port must fail loudly: silently moving to the next one would serve this
    // checkout's SPA where another checkout's is expected, against the wrong API.
    strictPort: true,
    // changeOrigin stays false so the API sees the browser's Host header and the
    // OIDC issuer matches the SPA origin.
    proxy: Object.fromEntries(proxiedPaths.map((path) => [path, { target: apiTarget }])),
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/setupTests.ts',
    // e2e/ belongs to Playwright, not Vitest.
    include: ['src/**/*.test.{ts,tsx}'],
    server: {
      deps: {
        // react-geo ships extensionless CJS-style imports (lodash/has) that
        // Node's ESM resolver rejects; inline it so Vite resolves them.
        inline: [/@terrestris/],
      },
    },
  },
});
