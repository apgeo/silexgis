// SPDX-License-Identifier: AGPL-3.0-or-later
import { defineConfig, devices } from '@playwright/test';

// Smoke flows against the dev stack: requires the dev database
// (deploy/docker-compose.dev.yml) and the API on :5080; Vite is started automatically.
//
// All three projects run in the standard e2e gate, so the phone layout is regressed by every
// later batch rather than rotting until someone remembers to check it. Pop-out and
// multi-window flows are desktop-only by construction and stay in the desktop project.
//
// WebKit is a separate browser download (`npx playwright install webkit`), not just another
// device profile.
// Follows the dev server's port, which is itself overridable so two checkouts can run side
// by side. Reusing an already-running server on the default port is a convenience that turns
// into a trap when the server belongs to another checkout.
const baseURL = `http://localhost:${process.env.SILEXGIS_DEV_PORT ?? 5173}`;

export default defineConfig({
  testDir: './e2e',
  // Every test here signs in through the real authorization server and then waits on a real
  // server doing real work, so what a flow costs depends on how many of its siblings are doing
  // the same thing at the same moment. A bound tight enough to fail a healthy test on a busy
  // machine teaches everyone to re-run the suite until it agrees with them, which is worse than
  // no bound at all; this one is loose enough that a failure means something is wrong.
  timeout: 120_000,
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [
    {
      // The pre-existing run, unchanged: Playwright's default desktop chromium viewport.
      // Each project pins its own file, so a new spec runs nowhere until it is named here.
      name: 'desktop',
      testMatch: /(smoke|settings|permission-groups|documents)\.spec\.ts/,
    },
    {
      // Pixel 7: 412x915 CSS px, touch enabled, coarse pointer, chromium.
      name: 'mobile-android',
      use: { ...devices['Pixel 7'] },
      testMatch: /mobile\.spec\.ts/,
    },
    {
      // iPhone 14 on WebKit — a smoke project, not a second editing gate. It exists for the
      // one thing chromium cannot answer: Safari raises no contextmenu over a canvas, so
      // the long-press menu is the app's own timer or it does not exist on an iPhone.
      // Emulation is still not an iPhone, so it proves the code path, not the platform.
      name: 'mobile-ios-smoke',
      use: { ...devices['iPhone 14'] },
      testMatch: /mobile-ios\.spec\.ts/,
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: baseURL,
    reuseExistingServer: true,
    timeout: 60_000,
  },
});
