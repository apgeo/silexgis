// SPDX-License-Identifier: AGPL-3.0-or-later
/// <reference types="vitest/config" />
import { appendFileSync, createReadStream, existsSync, mkdirSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
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

/**
 * Takes the error reports the application makes about itself and appends them to a file.
 *
 * A route on the development server rather than an endpoint on the API, and that is the whole design.
 * An ingest route on the API would be an unauthenticated write path in an application where every
 * other route is authenticated, permission-checked and visibility-filtered — needing an environment
 * gate, a rate limit, a size cap, a place in the anonymous allow-list and tests for all of it, in
 * order to carry development diagnostics. Here the guarantee is structural instead: a deployment runs
 * no development server, so the route does not exist there and nothing about it ships.
 *
 * The cost, stated plainly: only browsing through `npm run dev` is covered. Somebody using a packaged
 * installation reports nothing, and covering them means the API endpoint with all of its consequences.
 */
function clientErrorSink(): Plugin {
  const sinkFile = path.join('.diagnostics', 'browsing-errors.jsonl');
  // Refuses a body big enough to be a runaway loop rather than a report.
  const maxBodyBytes = 512 * 1024;

  return {
    name: 'silexgis:client-error-sink',
    apply: 'serve',
    configureServer(server) {
      const file = path.resolve(server.config.root, sinkFile);
      mkdirSync(path.dirname(file), { recursive: true });

      server.middlewares.use('/__client-errors', (request, response, next) => {
        if (request.method !== 'POST') {
          next();
          return;
        }
        const chunks: Buffer[] = [];
        let size = 0;
        let refused = false;

        request.on('data', (chunk: Buffer) => {
          size += chunk.length;
          if (size > maxBodyBytes) {
            refused = true;
            request.destroy();
            return;
          }
          chunks.push(chunk);
        });

        request.on('end', () => {
          if (refused) {
            response.statusCode = 413;
            response.end();
            return;
          }
          try {
            const batch: unknown = JSON.parse(Buffer.concat(chunks).toString('utf8'));
            const records = (Array.isArray(batch) ? batch : [batch]) as Record<string, unknown>[];
            appendFileSync(file, `${records.map((record) => JSON.stringify(record)).join('\n')}\n`);
            // Logged as well as written, so an error appears in the terminal already being watched
            // instead of only in a file somebody has to remember to look at.
            for (const record of records) {
              server.config.logger.warn(
                `  browser error ${String(record.fingerprint)}  ` +
                  `${String(record.message).split('\n')[0]}` +
                  `${record.frame ? ` [${String(record.frame)}]` : ''}`,
              );
            }
            response.statusCode = 204;
          } catch {
            // A report that cannot be read is not worth failing a development server over.
            response.statusCode = 400;
          }
          response.end();
        });

        request.on('error', () => {
          response.statusCode = 400;
          response.end();
        });
      });
    },
  };
}

/**
 * Serves the elevation pyramids the API published, the way a deployment's web server serves them.
 *
 * A deployment has nginx in front, and terrain is the one thing it answers entirely on its own —
 * the API never sees a request for a tile. Development has no such server, so without this the
 * address a published build is served at falls through to the single-page fallback and is answered
 * with the application's own HTML and a 200 status. Nothing errors: the manifest fails to parse and
 * the scene reports a damaged pyramid, which reads as a bad bake rather than as "there is no web
 * server here". So terrain could not be looked at, or browser-tested, in development at all.
 *
 * Deliberately narrow. It answers one prefix, reads files and nothing else, never falls through to
 * the application (a path with nothing behind it is a 404 here, which is what the deployment does
 * and what the scene can report honestly), and exists only while the development server is running,
 * so nothing about it ships. The directory it reads is the one the API is configured to publish
 * into; the default below is where the API puts it when nothing says otherwise.
 */
function publishedTerrain(): Plugin {
  // The same prefix the API hands the client, and the same one the deployment's server matches.
  const route = '/terrain/builds';
  const here = path.dirname(fileURLToPath(import.meta.url));
  const published = path.resolve(
    process.env.SILEXGIS_TERRAIN_PUBLISHED ??
      // Where the API writes them when nothing configures it otherwise: beside the built
      // application, which in development is its output directory.
      path.join(
        here,
        '..',
        'server',
        'src',
        'SilexGis.Api',
        'bin',
        'Debug',
        'net10.0',
        'data',
        'terrain',
        'published',
      ),
  );

  // Whatever is declared here has to describe the bytes on disk exactly. A tile is a binary mesh,
  // and a browser handed one whose declared type or encoding is wrong reports no error of any kind
  // — the request answers 200, the console stays empty, and the only symptom is a globe with no
  // ground on it. Nothing here sets Content-Encoding, because nothing here compresses.
  const types: Record<string, string> = {
    '.json': 'application/json',
    '.terrain': 'application/octet-stream',
  };

  return {
    name: 'silexgis:published-terrain',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use(route, (request, response, next) => {
        if (request.method !== 'GET' && request.method !== 'HEAD') {
          next();
          return;
        }

        // Tile addresses carry the pyramid's version as a query string, so the path has to be
        // taken apart rather than used whole.
        const requested = decodeURIComponent((request.url ?? '/').split('?')[0]);
        const file = path.resolve(published, `.${path.posix.normalize(requested)}`);
        // Containment is this middleware's own job: what arrives here is a raw request path, and
        // one climbing out of the published directory would read whatever it landed on.
        if (file !== published && !file.startsWith(published + path.sep)) {
          response.statusCode = 403;
          response.end();
          return;
        }

        let size: number;
        try {
          const found = statSync(file);
          if (!found.isFile()) {
            throw new Error('not a file');
          }
          size = found.size;
        } catch {
          response.statusCode = 404;
          response.end();
          return;
        }

        response.setHeader('Content-Type', types[path.extname(file)] ?? 'application/octet-stream');
        response.setHeader('Content-Length', size);
        // Development is where a pyramid is replaced most often, and a browser holding on to one
        // file of a replaced pyramid is exactly the confusion this is here to remove. What a
        // deployment caches, and why the manifest is cached differently from its tiles, is decided
        // by the web server configurations that ship.
        response.setHeader('Cache-Control', 'no-store');
        if (request.method === 'HEAD') {
          response.end();
          return;
        }
        createReadStream(file).pipe(response);
      });
    },
  };
}

export default defineConfig({
  plugins: [
    react(),
    clientErrorSink(),
    publishedTerrain(),
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
    // Five seconds is the runner's default and this suite has outgrown it. These are component
    // tests: each one mounts a real antd tree into jsdom, and under the parallelism the runner
    // chooses on a busy machine the slowest of them sit just under the line. The symptom is that
    // a handful fail per run and a *different* handful fails on the next one, because what tips
    // them is load rather than anything they assert — every failure reads "timed out", never a
    // wrong value. Raising the ceiling cannot turn a passing test red; it only stops a slow one
    // being reported as a broken one. If a test ever genuinely hangs, it now takes longer to say
    // so, which is the trade being made.
    testTimeout: 20_000,
    hookTimeout: 20_000,
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
