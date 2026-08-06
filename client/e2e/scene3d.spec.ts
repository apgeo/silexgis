// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';
import {
  countScene3dDrawCalls,
  entranceToPick,
  login,
  openScene3d,
  openScene3dAt,
  scene3dSurface,
  waitForScene3dQuiet,
  watchScene3dDrawing,
} from './helpers.ts';

// The engine's own hosted services are the one thing a unit test cannot rule out: it runs against
// a stand-in for the library, so it can only show that this application configures the real one
// correctly. Here the real library runs, draws a real globe and fetches its real runtime files, so
// an outbound request to the vendor would actually happen if the configuration were wrong.
const VENDOR_HOST = /(^|\/\/|\.)cesium\.com/;

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

test('a place in the scene is bookmarkable, and reopens where it was left', async ({ page }) => {
  await login(page);
  await page.goto('/map3d');
  await expect(page.getByTestId('scene3d-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });

  // A preset is a camera move, which is what puts a position in the address bar.
  await page.getByTestId('scene3d-preset-north').click();
  await expect.poll(() => page.url(), { timeout: 30_000 }).toMatch(/#3d\//);

  const shared = page.url();
  await page.goto(shared);
  await expect(page.getByTestId('scene3d-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });

  // Reopening a shared link puts the camera back rather than at the opening view, and the
  // position written for it survives the round trip to about a metre.
  const before = new URL(shared).hash.split('/').slice(1, 3).map(Number);
  await expect
    .poll(
      () => {
        const after = new URL(page.url()).hash.split('/').slice(1, 3).map(Number);
        return Math.max(Math.abs(after[0] - before[0]), Math.abs(after[1] - before[1]));
      },
      { timeout: 30_000 },
    )
    .toBeLessThan(0.001);
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
  const context = await browser.newContext();
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
