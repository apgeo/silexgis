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
  await expect(page.getByRole('heading', { name })).toBeVisible({
    timeout: 15_000,
  });
}

/**
 * Coming back to the cave by address is what a person does after an upload finishes, and it is
 * what puts the figures measured from the new line work on screen.
 */
async function reopenCave(page: Page) {
  await gotoRoute(page, new URL(page.url()).pathname);
}

/**
 * The shape of a cave's passage network, on the page, measured from a survey a person uploaded.
 *
 * What this shows that the unit tests cannot. Every unit test on this panel hands it a response
 * somebody wrote by hand, so it proves the drawing and not the measurement. Here the figures come
 * out of a real survey file that went through the whole server — upload, background reading,
 * station and shot extraction, contraction of the network, storage beside the file — and what is
 * checked is that they arrive on screen as figures a reader can interpret rather than as a grid of
 * bare terms out of a literature nobody on this page has read. The gloss beside each number is
 * load-bearing and not decoration: a panel that lost it would still show every number and would
 * still look entirely finished.
 */
test('a cave measured from its survey shows the shape of its passage network, in words', async ({ page }) => {
  const caveName = `E2E Topology Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();

  // The figures are measured when the file is read and stored beside it, so there is nothing to
  // show until that reading has finished.
  await expect(page.getByRole('table').filter({ hasText: 'Survex .3d' }).getByText('Ready')).toBeVisible({
    timeout: 60_000,
  });

  await reopenCave(page);

  const topology = page.getByTestId('cave-topology');
  await expect(topology).toBeVisible({ timeout: 30_000 });

  // Counts of the network as surveyed, populated rather than dashes.
  const stations = topology.getByTestId('topology-nodes');
  await expect(stations).toBeVisible();
  await expect(stations).not.toHaveText('—');

  // The contraction is the substantial piece of work behind almost every figure here, and it is
  // the one whose absence would leave the rest looking perfectly plausible: without it the
  // contracted network is simply the surveyed one, and every ratio counted over it is wrong.
  // Fewer junctions and dead ends than stations is what says the contraction ran.
  const surveyed = Number((await stations.textContent())?.replace(/[^\d]/g, ''));
  const contracted = Number(
    (await topology.getByTestId('topology-reducedNodes').textContent())?.replace(/[^\d]/g, ''),
  );
  expect(surveyed).toBeGreaterThan(0);
  expect(contracted).toBeGreaterThan(0);
  expect(contracted).toBeLessThan(surveyed);

  // Every figure carries a sentence saying what it means. This is the difference between a number
  // a caver can act on and one that only looks like information.
  await expect(topology.getByText(/genuinely different ways round the network/)).toBeVisible();
  await expect(topology.getByText(/Below two is a branching cave/)).toBeVisible();

  // And every answer says what the reading of the file dropped and merged, whether or not anything
  // was lost. A network that silently lost legs still produces reasonable-looking numbers, and
  // this is the only thing on the page that would say so.
  await expect(page.getByTestId('cave-topology-completeness')).toBeVisible();

  // Which upload was measured — a cave may hold several, and figures from two of them are not the
  // same measurement.
  await expect(topology.getByText(/Measured from the survey/)).toBeVisible();
});

test('a cave with no survey file shows no network figures at all', async ({ page }) => {
  // Not an empty panel of dashes: "0 independent loops" is a statement about a cave, and the
  // application must not make it about one nobody has surveyed. The rest of the page still works,
  // which is what separates this from the panel having crashed.
  const caveName = `E2E Topology Unsurveyed Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  // Nor is the question asked. The route refuses a cave whose network was never measured the same
  // way it refuses a caller who may not place the cave, so asking here would fail — and a failed
  // request is an error on the browser console of nearly every cave page in the application, which
  // is how the errors that mean something get buried. Watched rather than assumed, because the
  // panel drawing nothing looks identical either way.
  const asked: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/topology')) asked.push(request.url());
  });

  await reopenCave(page);

  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({
    timeout: 15_000,
  });
  await expect(page.getByTestId('cave-topology')).toHaveCount(0);
  expect(asked).toEqual([]);
});
