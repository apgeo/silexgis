// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

async function createCave(page: Page, name: string) {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
}

/** The centerlines card, so nothing here can drive the compiled-models uploader by accident. */
function centerlines(page: Page) {
  return page.locator('.ant-card').filter({ hasText: 'Centerlines' });
}

/**
 * Both figures are worked out when the page is opened, and nothing tells the page that a survey
 * finished being read while it was already on screen. Coming back to the cave by address is what
 * a person does, and it is what makes the figures reflect the line work that now exists.
 */
async function reopenCave(page: Page) {
  await gotoRoute(page, new URL(page.url()).pathname);
}

/**
 * A cave's survey figures, on the page, computed from line work a person really uploaded.
 *
 * What these show that no unit test can. Every unit test on these panels hands them a response
 * somebody wrote by hand, so they prove the drawing and not the measurement. Here the numbers come
 * out of a survey file and a centerline that went through the whole server — upload, background
 * reading, extraction, statistics — and what is checked is that the three answers the server can
 * give arrive on screen as three visibly different things: figures measured from a compiled
 * survey, figures approximated from a centerline and saying so, and a steepness the line work
 * cannot support being refused in words rather than drawn as a cave that is level everywhere.
 * A reader who cannot tell those apart draws a conclusion the data does not carry, and no
 * assertion on a hand-written response can catch that.
 */
test('a cave measured from its compiled survey shows its passage rose and its figures', async ({
  page,
}) => {
  const caveName = `E2E Stats Survey Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();

  // The reading has to have finished before there are any legs to measure: the statistics are
  // computed from the parsed shots, not from the uploaded bytes.
  await expect(page.getByRole('table').filter({ hasText: 'Survex .3d' }).getByText('Ready')).toBeVisible({
    timeout: 60_000,
  });

  await reopenCave(page);

  // The block of figures, populated rather than a row of dashes.
  const figures = page.getByTestId('cave-survey-statistics');
  await expect(figures).toBeVisible({ timeout: 30_000 });
  const surveyed = figures.locator('.ant-statistic').filter({ hasText: 'Length surveyed' });
  await expect(surveyed).toBeVisible();
  await expect(surveyed.locator('.ant-statistic-content')).not.toHaveText('—');

  // And it says what it measured. This sentence is the whole reason the next test's cave is not
  // the same measurement, and a reader who never sees it will compare the two as though it were.
  await expect(figures.getByText(/Measured from the compiled survey/)).toBeVisible();

  // And which upload it measured. A corrected re-export is a new upload rather than a replacement,
  // so a cave can hold several and only one of them produced these figures; two sets of numbers
  // about one cave are the same measurement only if they came from the same upload.
  await expect(figures.getByText(/Measured from the survey/)).toBeVisible();

  const rose = page.getByTestId('chart-rose');
  await expect(rose).toBeVisible();

  // Axial, and drawn as such: every sector the survey filled is drawn again on the opposite side,
  // because a passage running one way and the same passage surveyed from its other end are one
  // trend. A rose missing the mirrored half looks perfectly plausible and is a different diagram.
  const measured = rose.locator('path[data-petal="measured"]');
  await expect(measured.first()).toBeVisible();
  expect(await rose.locator('path[data-petal="mirrored"]').count()).toBe(await measured.count());

  // The diagram is one image with a sentence a screen reader can follow, not a heap of shapes.
  // Both the name of the drawing and the sentence describing it are announced, so what a reader
  // who cannot see it is told includes which weighting produced the shape.
  const image = rose.getByRole('img');
  await expect(image).toHaveAccessibleName(/Passage rose/);
  await expect(image).toHaveAccessibleName(/weighted by surveyed length/);

  // Length and count are different questions, and neither is the answer without saying which.
  await rose.locator('.ant-segmented-item').filter({ hasText: 'By count' }).click();
  await expect(image).toHaveAccessibleName(/weighted by the number of survey legs/);
});

test('a cave with only a centerline says its figures are an approximation', async ({ page }) => {
  const caveName = `E2E Stats Centerline Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await centerlines(page)
    .locator('input[type="file"]')
    .setInputFiles(path.join(fixtures, 'e2e-centerline-3d.geojson'));
  await expect(centerlines(page).getByText('e2e-centerline-3d')).toBeVisible({ timeout: 20_000 });

  await reopenCave(page);

  const figures = page.getByTestId('cave-survey-statistics');
  await expect(figures).toBeVisible({ timeout: 30_000 });

  // The caveat, in the same place and the same register as the sentence above it on the other
  // cave. Its absence would leave two roses that look alike and are not comparable.
  await expect(figures.getByText(/Approximated from the stored centerline/)).toBeVisible();
  await expect(figures.getByText(/Measured from the compiled survey/)).toHaveCount(0);

  await expect(page.getByTestId('chart-rose')).toBeVisible();
  await expect(
    page.getByTestId('cave-orientation').getByText(/Approximated from the stored centerline/),
  ).toBeVisible();
});

test('a cave whose line work carries no altitudes is refused a steepness, in words', async ({
  page,
}) => {
  const caveName = `E2E Stats Flat Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await centerlines(page)
    .locator('input[type="file"]')
    .setInputFiles(path.join(fixtures, 'e2e-centerline-flat.geojson'));
  await expect(centerlines(page).getByText('e2e-centerline-flat')).toBeVisible({ timeout: 20_000 });

  await reopenCave(page);

  // Which way the passages run is still answerable from a plan drawing, so the rose is drawn.
  await expect(page.getByTestId('chart-rose')).toBeVisible({ timeout: 30_000 });

  // How steeply they run is not, and the refusal is a stated reason rather than an empty chart or
  // a mean of zero. Zero would say the cave is level, which is a claim about the cave; this
  // drawing can only make a claim about the drawing.
  const refused = page.getByTestId('dip-refused');
  await expect(refused).toBeVisible();
  await expect(refused.getByText(/Steepness cannot be worked out/)).toBeVisible();
  await expect(refused.getByText(/carries no altitudes/)).toBeVisible();
  await expect(page.getByTestId('chart-dip')).toHaveCount(0);
  await expect(page.getByTestId('cave-orientation').getByText(/Mean steepness/)).toHaveCount(0);

  // A dash on its own reads as missing data. The panel says why the vertical figures are blank,
  // in the same register as the steepness refusal beside it: a plan drawing of a cave is not a
  // flat cave, and neither the reader nor the panel may turn one into the other.
  await expect(
    page.getByTestId('cave-survey-statistics').getByText(/carries no altitudes/),
  ).toBeVisible();

  // A vertical extent nothing measured is a dash, never a zero.
  const vertical = page
    .getByTestId('cave-survey-statistics')
    .locator('.ant-statistic')
    .filter({ hasText: 'Vertical extent' });
  await expect(vertical.locator('.ant-statistic-content')).toHaveText('—');
});
