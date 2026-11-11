// SPDX-License-Identifier: AGPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { seedUpload } from './surveyUploads.ts';

/**
 * The page of one upload's results, signed in and behind its public link.
 *
 * This suite has no survey compiler, so a seeded upload produces no model, its run places no line
 * in the world and nothing is measured. The first and the last test therefore lay a model, a line
 * and measured figures over the real upload's answers — the model is upstream CaveView's own
 * demonstration file, the line and the figures invented — to see the viewer tabs, the map and the
 * upload's own analyses drawn; the middle one uses the real answers alone, where the page has to
 * say there is no map and nothing measured rather than draw empty panels.
 */
const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');
const MODEL_URL = '/api/v1/files/e2e-viewer-model/content?token=e2e';

/** A transparent tile for every background request, so a map draws without leaving the machine. */
const TILE = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=',
  'base64',
);

/** An invented line, as the server answers a survey it placed. */
const LINE = {
  type: 'FeatureCollection',
  features: [
    {
      type: 'Feature',
      properties: { name: 'invented' },
      geometry: { type: 'MultiLineString', coordinates: [[[25.0, 45.0], [25.001, 45.0005]]] },
    },
  ],
};

/** Invented measurements of an upload's own survey, in the shape the figures route answers them. */
const MEASURE = {
  meanAxisDegrees: 30, resultantLength: 0.8, effectiveSampleSize: 2, rayleighZ: null, rayleighP: null, entropyNats: null,
  entropyNormalized: null,
};
const ANALYSES = {
  surveyModelId: '00000000-0000-0000-0000-000000000001',
  hasAltitudes: true,
  statistics: {
    basis: 'surveyFlags',
    hasAltitudes: true,
    indices: {
      segmentCount: 2, pathCount: 1, totalLengthM: 71.3, planLengthM: 66.7, verticalExtentM: 20, highestZM: 1000,
      lowestZM: 980, maximumExtentM: 66, verticality: 0.28, horizontality: 0.93, linearity: 0.9, sinuosity: 1.08,
      lengthToDepthRatio: 3.5, hasAltitudes: true,
    },
    paths: [],
  },
  orientation: {
    basis: 'surveyFlags', hasAltitudes: true, segmentCount: 2, totalLengthM: 71.3, byCount: MEASURE, byLength: MEASURE,
    bins: [{ fromDegrees: 20, toDegrees: 30, count: 2, lengthM: 71.3, countFraction: 1, lengthFraction: 1 }],
    dip: null,
  },
  depthProfile: {
    basis: 'surveyFlags', hasAltitudes: true, passageLengthM: 71.3, highestM: 1000, lowestM: 980, deepestM: 20,
    samples: [
      { distanceAlongM: 0, altitudeM: 1000, depthM: 0, pathIndex: null, segmentIndex: 0 },
      { distanceAlongM: 71.3, altitudeM: 980, depthM: 20, pathIndex: null, segmentIndex: 1 },
    ],
  },
  topology: null,
};

async function keepBackgroundsLocal(page: Page) {
  await page.route(
    (url) => !['localhost', '127.0.0.1'].includes(url.hostname),
    (route) => route.fulfill({ status: 200, contentType: 'image/png', body: TILE }),
  );
}

test('the signed-in page of results shows the model in both viewers, copies its link and maps a placed survey', async ({
  page,
}) => {
  await login(page);
  const upload = await seedUpload(page, `E2E Viewer ${Date.now()}`);
  await keepBackgroundsLocal(page);

  // The real answer, with a model among what the run produced.
  await page.route(`**/api/v1/survey-uploads/${upload.id}`, async (route) => {
    const response = await route.fetch();
    const body = await response.json();
    body.artefacts = [
      ...body.artefacts,
      { kind: '3d', name: 'P8_Master.3d', sizeBytes: 1, contentUrl: MODEL_URL, surveyModelId: null, documentId: null, surveySourceId: null, viewUrl: null, modelStatus: null, error: null },
    ];
    await route.fulfill({ response, json: body });
  });
  await page.route(`**${MODEL_URL}`, (route) =>
    route.fulfill({ status: 200, contentType: 'application/octet-stream', body: fs.readFileSync(path.join(fixtures, 'P8_Master.3d')) }),
  );
  // The real figures, saying the run placed the survey where this reader may see it, and with
  // the upload's own survey measured.
  await page.route(`**/api/v1/survey-uploads/${upload.id}/figures`, async (route) => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...(await response.json()), placed: true, analyses: ANALYSES } });
  });
  await page.route(`**/api/v1/survey-uploads/${upload.id}/geodata?format=geojson`, (route) =>
    route.fulfill({ status: 200, contentType: 'application/geo+json', json: LINE }),
  );

  await page.goto(upload.statusPageUrl);
  await page.getByTestId('survey-upload-open-viewer').click({ timeout: 30_000 });
  await expect(page.getByTestId('survey-viewer')).toBeVisible({ timeout: 30_000 });
  expect(new URL(page.url()).pathname).toBe(`${upload.statusPageUrl}/view`);

  const tabs = page.getByTestId('survey-model-tabs').getByRole('tab');
  await expect(tabs).toHaveCount(2);
  await expect(tabs.nth(0)).toHaveText('3D');
  await expect(tabs.nth(1)).toHaveText('LochViewer (trial)');

  await expect(page.getByTestId('survey-viewer-copy-link')).toBeVisible();
  // The upload has a public link, so that is what is copied.
  await expect(page.getByTestId('survey-viewer-share-kind')).toHaveAttribute('data-share-kind', 'public');

  await expect(page.getByTestId('survey-viewer-map')).toBeVisible();
  await expect(page.getByTestId('survey-viewer-geodata-kml')).toBeVisible();
  await expect(page.getByTestId('survey-viewer-open-in-app')).toHaveAttribute('href', `/caves/${upload.caveId}`);

  // The analyses are this upload's own, and the cave's current survey is not set beside them.
  const analyses = page.getByTestId('survey-viewer-analyses');
  await expect(analyses.getByTestId('cave-survey-statistics')).toContainText('71.3');
  await expect(analyses.getByTestId('chart-cave-depth-profile')).toBeVisible();
  await expect(page.getByTestId('survey-viewer-cave-analyses')).toHaveCount(0);
});

test('a survey the run could not place has no map, and the page fits a phone', async ({ page }) => {
  await login(page);
  const upload = await seedUpload(page, `E2E Viewer unplaced ${Date.now()}`, { publicLink: { wanted: false } });
  await keepBackgroundsLocal(page);
  await page.setViewportSize({ width: 390, height: 844 });

  await page.goto(`${upload.statusPageUrl}/view`);
  await expect(page.getByTestId('survey-viewer')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('survey-viewer-geo-unplaced')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('survey-viewer-map')).toHaveCount(0);
  // Nothing was read into shots, and the page says so once rather than drawing empty panels.
  await expect(page.getByTestId('survey-viewer-no-analyses')).toBeVisible();
  // No link was made, so the copied address is this page's own.
  await expect(page.getByTestId('survey-viewer-share-kind')).toHaveAttribute('data-share-kind', 'inApp');
  await expect(page.getByTestId('survey-viewer-copy-link')).toBeVisible();

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, 'the page scrolls sideways at phone width').toBeLessThanOrEqual(1);
});

test('the public link\'s page of results opens without an account and maps the survey its files place', async ({ browser, page }) => {
  await login(page);
  const upload = await seedUpload(page, `E2E Viewer public ${Date.now()}`);
  expect(upload.publicLinkUrl, 'a finished upload of an unprotected cave has a public link by default').toBeTruthy();
  const linkPath = new URL(upload.publicLinkUrl!, 'http://localhost').pathname;

  const visitor = await ownContext(browser);
  try {
    const anonymous = await visitor.newPage();
    await keepBackgroundsLocal(anonymous);
    const token = linkPath.split('/').pop()!;
    // The real public figures, saying the run placed the survey, with an invented line to draw.
    await anonymous.route(`**/api/v1/public/survey-uploads/${token}/figures`, async (route) => {
      const response = await route.fetch();
      await route.fulfill({ response, json: { ...(await response.json()), placed: true, analyses: ANALYSES } });
    });
    await anonymous.route(`**/api/v1/public/survey-uploads/${token}/geodata?format=*`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/geo+json', json: LINE }),
    );
    // The installation's map catalogue is a signed-in route; a public page must never ask it.
    const catalogueAsked: string[] = [];
    anonymous.on('request', (request) => {
      if (new URL(request.url()).pathname.startsWith('/api/v1/map-layers')) {
        catalogueAsked.push(request.url());
      }
    });
    await anonymous.goto(linkPath);
    await anonymous.getByTestId('public-survey-open-viewer').click({ timeout: 30_000 });
    await expect(anonymous.getByTestId('survey-viewer')).toBeVisible({ timeout: 30_000 });
    expect(new URL(anonymous.url()).pathname).toBe(`${linkPath}/view`);

    await expect(anonymous.getByTestId('survey-viewer-map')).toBeVisible({ timeout: 30_000 });
    await expect(anonymous.getByTestId('survey-viewer-map')).toHaveAttribute('data-basemap', 'opentopomap');
    await expect(anonymous.getByTestId('survey-viewer-geodata-kml')).toBeVisible();
    await expect(anonymous.getByTestId('survey-viewer-geodata-gpx')).toBeVisible();
    await expect(anonymous.getByTestId('survey-viewer-analyses').getByTestId('cave-survey-statistics')).toContainText('71.3');
    expect(catalogueAsked, 'the public page asked for the map catalogue').toEqual([]);
    await expect(anonymous.getByTestId('survey-viewer-copy-link')).toBeVisible();
    await expect(anonymous.getByTestId('survey-viewer-share-kind')).toHaveAttribute('data-share-kind', 'public');
    await expect(anonymous.getByTestId('survey-viewer-sign-in-to-open')).toBeVisible();
  } finally {
    await visitor.close();
  }
});
