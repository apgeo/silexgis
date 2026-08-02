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
const apiTarget = 'http://localhost:5080';
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

/**
 * Fails the build if the engine's runtime files did not land where the engine will look for them.
 *
 * This check is here because the failure it catches is invisible without it: a request for a
 * missing file under this path is answered by the single-page fallback with someone else's HTML
 * and a 200 status, so the engine reports no error, the network panel shows no error, and the
 * only symptom is a black globe.
 */
function assertCesiumAssetsCopied(): Plugin {
  const probe = path.join('Assets', 'approximateTerrainHeights.json');
  return {
    name: 'silexgis:assert-cesium-assets-copied',
    apply: 'build',
    closeBundle(this: { environment?: { config?: { build?: { outDir?: string } } } }) {
      const outDir = this.environment?.config?.build?.outDir ?? 'dist';
      const expected = path.join(outDir, cesiumOutputDir, probe);
      if (!existsSync(expected)) {
        throw new Error(`3D engine runtime files are missing from the build output: ${expected}`);
      }
    },
  };
}

export default defineConfig({
  plugins: [
    react(),
    viteStaticCopy({
      targets: cesiumAssetTrees.map((tree) => ({
        src: `${cesiumSource}/${tree}`,
        dest: cesiumOutputDir,
        rename: { stripBase: cesiumStripDepth },
      })),
    }),
    assertCesiumAssetsCopied(),
  ],
  define: {
    // The engine reads this bare global to find the files copied above. Replaced at build time
    // rather than assigned at runtime, because the engine resolves it while its own modules are
    // still initialising — earlier than any code of ours could run. Left unset it silently
    // resolves against the hashed bundle directory, where nothing is: every request then returns
    // the SPA's index.html with a 200, so the failure looks like a blank globe and no errors.
    CESIUM_BASE_URL: JSON.stringify(cesiumBaseUrl),
  },
  server: {
    port: 5173,
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
