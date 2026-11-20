// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';
import { differingPixels, settledPicture } from './settled.ts';

// A cave surveyed twice, and the two surveys looked at together. What this shows that the unit
// tests cannot: that the real viewer bundle reads two files into one model under a name each, that
// a second and a third viewer live on one page with the first, and that nothing in any of it puts
// an error on the console — which the guard this file's `test` comes from fails a test for.

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');

/**
 * The caves this file made, removed again when the test that made them ends — passed or failed.
 *
 * The suite shares one database, and a cave left behind is not inert there: it is on every list
 * and in every count another spec reads.
 */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/** A cave of this run's own, through the form, so nothing here depends on seeded names. */
async function createCave(page: Page, name: string): Promise<string> {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId, 'the cave page names the cave in its address').toBeTruthy();
  madeCaves.push(caveId!);
  return caveId!;
}

/** One survey of the cave, uploaded under the name it is to be listed by. */
async function uploadSurvey(page: Page, token: string, caveId: string, fileName: string) {
  const created = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: fileName, mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(created.status(), await created.text()).toBe(201);
}

test('two surveys of a cave are laid over one another and set side by side, their parts shown and hidden together or apart', async ({
  page,
}) => {
  test.setTimeout(300_000);
  await login(page);
  const caveId = await createCave(page, `E2E Compare ${Date.now()}`);
  const token = await bearerToken(page);

  // The public demonstration survey, twice: last year's survey and this year's are, for a cave
  // nobody has dug in between, the same survey under two names.
  await uploadSurvey(page, token, caveId, 'Survey 2023.3d');
  await uploadSurvey(page, token, caveId, 'Survey 2024.3d');

  await gotoRoute(page, `/caves/${caveId}`);
  // In the list of models, and nowhere else: a survey that has been read is also named by the
  // centerline made from it, further down the same page.
  const earlier = page
    .locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('row')
    .filter({ hasText: 'Survey 2023' });
  await expect(earlier).toBeVisible({ timeout: 30_000 });
  await earlier.getByRole('button', { name: 'View in 3D' }).click();

  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  // The models the viewers on the page are drawing, by the surfaces they are drawn on.
  const surfaces = viewer.locator('.caveview-panel-surface:visible canvas');
  await expect(surfaces).toHaveCount(1);

  // ---- Laid over one another: one viewer, both surveys, and a key that names each ----
  await viewer.getByTestId('caveview-compare-open').click();
  await page.getByRole('menuitem', { name: 'Overlaid' }).click();

  // The key appears only once the two are known to lie in the same place, which they are read
  // into one model to find out.
  const legend = viewer.getByTestId('caveview-compare-legend');
  await expect(legend).toBeVisible({ timeout: 60_000 });
  await expect(legend).toContainText('Survey 2023');
  await expect(legend).toContainText('Survey 2024');
  await expect(viewer.getByTestId('caveview-compare-apart')).toHaveCount(0);
  await expect(surfaces).toHaveCount(1);
  await expect(viewer.getByTestId('caveview-container')).toBeHidden();
  await expect(viewer.getByTestId('caveview-compare-overlay')).toBeVisible();

  /**
   * What a drawing shows at rest, with the pointer off it: the checkboxes are this application's
   * own, and only the drawing says whether the viewer behind them hid the part. A pointer left
   * over a model is over a station, and the viewer says so on the drawing.
   */
  const drawn = async (testId: string) => {
    await viewer.locator('.ant-modal-title').hover();
    return settledPicture(viewer.getByTestId(testId), { stillFor: 1_500 });
  };
  /**
   * How many pixels have to change for a part to have been shown or hidden, measured here rather
   * than fixed: the drawing is pictured twice with nothing changed in between, and a part has to
   * move several times what that moved. The part hidden here is a few short passages, a hundred
   * or two pixels in a model a few hundred pixels tall, and the side-by-side viewers draw it
   * smaller still — so a fixed count large enough to be safe from a redraw sits right at the size
   * of the part itself. The floor keeps a redraw that happens to come back pixel-identical from
   * letting a handful of pixels pass for a part.
   */
  const partDrawnIn = async (testId: string, picture: Buffer) => {
    const sameDrawing = await differingPixels(page, picture, await drawn(testId));
    return Math.max(4 * sameDrawing, 40);
  };
  const overlaidWhole = await drawn('caveview-compare-overlay');
  const partDrawn = await partDrawnIn('caveview-compare-overlay', overlaidWhole);

  // ---- One part of the survey, hidden in both at once ----
  const inEarlier = viewer.getByTestId('caveview-compare-survey-primary-p8.bens_dig');
  const inLater = viewer.getByTestId('caveview-compare-survey-other-p8.bens_dig');
  await expect(inEarlier).toBeChecked();
  await expect(inLater).toBeChecked();
  await expect(viewer.getByTestId('caveview-compare-sync')).toBeChecked();

  await inEarlier.uncheck();

  await expect(inEarlier).not.toBeChecked();
  await expect(inLater).not.toBeChecked();
  // The rest of both is as it was.
  await expect(viewer.getByTestId('caveview-compare-survey-other-p8.christmas_aven')).toBeChecked();
  // And the drawing lost the part.
  const overlaidHidden = await drawn('caveview-compare-overlay');
  expect(
    await differingPixels(page, overlaidWhole, overlaidHidden),
    'hiding the part took it out of the drawing',
  ).toBeGreaterThan(partDrawn);

  // ---- And shown again in one of them only ----
  await viewer.getByTestId('caveview-compare-sync').click();
  await expect(viewer.getByTestId('caveview-compare-sync')).not.toBeChecked();

  await inEarlier.check();

  await expect(inEarlier).toBeChecked();
  await expect(inLater).not.toBeChecked();
  expect(
    await differingPixels(page, overlaidHidden, await drawn('caveview-compare-overlay')),
    'showing the part in one survey drew it again',
  ).toBeGreaterThan(partDrawn);

  // ---- Side by side: a viewer for each, under its name ----
  await viewer.getByTestId('caveview-compare-mode').getByText('Side by side').click();

  await expect(viewer.getByTestId('caveview-compare-other').locator('canvas').first()).toBeAttached({
    timeout: 60_000,
  });
  await expect(viewer.getByTestId('caveview-compare-other-loading')).toHaveCount(0, { timeout: 60_000 });
  await expect(surfaces).toHaveCount(2);
  await expect(viewer.getByTestId('caveview-compare-name-primary')).toHaveText('Survey 2023');
  await expect(viewer.getByTestId('caveview-compare-name-other')).toHaveText('Survey 2024');
  await expect(viewer.getByTestId('caveview-compare-link')).toBeChecked();
  await expect(legend).toHaveCount(0);
  // What was hidden stays hidden across the change of arrangement.
  await expect(inEarlier).toBeChecked();
  await expect(inLater).not.toBeChecked();

  // In the second viewer's drawing too, which is a viewer built after the part was hidden: shown
  // there, the drawing changes; hidden again, it is the drawing it was.
  const apartHidden = await drawn('caveview-compare-other');
  const partDrawnApart = await partDrawnIn('caveview-compare-other', apartHidden);
  await inLater.check();
  await expect(inLater).toBeChecked();
  const apartShown = await drawn('caveview-compare-other');
  const shownDiffers = await differingPixels(page, apartHidden, apartShown);
  expect(shownDiffers, 'showing the part drew it in the second viewer').toBeGreaterThan(partDrawnApart);
  await inLater.uncheck();
  await expect(inLater).not.toBeChecked();
  expect(
    await differingPixels(page, apartHidden, await drawn('caveview-compare-other')),
    'hiding it again took it back out of the second viewer',
  ).toBeLessThan(shownDiffers / 5);

  // ---- And back to the one survey ----
  await viewer.getByTestId('caveview-compare-stop').click();

  await expect(surfaces).toHaveCount(1);
  await expect(viewer.getByTestId('caveview-container')).toBeVisible();
  await expect(viewer.getByTestId('caveview-compare-open')).toBeVisible();
  await expect(viewer.getByTestId('caveview-compare-lists')).toHaveCount(0);
});
