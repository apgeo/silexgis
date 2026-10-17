// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import {
  countScene3dDrawCalls,
  entranceToPick,
  login,
  openScene3d,
  openScene3dAt,
  scene3dSurface,
  waitForScene3dQuiet,
  waitForScene3dReady,
  watchScene3dDrawing,
} from './helpers.ts';
import { tintedPng } from './png.ts';

// The engine's own hosted services are the one thing a unit test cannot rule out: it runs against
// a stand-in for the library, so it can only show that this application configures the real one
// correctly. Here the real library runs, draws a real globe and fetches its real runtime files, so
// an outbound request to the vendor would actually happen if the configuration were wrong.
const VENDOR_HOST = /(^|\/\/|\.)cesium\.com/;

/** How many times slower than this machine the starved-processor test runs the page. */
const SLOW_PROCESSOR_FACTOR = 4;

test('the 3D view opens on our own basemap and talks to nobody else', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', (request) => requested.push(request.url()));

  await login(page);
  await page.getByRole('menuitem', { name: '3D view' }).click();
  await page.waitForURL(/\/map3d$/);

  // The engine owns the canvas inside our element; its presence is the scene starting.
  const canvas = page.getByTestId('scene3d-container').locator('canvas').first();
  await expect(canvas).toBeAttached({ timeout: 30_000 });
  await expect(page.getByTestId('scene3d-loading')).toHaveCount(0, { timeout: 30_000 });
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);

  // Tile licences require the attribution to be shown, and it is the basemap's, not a vendor's.
  await expect(page.locator('.cesium-widget-credits')).toContainText('OpenStreetMap', {
    timeout: 30_000,
  });

  // The runtime files come from this installation, and the map tiles from the configured catalog.
  await expect
    .poll(() => requested.some((url) => /\/cesiumStatic\//.test(url)), { timeout: 30_000 })
    .toBe(true);

  expect(requested.filter((url) => VENDOR_HOST.test(url))).toEqual([]);
});

test('the 3D view loads the caves in front of it, asking for surveyed depths', async ({ page }) => {
  await login(page);

  // Both requests have to be watched for before the page is opened: the loader fires as soon as
  // the scene exists, which can be before the navigation has even settled.
  const centerlines = page.waitForResponse(
    (response) => response.url().includes('/api/v1/map/cave-centerlines') && response.ok(),
  );
  const entrances = page.waitForResponse(
    (response) => response.url().includes('/api/v1/map/cave-entrances') && response.ok(),
  );

  await page.goto('/map3d');

  const centerlineRequest = await centerlines;
  // Depths are what the flat map cannot show, and this is the only view that asks for them.
  expect(new URL(centerlineRequest.url()).searchParams.get('z')).toBe('true');
  // The box and the zoom are derived from the camera, so both have to actually be sent.
  const query = new URL(centerlineRequest.url()).searchParams;
  expect(query.get('bbox')!.split(',')).toHaveLength(4);
  expect(Number(query.get('zoom'))).toBeGreaterThanOrEqual(0);

  await entrances;

  // Every request of the first load has answered; the scene is as full as it is going to get.
  await expect(page.getByTestId('scene3d-data')).toHaveAttribute('data-loading', 'false', {
    timeout: 30_000,
  });
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);

  // The detail panel is the flat map's own, waiting for something to be picked.
  await expect(page.getByText('Click a feature on the map to see details.')).toBeVisible();
});

/** The camera the address bar names, as `[lat, lon, height, heading, pitch]`. */
function cameraInAddress(url: string): number[] {
  return new URL(url).hash.split('/').slice(1).map(Number);
}

/**
 * True once the address names the camera the north preset turns to: looking south from the north
 * side of what it was looking at, ten degrees below the horizon.
 */
function addressShowsNorthPreset(url: string): boolean {
  const [, , , heading, pitch] = cameraInAddress(url);
  return Math.abs(heading - 180) < 0.5 && Math.abs(pitch + 10) < 0.5;
}

/**
 * Takes a bookmark of the north preset, reopens it as a new page and proves the camera is back.
 *
 * Two waits here are the whole of what the test is about, and each was once missing:
 *
 *   * The bookmark is read only once the address describes the preset. The preset is a half-second
 *     flight, and the address is rewritten only after the camera has been still for a moment, so
 *     an address read as soon as it holds any position can still name the opening view — whose own
 *     settle writes one too — or a point the flight was passing through.
 *   * Reopening goes through a blank page. Going straight to an address that differs from the open
 *     one only in its hash is a navigation within the same document: nothing is rebuilt, nothing is
 *     restored, and the comparison measures a camera that was never put back.
 *
 * Whether the camera stays put after the restore is then asked of a scene that has stopped
 * drawing: anything that moved it would have raised the settled event, and the settled camera
 * would have been written back into the address before the scene went quiet.
 */
async function bookmarkRoundTrip(page: Page, patience = 60_000) {
  const north = page.getByTestId('scene3d-preset-north');
  await north.click();
  await expect(north).toHaveAttribute('aria-pressed', 'true', { timeout: patience });
  await expect.poll(() => addressShowsNorthPreset(page.url()), { timeout: patience }).toBe(true);
  const shared = page.url();

  const firstDocument = await page.evaluate(() => performance.timeOrigin);
  await page.goto('about:blank');
  await page.goto(shared);
  // The new page signs in again on its way, through redirects of its own, so the document is asked
  // for its age only once the scene stands in it.
  await waitForScene3dReady(page, patience);
  expect(await page.evaluate(() => performance.timeOrigin)).not.toBe(firstDocument);

  // Put back rather than left at the opening view, which looks straight down.
  await expect(north).toHaveAttribute('aria-pressed', 'true', { timeout: patience });
  await waitForScene3dQuiet(page, 2 * patience);

  // And to about a metre: the precision the address is written with.
  const [lat, lon] = cameraInAddress(page.url());
  const [sharedLat, sharedLon] = cameraInAddress(shared);
  expect(Math.max(Math.abs(lat - sharedLat), Math.abs(lon - sharedLon))).toBeLessThan(0.001);
}

test('a place in the scene is bookmarkable, and reopens where it was left', async ({ page }) => {
  await watchScene3dDrawing(page);
  await login(page);
  await openScene3d(page);
  await bookmarkRoundTrip(page);
});

test('a bookmark reopens where it was left on a starved processor too', async ({
  page,
  browserName,
}) => {
  // The engine's frames, its settled event and the address bar's write-back all run late when the
  // processor is shared with a whole browser suite, which is where the waits above were found
  // missing: the test failed only in whole runs, never alone, by exactly the distance between the
  // opening view and the preset. Throttling this page's processor puts it there on purpose.
  test.skip(browserName !== 'chromium', 'processor throttling is a Chromium facility');
  test.slow();
  await watchScene3dDrawing(page);
  await login(page);
  const devtools = await page.context().newCDPSession(page);
  await devtools.send('Emulation.setCPUThrottlingRate', { rate: SLOW_PROCESSOR_FACTOR });
  await page.goto('/map3d');
  await waitForScene3dReady(page, 120_000);

  // Whether the opening view writes the address depends on whether anything settles its camera
  // after the address writer is listening. A layout change that resizes the drawing surface does,
  // and in a loaded run one can land late; resizing here makes it land every time, so the address
  // already names somewhere before the preset is pressed — the position the old wait took for the
  // bookmark.
  const size = page.viewportSize()!;
  await page.setViewportSize({ width: size.width - 40, height: size.height });
  await expect.poll(() => page.url(), { timeout: 120_000 }).toMatch(/#3d\//);
  expect(addressShowsNorthPreset(page.url())).toBe(false);

  await bookmarkRoundTrip(page, 120_000);
});

test('the scene opens beside the flat map, sharing the one scene this window has', async ({
  page,
}) => {
  await login(page);
  await page.goto('/map');

  await page.getByTestId('map-scene3d-toggle').click();

  await expect(page.getByTestId('scene3d-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);
  // Exactly one drawing surface in the window, whatever is showing it.
  await expect(page.getByTestId('scene3d-surface')).toHaveCount(1);
  // The map keeps the address bar while the scene is only a panel beside it: one window has one
  // hash, and two writers would overwrite each other on every camera move.
  await expect.poll(() => page.url(), { timeout: 30_000 }).not.toMatch(/#3d\//);
});

test('a standing-still scene draws nothing at all', async ({ page }) => {
  await watchScene3dDrawing(page);
  await login(page);
  await openScene3d(page);

  // Measured against the real renderer, because this is the one thing the unit tests cannot show:
  // they drive a stand-in, so they can only prove the scene was CONFIGURED to draw on demand. What
  // this proves is that nothing in the running application asks it to. Two things here run before
  // every drawn frame — the camera's descent clamp and the surface-mode check — and either of them
  // asking for a redraw from inside one would keep the scene drawing for ever, at full rate,
  // looking on screen exactly like it working. That is the whole of the battery case on a phone,
  // and it is invisible without counting.
  await waitForScene3dQuiet(page);

  // Ten seconds and not one draw. The window is long because what it is looking for is a trickle
  // rather than a burst.
  expect(await countScene3dDrawCalls(page, 10_000)).toBe(0);
});

test('turning the camera draws, and then it stops again', async ({ page }) => {
  await watchScene3dDrawing(page);
  await login(page);
  await openScene3d(page);
  await waitForScene3dQuiet(page);

  // The counterpart of the test above, and the reason that one is not merely a test that the
  // renderer is broken: a scene that never drew anything at all would pass it too.
  const box = (await scene3dSurface(page).boundingBox())!;
  const drawing = countScene3dDrawCalls(page, 2_000);
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  for (let step = 1; step <= 10; step += 1) {
    await page.mouse.move(box.x + box.width / 2 - step * 10, box.y + box.height / 2);
  }
  await page.mouse.up();
  expect(await drawing).toBeGreaterThan(0);

  // And it goes quiet again rather than running on. The drag settles the camera, which reloads the
  // cave data for where it now points, so this waits for that rather than for a fixed time.
  await waitForScene3dQuiet(page);
  expect(await countScene3dDrawCalls(page, 5_000)).toBe(0);
});

test('picking an entrance in the scene names it over the scene, and lets go of it', async ({
  page,
}) => {
  await login(page);

  // The camera is put directly over a real entrance, looking straight down, so the entrance sits
  // in the middle of the view by construction and the click needs no projection to find it. "Frame
  // the cave" cannot be used for this against seeded data: the demo caves have entrances but no
  // surveys, so there is no drawn geometry to frame and the control is correctly disabled — and
  // the middle of the opening view is empty Carpathian hillside.
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);

  // Nothing is picked until something is, so the scene starts with no callout over it.
  await expect(page.getByTestId('scene3d-callout')).toHaveCount(0);

  const box = (await scene3dSurface(page).boundingBox())!;
  await expect(async () => {
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await expect(page.getByTestId('scene3d-callout')).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // The name over the scene is the name the flat map would give the same entrance, which is the
  // whole point of composing it in one place.
  await expect(page.getByTestId('scene3d-callout')).toContainText(entrance.name);

  // Reachable and dismissible from the keyboard — which the flat map's own hover chip is not. This
  // one is a thing a viewer acts on rather than a label that follows the pointer, so it is a
  // deliberate improvement on the flat map rather than a copy of it.
  const callout = page.getByTestId('scene3d-callout');
  await callout.focus();
  await expect(callout).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('scene3d-callout')).toHaveCount(0);

  // Dismissing the label is not deselecting: the detail panel is still showing what was picked.
  await expect(page.getByText('Click a feature on the map to see details.')).toHaveCount(0);
});

test('a browser without WebGL 2 is told why, rather than shown a dead canvas', async ({
  browser,
}) => {
  // A context of the test's own, reading English like the watched one, or the sign-in below waits
  // on a field labelled in Romanian.
  const context = await ownContext(browser);
  // Removes the capability the scene needs, before any application code runs.
  await context.addInitScript(() => {
    Reflect.deleteProperty(window, 'WebGL2RenderingContext');
  });
  const page = await context.newPage();

  await login(page);
  await page.goto('/map3d');

  await expect(page.getByTestId('scene3d-unsupported')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('This browser cannot show the 3D view')).toBeVisible();
  await expect(page.getByTestId('scene3d-container')).toHaveCount(0);

  await context.close();
});

// A source publishes tiles up to a zoom and no further, and its catalogue entry says which. Close to
// the ground the scene used to ask anyway — OpenStreetMap, which stops at 19, was asked for 20 to 22,
// refused every one, and each refusal was an error in the console. Served offline here, so the test
// counts what the scene asks for and depends on nobody's tile server.
test('close to the ground the scene asks a source for no zoom past the one it publishes', async ({ page }) => {
  const zooms: number[] = [];
  const tile = tintedPng(256, [118, 168, 208]);
  await page.route('**://tile.openstreetmap.org/**', (route) => {
    const zoom = /\/(\d+)\/\d+\/\d+\.png/.exec(route.request().url());
    if (zoom) zooms.push(Number(zoom[1]));
    return route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: tile,
    });
  });

  await login(page);
  await watchScene3dDrawing(page);
  // Ten metres above the ground, looking straight down: deep enough that, unbounded, the scene asks
  // for zoom 22 here.
  await page.goto('/map3d#3d/45.53000/25.30000/10/0.0/-90.0');
  await waitForScene3dReady(page);

  // It does reach the deepest level the source has, so a ceiling of 19 below is not just a scene
  // that never asked for much.
  await expect.poll(() => Math.max(0, ...zooms), { timeout: 45_000 }).toBeGreaterThanOrEqual(19);
  await waitForScene3dQuiet(page);
  expect(Math.max(...zooms)).toBe(19);
});
