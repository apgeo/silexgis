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
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      // The pre-existing run, unchanged: Playwright's default desktop chromium viewport.
      // Each project pins its own file, so a new spec runs nowhere until it is named here.
      name: 'desktop',
      testMatch: /(smoke|settings|permission-groups|scene3d)\.spec\.ts/,
    },
    {
      // Pixel 7: 412x915 CSS px, touch enabled, coarse pointer, chromium.
      //
      // The 3D scene has its own file here rather than sharing the flat map's: a scene is one
      // drawing context per window and the specs that drive it have to stand alone, and naming it
      // separately is also the only way to see at a glance that the phone layout of the 3D view is
      // covered at all. Spelled out rather than left to `mobile\.spec\.ts` matching it by accident.
      name: 'mobile-android',
      use: { ...devices['Pixel 7'] },
      testMatch: /(mobile|scene3d-mobile)\.spec\.ts/,
    },
    {
      // The same phone turned sideways: 863x360 CSS px, touch enabled, coarse pointer.
      //
      // A second orientation rather than a second device, because the axis it covers is one the
      // portrait run cannot: sideways the viewport is WIDER than the breakpoint that selects a
      // phone layout, while the pointer is still a finger. Everything that has to be sized or
      // captioned for a finger is chosen on the pointer and everything about how much room there
      // is is chosen on the width, and only a device where those two disagree can show that the
      // right one was used for each. A tablet is the same case; this is the cheapest instance of
      // it, needing no extra profile beyond one already in the suite.
      //
      // Only the 3D scene, whose controls are what this covers.
      name: 'mobile-android-landscape',
      use: { ...devices['Pixel 7 landscape'] },
      testMatch: /scene3d-mobile\.spec\.ts/,
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
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },
});
