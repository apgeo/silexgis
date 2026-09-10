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
//
// One flow needs a second account, and nothing but self-registration mints one, so the API
// has to be started with SILEXGIS__Auth__OpenRegistration=true — it is off by default. That
// flow fails rather than skipping without it: the behaviour it covers, an administrator
// editing a link somebody else recorded, has no other cover, and a skipped test reads as a
// green run.
//
// SILEXGIS_DEV_PORT moves the whole run — the dev server Vite starts and the address the
// browser is pointed at — so a second checkout can be exercised against its own API and
// database. It is the same variable the dev server reads, and it must be set together with
// SILEXGIS_API_TARGET; setting only one would drive this checkout's SPA against the other's
// data. Unset, this is the ordinary local run on :5173. Reusing an already-running server on
// the default port is a convenience that turns into a trap when the server belongs to
// another checkout.
const devPort = process.env.SILEXGIS_DEV_PORT ?? '5173';
const baseURL = `http://localhost:${devPort}`;

/**
 * The specs that belong to a phone project and must not also run on the desktop one.
 *
 * Named in one place because two things read them: the desktop project subtracts them, and the
 * phone projects below select them. Written as anchored file names rather than as substrings —
 * `scene3d` as a substring also matches `scene3d-mobile`, which is how a phone spec would end up
 * running twice, once in a viewport it was never written for.
 */
const PHONE_ONLY_SPECS = [
  /(^|\/)mobile\.spec\.ts$/,
  /(^|\/)scene3d-mobile\.spec\.ts$/,
  /(^|\/)mobile-ios\.spec\.ts$/,
];

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
      // Playwright's default desktop chromium viewport, and the home of every spec that no
      // narrower project claims.
      //
      // This used to name its forty-four specs in one alternation, and the comment here admitted
      // what that costs: a new spec ran nowhere until somebody remembered to add it. That is a
      // fail-closed list, and a fail-closed list quietly shrinks — `photo-library-smoke.spec.ts`
      // sat on disk claimed by no project at all, passing by never running. So the rule is
      // inverted: desktop takes every spec, and the phone projects below subtract the three that
      // are theirs. A new spec is now covered by writing it, and `e2e-spec-coverage.test.mjs`
      // fails the gate if any spec ends up claimed by nobody.
      name: 'desktop',
      testMatch: /\.spec\.ts$/,
      testIgnore: PHONE_ONLY_SPECS,
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
    url: baseURL,
    // Reusing a running dev server is a convenience by hand and a trap under the runner: a Vite
    // started before an edit keeps serving the code from before it, so the suite passes or fails
    // against assets nobody is looking at. That is not hypothetical — a CSS fix measured as
    // ineffective across forty runs here, and was measured working twenty times out of twenty the
    // moment the server was started fresh. `scripts/e2e.mjs` sets the marker, so a managed run
    // always builds its own server and an ordinary `npx playwright test` keeps the convenience.
    reuseExistingServer: !process.env.SILEXGIS_E2E_MANAGED,
    timeout: 60_000,
  },
});
