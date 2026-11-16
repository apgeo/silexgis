// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

// Where an area's entrances and sinkholes sit among the landforms, on the area's own page.
//
// What this file shows that no server test can. The server's tests prove the counting: which class
// each entrance falls in, that ground the landform picture left unclassified is not counted, that a
// cave the reader may not place moves nothing. None of them can show that the answer arrives
// legible: that the landform classes are named as the landform picture's legend names them and not
// as the position classes that share a code with them ("hollow" is both), and that a missing
// landform picture on an installation that does not run the analysis program says so instead of
// sending the reader to a terrain page where nothing can be computed.
//
// Why it is driven against a supplied answer. Counting needs a computed landform picture, and this
// suite's installation runs no analysis program and holds no prepared elevation, so no installation
// the suite can build would produce one. The answers handed to the page are the shapes the server's
// own tests pin.

interface FeatureType {
  id: number;
  code: string;
}

/** Far from the demonstration data and from every other spec's features. */
const HERE = { lon: 26.71, lat: 46.31 };

const row = (code: string, entrances: number, entranceRatio: number | null, backgroundShare: number) => ({
  code,
  entrances,
  entranceShare: entrances / 3,
  entranceRatio,
  sinkholes: 0,
  sinkholeShare: null,
  sinkholeRatio: null,
  background: Math.round(backgroundShare * 1890),
  backgroundShare,
});

function answer(areaId: string, landform: object) {
  const absent = (measure: string) => ({
    measure,
    available: false,
    reason: 'no_layer',
    entrancesRead: 0,
    sinkholesRead: 0,
    backgroundRead: 0,
    classes: [],
  });
  return {
    areaId,
    hasTerrain: true,
    entrances: 3,
    sinkholes: 0,
    backgroundPoints: 2025,
    measures: [absent('slope'), absent('aspect'), absent('position'), landform],
  };
}

const counted = {
  measure: 'landform',
  available: true,
  reason: null,
  entrancesRead: 3,
  sinkholesRead: 0,
  backgroundRead: 1890,
  classes: [
    row('flat', 0, null, 0),
    row('peak', 0, 0, 0.02),
    row('ridge', 1, 0.83, 0.4),
    row('shoulder', 0, 0, 0.05),
    row('spur', 0, 0, 0.03),
    row('slope', 0, 0, 0.3),
    row('hollow', 2, 6.67, 0.1),
    row('footslope', 0, 0, 0.04),
    row('valley', 0, 0, 0.06),
    row('pit', 0, null, 0),
  ],
};

const unavailable = {
  measure: 'landform',
  available: false,
  reason: 'analysis_unavailable',
  entrancesRead: 0,
  sinkholesRead: 0,
  backgroundRead: 0,
  classes: [],
};

async function answerWith(page: Page, areaId: string, landform: object) {
  await page.unroute('**/api/v1/features/*/terrain-statistics');
  await page.route('**/api/v1/features/*/terrain-statistics', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer(areaId, landform)) }),
  );
}

test('an area page names landform classes as the landform legend does and says when none can be drawn here', async ({
  page,
}) => {
  await login(page);
  const types = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const karstArea = types.find((t) => t.code === 'karst_area');
  expect(karstArea, 'the karst area kind is seeded').toBeTruthy();

  const { lon: w, lat: s } = HERE;
  const area = await asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
    kind: 'generic',
    name: `E2E landform area ${Date.now()}`,
    featureTypeId: karstArea!.id,
    geometry: {
      type: 'Polygon',
      coordinates: [[[w, s], [w + 0.01, s], [w + 0.01, s + 0.01], [w, s + 0.01], [w, s]]],
    },
    description: null,
    properties: null,
    parents: null,
    locationProtected: false,
    cavingGroupId: null,
    visibility: 'authenticated',
  });

  try {
    await answerWith(page, area.id, counted);
    await gotoRoute(page, `/features/${area.id}`);
    const card = page.getByTestId('area-terrain-statistics');
    await expect(card).toBeVisible();
    await card.getByRole('tab', { name: 'Landform' }).click();

    const panel = card.getByRole('tabpanel', { name: 'Landform' });
    await expect(panel.getByTestId('chart-terrain-landform')).toBeVisible();
    // The landform's own names, capitalised as its legend has them; the position class of the same
    // code reads "hollow" and must not appear here.
    await expect(panel.getByRole('cell', { name: 'Hollow', exact: true })).toBeVisible();
    await expect(panel.getByRole('cell', { name: 'Footslope', exact: true })).toBeVisible();
    await expect(panel.getByRole('cell', { name: 'hollow', exact: true })).toHaveCount(0);
    await expect(panel.getByRole('cell', { name: '6.67', exact: true })).toBeVisible();
    await expect(panel).toContainText('newest landform picture');

    await answerWith(page, area.id, unavailable);
    await gotoRoute(page, `/features/${area.id}`);
    await page.getByTestId('area-terrain-statistics').getByRole('tab', { name: 'Landform' }).click();
    const empty = page.getByTestId('area-terrain-statistics').getByRole('tabpanel', { name: 'Landform' });
    await expect(empty).toContainText('WhiteboxTools');
    await expect(empty).not.toContainText('No picture of the terrain');
    await expect(empty.getByRole('table')).toHaveCount(0);
  } finally {
    await page.unroute('**/api/v1/features/*/terrain-statistics');
    await asPerson(page, 'DELETE', `/api/v1/features/${area.id}`).catch(() => undefined);
  }
});
