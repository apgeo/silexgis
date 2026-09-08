// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

// How much rock is over your head, on the page, drawn as the curve a reader actually looks at.
//
// What this file shows that no server test can. The server's own tests prove the arithmetic: the
// depth to the surface, the geoid correction applied exactly once, a cave a reader may not place
// answered as no such cave. None of them can show that the answer arrives legible — that a curve
// which is known over only part of a cave is drawn with a hole in it rather than a line sweeping
// down to the axis, and that the number under the pointer says what it is a number of. A reader
// who cannot tell a gap from a passage at the surface draws a conclusion the data does not carry,
// and that is a defect that lives entirely in the browser.
//
// Why part of it is driven against a supplied answer rather than a built one. Reading the ground
// needs prepared elevation data, and a plain installation has none: obtaining and preparing real
// rasters is an operator's job that takes network, disk and a tile-making service this suite does
// not have. So the first test drives the whole way through and asserts the one thing the server
// truly answers here — that with no elevation data the panel says so instead of drawing a cave at
// sea level — and the second hands the page a profile with a hole in it, because the hole is what
// has to be seen and no installation this suite can build would produce one on demand.
//
// Budget it as slow rather than broken: it uploads a survey and waits for it to be read.

/** A profile over 400 m of passage whose middle third no elevation data reaches. */
function profileWithAGap(caveId: string) {
  const sample = (
    distanceAlongM: number,
    overburdenM: number | null,
    outcome: 'sampled' | 'outsideCoverage' = 'sampled',
  ) => ({
    distanceAlongM,
    longitude: 25.61 + distanceAlongM / 100000,
    latitude: 45.62,
    passageAltitudeM: 900 - distanceAlongM / 20,
    outcome,
    groundAltitudeM: overburdenM === null ? null : 900 - distanceAlongM / 20 + overburdenM,
    overburdenM,
    pathIndex: 0,
    segmentIndex: Math.floor(distanceAlongM / 100),
  });

  return {
    caveId,
    basis: 'surveyFlags',
    isApproximation: false,
    surveyModelId: '00000000-0000-0000-0000-000000000001',
    hasAltitudes: true,
    hasTerrain: true,
    passageLengthM: 400,
    coveredSampleCount: 3,
    minOverburdenM: 34.5,
    maxOverburdenM: 61.25,
    meanOverburdenM: 48.75,
    samples: [
      sample(0, 61.25),
      sample(100, 50.5),
      sample(200, null, 'outsideCoverage'),
      sample(300, null, 'outsideCoverage'),
      sample(400, 34.5),
    ],
  };
}

async function createCave(page: Page, name: string) {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  return new URL(page.url()).pathname;
}

/** The card this file is about, found by its heading rather than by position on the page. */
function overburdenCard(page: Page) {
  return page.locator('.ant-card').filter({ hasText: 'Rock overhead' });
}

test('a cave with no elevation data over it is told so, not drawn at sea level', async ({
  page,
}) => {
  const caveName = `E2E Overburden Cave ${Date.now()}`;
  await login(page);
  const cavePath = await createCave(page, caveName);

  // A survey with altitudes in it, because a plan drawing has no passage altitude for a ground
  // height to be measured down to and the panel would rightly refuse for that reason instead.
  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();
  await expect(
    page.getByRole('table').filter({ hasText: 'Survex .3d' }).getByText('Ready'),
  ).toBeVisible({ timeout: 60_000 });

  await gotoRoute(page, cavePath);

  const card = overburdenCard(page);
  await expect(card).toBeVisible({ timeout: 30_000 });

  // Either honest absence is the right answer here and which one it is depends on whether an
  // operator has ever run a build on this installation: no elevation data prepared anywhere, or
  // some prepared and none of it reaching this cave. What must never happen is a curve.
  await expect(
    card.locator('[data-testid="overburden-no-terrain"], [data-testid="overburden-no-coverage"]'),
  ).toBeVisible({ timeout: 30_000 });
  await expect(card.getByTestId('chart-cave-overburden')).toHaveCount(0);
});

test('a curve known over part of a cave is drawn with the hole left open', async ({ page }) => {
  const caveName = `E2E Overburden Curve ${Date.now()}`;
  await login(page);
  const cavePath = await createCave(page, caveName);
  const caveId = cavePath.split('/').filter(Boolean).pop()!;

  await page.route('**/api/v1/caves/*/overburden', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(profileWithAGap(caveId)),
    }),
  );
  await gotoRoute(page, cavePath);

  const card = overburdenCard(page);
  await expect(card).toBeVisible({ timeout: 30_000 });

  // The curve reaches the reader at all: a drawing, not a table of numbers and not a blank card.
  const chart = card.getByTestId('chart-cave-overburden');
  await expect(chart).toBeVisible({ timeout: 30_000 });
  await expect(chart.locator('svg')).toBeVisible({ timeout: 30_000 });

  // The gap is named, and named as the kind of gap it is. Silence here would leave a reader to
  // read the break in the line as a fact about the cave rather than about the elevation data.
  await expect(card.getByTestId('overburden-partial')).toContainText(
    'no prepared elevation data reaches the passage',
  );

  // The figures beside it are taken over the readings that got a ground height and over no others.
  // A mean that counted the two absent readings as no rock would print 29.3 m here.
  await expect(card).toContainText('48.8 m');
  await expect(card).toContainText('3 of 5');

  // Pressing a reading says which one is marked and what its number is a number of. The mark
  // itself is put on the flat map and in the 3D scene, neither of which is on this page — that
  // announcement is covered where it can be asserted without a rendering context.
  const readout = card.getByTestId('overburden-readout');

  // The card sits well down a long cave page, and a press is delivered at a viewport coordinate
  // rather than to an element: with the chart still below the fold every coordinate worked out
  // below addresses a point off-screen, the presses land on nothing, and the failure reads as
  // "the curve does not answer" rather than "the curve was never touched". Being visible is not
  // the same as being in view.
  await chart.scrollIntoViewIfNeeded();

  // Where to press. The curve is a two-pixel line whose height at any x is decided by the data
  // rather than by the frame, so guessing at coordinates — a diagonal, or a grid over the plot —
  // is a coin toss against a stroke a few pixels wide, and a press that lands beside the line puts
  // the readout away again rather than leaving it up for a later assertion to find. The drawing is
  // SVG, so the line is a path and the browser will say exactly where a point on it is: take the
  // curve's own midpoint and press there. That also keeps the press on the drawn stretch, which
  // here is only the run between the first two readings — the rest of this fixture is the hole.
  const points = await chart.evaluate((element) => {
    const onCurve: Array<{ x: number; y: number }> = [];
    for (const path of Array.from(element.querySelectorAll('path'))) {
      const stroke = path.getAttribute('stroke');
      // The band's two edges are drawn transparent and its fill has no stroke; the curve is the
      // one painted line, so it is the one with a visible stroke and a length to walk along.
      if (!stroke || stroke === 'none' || path.getAttribute('stroke-opacity') === '0') continue;
      const length = path.getTotalLength();
      if (length < 8) continue;
      const matrix = path.getScreenCTM();
      if (!matrix) continue;
      for (const fraction of [0.5, 0.25, 0.75, 0.1, 0.9]) {
        const at = path.getPointAtLength(length * fraction);
        onCurve.push({ x: matrix.a * at.x + matrix.c * at.y + matrix.e, y: matrix.b * at.x + matrix.d * at.y + matrix.f });
      }
    }
    return onCurve;
  });
  expect(points.length, 'the curve was not drawn as a line that can be pressed').toBeGreaterThan(0);

  await expect
    .poll(
      async () => {
        for (const point of points) {
          await page.mouse.click(point.x, point.y);
          if ((await readout.count()) > 0) return 1;
        }
        return 0;
      },
      { message: 'pressing the curve never produced a readout', timeout: 60_000 },
    )
    .toBeGreaterThan(0);

  // What the readout says is the whole point of it: a distance, a thickness with its unit, and
  // which piece of the line work answered.
  await expect(readout).toContainText('along');
  await expect(readout).toContainText(' m');
  await expect(readout).toContainText('piece');
});
