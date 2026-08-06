// SPDX-License-Identifier: AGPL-3.0-or-later
/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server proxies API + OIDC endpoints to the local API so the SPA is same-origin
// (matching the production nginx topology).
// Both ends are overridable because a machine may hold more than one checkout of this
// project, and the defaults collide: a second dev server silently reusing the first one's
// port serves the other checkout's application, which is worse than refusing to start.
const apiTarget = process.env.SILEXGIS_API_TARGET ?? 'http://localhost:5080';
const devPort = Number(process.env.SILEXGIS_DEV_PORT ?? 5173);
const proxiedPaths = ['/api', '/connect', '/health', '/openapi', '/.well-known'];

export default defineConfig({
  plugins: [react()],
  server: {
    port: devPort,
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
