// SPDX-License-Identifier: AGPL-3.0-or-later
/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server proxies API + OIDC endpoints to the local API so the SPA is same-origin
// (matching the production nginx topology).
//
// Port and target are overridable so a second checkout can run its own stack — its own API
// and its own database — beside the primary one, with neither reaching into the other's
// data. Unset, both are the ordinary local stack, so `npm run dev` is unchanged. An API
// reached this way must also accept `http://localhost:<port>/auth/callback` as a redirect
// address, or signing in from the moved origin is refused.
const apiTarget = process.env.SILEXGIS_DEV_API_TARGET ?? 'http://localhost:5080';
const devPort = Number(process.env.SILEXGIS_DEV_PORT ?? 5173);
const proxiedPaths = ['/api', '/connect', '/health', '/openapi', '/.well-known'];

export default defineConfig({
  plugins: [react()],
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
