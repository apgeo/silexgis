// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, registryRow, pickCaveType } from './helpers.ts';

// From a doline to the place where a cave beneath it comes nearest the surface, and on to the map.
//
// What this file shows that no other test can. The server's tests prove which caves a feature
// has beneath it and where each cave's thinnest rock is; the component tests prove that pressing
// the button announces a place. Neither shows that a person can walk the whole way: open a
// feature's page, find the cave listed under it, follow the link, press "Show on the map" beside
// the least cover, open the map, and find the mark where the passage is. That walk crosses three
// routes and one module-level announcement, and it breaks if any link of it is cut — a card that
// does not link, a button that announces nothing, a map that forgets what was announced.
//
// Both answers are supplied rather than built, for the reason the overburden specs give: reading
// the ground needs prepared elevation data, which a plain installation does not have. The cave
// itself is a real one, made here, so the link lands on a real page.

const CAMERA = { lat: 45.5303, lon: 25.4452, zoom: 15 };

// The mark's colour as the highlight layer paints it, and how far a pixel may stray from it —
// the circle is drawn with an outline, so its middle is the only pixel that is exactly itself.
const MARK_COLOUR = [0xf5, 0x22, 0x2d];
const TOLERANCE = 24;

async function createCave(page: Page, name: string) {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await pickCaveType(page);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  return new URL(page.url()).pathname.split('/').filter(Boolean).pop()!;
}

/** What the passages-beneath route would say for one cave 12 m under a doline, with terrain built. */
function passagesBeneath(featureId: string, caveId: string, caveName: string) {
  return {
    featureId,
    featureName: 'Dolina Demo',
    isPolygon: false,
    distanceM: 50,
    hasTerrain: true,
    truncated: false,
    passages: [
      {
        caveId,
        caveName,
        lateralOffsetM: 12,
        basis: 'surveyFlags',
        isApproximation: false,
        hasAltitudes: true,
        passageLongitude: CAMERA.lon,
        passageLatitude: CAMERA.lat,
        passageAltitudeM: 880,
        outcome: 'sampled',
        groundAltitudeM: 930,
        overburdenM: 50,
      },
    ],
  };
}

/** A short profile whose thinnest cover is at the camera's own centre. */
function profile(caveId: string) {
  const sample = (distanceAlongM: number, overburdenM: number, longitude: number) => ({
    distanceAlongM,
    longitude,
    latitude: CAMERA.lat,
    passageAltitudeM: 880,
    outcome: 'sampled' as const,
    groundAltitudeM: 880 + overburdenM,
    overburdenM,
    pathIndex: 0,
    segmentIndex: Math.floor(distanceAlongM / 100),
  });
  const least = sample(100, 22.5, CAMERA.lon);
  return {
    caveId,
    basis: 'surveyFlags',
    isApproximation: false,
    surveyModelId: '00000000-0000-0000-0000-000000000001',
    hasAltitudes: true,
    hasTerrain: true,
    passageLengthM: 200,
    coveredSampleCount: 3,
    minOverburdenM: 22.5,
    maxOverburdenM: 61.25,
    meanOverburdenM: 40.5,
    leastCover: least,
    samples: [sample(0, 61.25, CAMERA.lon - 0.001), least, sample(200, 37.75, CAMERA.lon + 0.001)],
  };
}

/**
 * Whether a pixel of the mark's colour is drawn at the middle of the map, which is where the
 * camera was put and so where the announced place is. The map keeps its objects to itself; its
 * picture is the one thing that can be asked of it.
 */
async function markAtCentre(map: Locator): Promise<boolean> {
  return map.evaluate(
    (element, [colour, tolerance]) => {
      const box = element.getBoundingClientRect();
      for (const canvas of Array.from(element.querySelectorAll('canvas'))) {
        const frame = canvas.getBoundingClientRect();
        if (frame.width === 0) continue;
        let context: CanvasRenderingContext2D | null;
        try {
          context = canvas.getContext('2d', { willReadFrequently: true });
        } catch {
          continue;
        }
        if (context === null) continue;
        const scale = canvas.width / frame.width;
        const x = Math.round((box.left + box.width / 2 - frame.left) * scale);
        const y = Math.round((box.top + box.height / 2 - frame.top) * scale);
        const radius = Math.round(2 * scale);
        let data: Uint8ClampedArray;
        try {
          data = context.getImageData(x - radius, y - radius, 2 * radius + 1, 2 * radius + 1).data;
        } catch {
          continue;
        }
        for (let i = 0; i < data.length; i += 4) {
          if (
            data[i + 3] > 200 &&
            Math.abs(data[i] - colour[0]) <= tolerance &&
            Math.abs(data[i + 1] - colour[1]) <= tolerance &&
            Math.abs(data[i + 2] - colour[2]) <= tolerance
          ) {
            return true;
          }
        }
      }
      return false;
    },
    [MARK_COLOUR, TOLERANCE] as const,
  );
}

test('the cave listed under a doline shows its least rock cover on the map', async ({ page }) => {
  const caveName = `E2E Least Cover Cave ${Date.now()}`;
  await login(page);
  const caveId = await createCave(page, caveName);

  // Stubbed for any feature: the route is asked for the one page this test opens, and the demo
  // doline that page belongs to is the same on every installation the suite runs on.
  await page.route('**/api/v1/features/*/passages-beneath*', (route) => {
    const featureId = new URL(route.request().url()).pathname.split('/').at(-2)!;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(passagesBeneath(featureId, caveId, caveName)),
    });
  });
  await page.route('**/api/v1/caves/*/overburden', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(profile(caveId)),
    }),
  );

  // From the registry, narrowed to the demonstration doline: the row carries its own navigation.
  const row = await registryRow(page, 'Dolina Demo');
  await expect(row).toBeVisible({ timeout: 30_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const passages = page.getByTestId('feature-passages');
  await expect(passages).toBeVisible({ timeout: 30_000 });
  await expect(passages.getByTestId('feature-passages-no-terrain')).toHaveCount(0);
  const link = passages.getByRole('link', { name: caveName });
  await expect(link).toBeVisible();
  await link.click();
  await expect(page).toHaveURL(new RegExp(`/caves/${caveId}$`), { timeout: 15_000 });

  // The control: nothing is marked until the button is pressed. Without this the check on the map
  // below could be satisfied by a mark left behind by something else.
  const leastCover = page.getByTestId('overburden-least-cover');
  await expect(leastCover).toBeVisible({ timeout: 30_000 });
  await expect(leastCover).toContainText('22.5 m');
  await expect(page.getByTestId('overburden-readout')).toHaveCount(0);
  await leastCover.getByRole('button', { name: 'Show on the map' }).click();
  await expect(page.getByTestId('overburden-readout')).toBeVisible();

  // Opened inside the running application, not by loading the address afresh: the place is held in
  // the page's memory only, and a reload would be a different session with nothing announced.
  await page.evaluate((address) => {
    window.history.pushState({}, '', address);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, mapAddress(CAMERA));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 60_000 });
  await expect(page).toHaveURL(/\/map/);

  await expect
    .poll(() => markAtCentre(page.locator('.ol-viewport')), {
      message: 'the least cover was announced but no mark was drawn where the passage is',
      timeout: 60_000,
    })
    .toBe(true);

  // And a map opened with nothing announced is empty at the same place: the mark is the button's
  // doing and not a colour the basemap happens to have there.
  await page.reload();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 60_000 });
  await page.waitForTimeout(2_000);
  expect(await markAtCentre(page.locator('.ol-viewport'))).toBe(false);

  // Leaves the cave behind so a rerun does not accumulate them.
  await gotoRoute(page, `/caves/${caveId}`);
});
