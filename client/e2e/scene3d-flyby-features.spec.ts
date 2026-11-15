// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { login, openScene3dAt, scene3dSurface } from './helpers.ts';
import { tintedPng } from './png.ts';

// A flyby about what a saved filter selects, showing features of its own, in the real scene.
//
// The unit tests write the filter's caves out and hand a layer of the flyby's own to a scene made of
// plain objects. Only the application itself can show that a saved filter is asked for its caves when
// the flyby is played and the tour then visits that many; that a layer of features chosen by kind is
// really drawn by the scene while the flyby has the camera; and that it is gone again when the flyby
// stops. The scene is read for the layer's own colour, which nothing else on the screen is.
//
// Everything is arranged by the test on a patch of ground nothing else uses — two caves of a region
// of their own, a saved filter keeping that region's caves, and features of one surface type — and
// taken away again at the end. The street map is answered with a flat tile of one colour, so nothing
// leaves the machine and nothing on the ground is magenta.

/** Far from the demonstration data and from every other spec's caves. */
const HERE = { lon: 26.43, lat: 46.12 };
/** The street map, as served here. */
const STREET_BLUE: [number, number, number] = [118, 168, 208];
/** The palette's magenta, which the flyby's own layer is drawn in. */
const LAYER_MAGENTA: [number, number, number] = [196, 29, 127];

interface FeatureType {
  id: number;
  name: string;
  category: string;
  boundKind: string;
  requiresParent: boolean;
  acceptedGeometryClasses: string[];
}

/** Counts the pixels of the scene near one colour, over all of it the flyby's panel and bar leave uncovered. */
async function colourIn(page: Page, [red, green, blue]: readonly number[], tolerance = 40) {
  const box = (await scene3dSurface(page).boundingBox())!;
  const panelShown = (await page.getByTestId('flyby-panel').count()) > 0;
  const panel = panelShown ? await page.getByTestId('flyby-panel').boundingBox() : null;
  const left = panel ? Math.max(box.x, panel.x + panel.width + 4) : box.x;
  const picture = await page.screenshot({ clip: { x: left, y: box.y, width: box.x + box.width - left, height: box.height - 90 } });
  return page.evaluate(
    async ({ data, red, green, blue, tolerance }) => {
      const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const pixels = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
      let near = 0;
      for (let at = 0; at < pixels.length; at += 4) {
        if (
          Math.abs(pixels[at]! - red) <= tolerance &&
          Math.abs(pixels[at + 1]! - green) <= tolerance &&
          Math.abs(pixels[at + 2]! - blue) <= tolerance
        ) {
          near++;
        }
      }
      return near;
    },
    { data: picture.toString('base64'), red, green, blue, tolerance },
  );
}

/** Picks options of a drop-down list by their titles, searched for first, then closes it. */
async function pickFrom(page: Page, select: string, titles: string[]) {
  await page.getByTestId(select).click();
  const list = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)');
  for (const title of titles) {
    await page.keyboard.type(title);
    await list.getByTitle(title, { exact: true }).click();
  }
  await page.keyboard.press('Escape');
  await expect(list).toHaveCount(0);
}

const status = (page: Page) => page.getByTestId('flyby-time');

test('a flyby tours the caves a saved filter selects, shows features of a kind while it flies, and takes them away when stopped', async ({
  page,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const street = tintedPng(256, STREET_BLUE);
  await page.context().route('**://tile.openstreetmap.org/**', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', headers: { 'access-control-allow-origin': '*' }, body: street }),
  );
  await login(page);

  // ---- arranged: two caves of a region of their own, the filter keeping them, features of one kind ----
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  const featureTypes = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const surfaceType = featureTypes.find(
    (type) =>
      type.category === 'surface' &&
      type.boundKind === 'generic' &&
      !type.requiresParent &&
      type.acceptedGeometryClasses.includes('point'),
  );
  expect(surfaceType, 'the catalogue has a kind of surface feature placed as a point').toBeDefined();
  const region = `E2E flyby region ${stamp}`;
  const caves: string[] = [];
  const features: string[] = [];
  let filterId: string | undefined;
  try {
    for (const [index, east] of [0, 0.004].entries()) {
      const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
        name: `E2E flyby cave ${index + 1} ${stamp}`,
        caveTypeId: caveTypes[0].id,
        region,
        visibility: 'authenticated',
        locationProtected: false,
        explorationStatus: 'Unknown',
        isShowCave: false,
      });
      caves.push(cave.id);
      await asPerson(page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
        entranceTypeId: entranceTypes[0].id,
        isMain: true,
        geom: { type: 'Point', coordinates: [HERE.lon + east, HERE.lat] },
        positionQuality: 'Gps',
      });
    }
    for (const [north, east] of [
      [0.0006, 0.0006],
      [-0.0006, 0.0018],
      [0.0004, 0.0032],
    ]) {
      const feature = await asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
        kind: 'generic',
        name: `E2E flyby feature ${stamp}`,
        featureTypeId: surfaceType!.id,
        geometry: { type: 'Point', coordinates: [HERE.lon + east, HERE.lat + north] },
        description: null,
        properties: null,
        parents: null,
        locationProtected: false,
        cavingGroupId: null,
        visibility: 'authenticated',
      });
      features.push(feature.id);
    }
    const filterName = `E2E flyby filter ${stamp}`;
    const created = await asPerson<{ id: string }>(page, 'POST', '/api/v1/saved-filters', {
      name: filterName,
      description: null,
      visibility: 'private',
      document: {
        version: 1,
        scope: [
          {
            world: 'feature',
            where: {
              node: 'allOf',
              of: [
                { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] },
                { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: region }] },
              ],
            },
          },
        ],
        sort: 'title',
        descending: false,
      },
    });
    filterId = created.id;

    await openScene3dAt(page, HERE.lat, HERE.lon + 0.002);
    await page.getByTestId('scene3d-flyby').click();
    await expect(page.getByTestId('flyby-panel')).toBeVisible();
    await page.getByTestId('flyby-load-route').uncheck();
    await page.getByTestId('flyby-turns').fill('0.25');
    await page.getByTestId('flyby-turns').blur();
    await page.getByTestId('flyby-seconds-per-turn').fill('6');
    await page.getByTestId('flyby-seconds-per-turn').blur();

    // ---- a cave added to a tour by its name ----
    await page.getByTestId('flyby-source-inView').click();
    await page.getByRole('combobox', { name: 'Add a cave by name' }).fill(`E2E flyby cave 2 ${stamp}`);
    await page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').getByText(`E2E flyby cave 2 ${stamp}`).click();
    await expect(page.getByRole('checkbox', { name: `E2E flyby cave 2 ${stamp}` })).toBeChecked();

    // ---- the tour of what the saved filter selects ----
    await page.getByTestId('flyby-source-filter').click();
    await pickFrom(page, 'flyby-filter', [filterName]);
    await expect(page.getByTestId('flyby-preset')).toContainText('Cave tour');
    await page.getByText('3 sections').click();
    await expect(page.getByTestId('flyby-sections')).toContainText(`each of the caves of “${filterName}”`);

    // ---- a layer of the flyby's own: the arranged kind, in magenta ----
    await page.getByText('Features the flyby shows (0)').click();
    await pickFrom(page, 'flyby-features-types', [surfaceType!.name]);
    await page.getByTestId('flyby-features-add').click();
    await expect(page.getByTestId('flyby-features-f1')).toContainText(surfaceType!.name);
    await pickFrom(page, 'flyby-features-f1-colour', ['magenta']);

    // The cave being visited lit and the others dimmed, written as changes at each stop of the tour.
    await page.getByText('Ready-made changes').click();
    await page.getByTestId('flyby-recipe-light-add').click();
    await page.getByText('2 changes').click();
    await expect(page.getByTestId('flyby-changes')).toContainText('The cave visited in #ffb000, 85%');

    const before = await colourIn(page, LAYER_MAGENTA);
    const asked = page.waitForResponse(
      (response) => response.ok() && new URL(response.url()).pathname === '/api/v1/filters/query',
    );
    const layerAsked = page.waitForResponse(
      (response) =>
        response.ok() &&
        new URL(response.url()).pathname === '/api/v1/map/features' &&
        (new URL(response.url()).searchParams.get('filter') ?? '') !== '',
    );
    await page.getByTestId('flyby-play').click();
    // The caves are asked for when the flyby is played, and the layer by the kind chosen.
    await asked;
    await layerAsked;
    await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 180_000 });
    // Two caves, each a hop of 8 s and a quarter of a six-second circle, then the rise of 8 s:
    // 2 × (8 + 1.5) + 8 = 27 seconds. One cave would be 17.5.
    await expect(status(page)).toContainText('/ 0:27');
    await expect(status(page)).toHaveAttribute('data-status', 'ended', { timeout: 180_000 });
    await expect
      .poll(() => colourIn(page, LAYER_MAGENTA), { message: 'the flyby\'s own layer never showed in the scene', timeout: 90_000, intervals: [1_000] })
      .toBeGreaterThan(before + 20);
    // Kept beside the run's results, for a person to look at.
    await page.screenshot({ path: test.info().outputPath('flyby-features-shown.png') });

    // Stopped: the layer goes with the flyby.
    await page.getByTestId('flyby-stop').click();
    await expect(status(page)).toHaveAttribute('data-status', 'idle');
    await expect
      .poll(() => colourIn(page, LAYER_MAGENTA), { message: 'the flyby\'s own layer stayed after it stopped', timeout: 60_000, intervals: [1_000] })
      .toBeLessThanOrEqual(before + 5);
    await page.screenshot({ path: test.info().outputPath('flyby-features-stopped.png') });
  } finally {
    for (const id of features) {
      await asPerson(page, 'DELETE', `/api/v1/features/${id}`).catch(() => undefined);
    }
    for (const id of caves) {
      await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
    }
    if (filterId) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${filterId}`).catch(() => undefined);
    }
  }
});
