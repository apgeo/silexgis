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

function centerlines(page: Page) {
  return page.locator('.ant-card').filter({ hasText: 'Centerlines' });
}

/** The figures are computed when the page is opened; coming back by address is what a person does. */
async function reopen(page: Page) {
  await gotoRoute(page, new URL(page.url()).pathname);
}

/**
 * The storeys a cave appears to be cut at, confirmed by a person and surviving a reload.
 *
 * What this shows that no server test can. The arithmetic is proved against a hand-computed break
 * set by the unit tests and the routes are proved by the integration tests; what only a browser can
 * show is that the two halves of the idea reach a reader as two different things. The machine's
 * proposal has to arrive labelled as a reading of a histogram, the reading a person confirmed has
 * to arrive as a stored conclusion with a way to withdraw it, and the second has to still be there
 * after the page is thrown away and asked for again. A screen that merged them — or that saved the
 * proposal by itself — would pass every server test and quietly turn arithmetic into a record.
 */
test('a cave at two heights proposes two levels and a confirmed reading survives a reload', async ({
  page,
}) => {
  const caveName = `E2E Levels Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await centerlines(page)
    .locator('input[type="file"]')
    .setInputFiles(path.join(fixtures, 'e2e-centerline-two-storeys.geojson'));
  await expect(centerlines(page).getByText('e2e-centerline-two-storeys')).toBeVisible({
    timeout: 20_000,
  });

  await reopen(page);

  const card = page.locator('.ant-card').filter({ hasText: 'Passage by height' });
  await expect(card).toBeVisible({ timeout: 30_000 });

  // The histogram itself, drawn from real line work rather than from a hand-written response.
  await expect(card.getByTestId('chart-cave-hypsometry')).toBeVisible();

  // Two storeys in the drawing come back as two proposed levels. The count is asserted rather
  // than "at least one", because a picker that cut everywhere would also show a table.
  const proposed = card.getByTestId('hypsometry-proposed');
  await expect(proposed).toBeVisible();
  await expect(proposed.locator('tbody tr')).toHaveCount(2);

  // And it says what the bands are. A screen that showed them without this sentence would be
  // presenting arithmetic as a statement about the cave.
  await expect(card.getByText(/not a statement about the cave/)).toBeVisible();

  // Nothing is recorded until somebody records it.
  await expect(card.getByTestId('hypsometry-saved')).toHaveText(/No reading has been recorded yet/);

  await card.getByTestId('hypsometry-confirm').click();
  await expect(page.getByText('The reading was recorded.')).toBeVisible({ timeout: 15_000 });

  // Thrown away and asked for again: this is the assertion the stored record exists for.
  await reopen(page);
  const reloaded = page.locator('.ant-card').filter({ hasText: 'Passage by height' });
  await expect(reloaded.getByTestId('hypsometry-saved')).toHaveText(
    /Levels in the recorded reading: 2/,
    { timeout: 30_000 },
  );

  // And it can be withdrawn, so a reading recorded by mistake is not permanent.
  await reloaded.getByTestId('hypsometry-clear').click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(reloaded.getByTestId('hypsometry-saved')).toHaveText(
    /No reading has been recorded yet/,
    { timeout: 15_000 },
  );
});

/**
 * The cave's passages set against the rock around them, and the one comparison this installation
 * cannot make said in words rather than left as a dead affordance.
 */
test('a cave shows its passage rose against the mapped structure', async ({ page }) => {
  const caveName = `E2E Structure Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await centerlines(page)
    .locator('input[type="file"]')
    .setInputFiles(path.join(fixtures, 'e2e-centerline-3d.geojson'));
  await expect(centerlines(page).getByText('e2e-centerline-3d')).toBeVisible({ timeout: 20_000 });

  await reopen(page);

  const card = page.locator('.ant-card').filter({ hasText: 'Passage against the mapped structure' });
  await expect(card).toBeVisible({ timeout: 30_000 });

  const comparison = card.getByTestId('cave-structure-comparison');
  await expect(comparison).toBeVisible();

  // The cave's own trend is drawn. The other half is whatever this installation has mapped, which
  // on a fresh demo map is nothing — and the panel says so rather than drawing an empty rose.
  // Exactly, because the rose's own accessible description begins with the same words: a loose
  // match here finds the heading and the description and fails on the ambiguity.
  await expect(comparison.getByText('Passage trend', { exact: true })).toBeVisible();

  // Nought is not the answer when one side is empty, and the panel refuses to give one.
  await expect(comparison.getByTestId('cave-structure-comparison-no-divergence')).toBeVisible();

  // Bedding and joint readings are the comparison this installation cannot make. Said here, so a
  // reader is not left looking for a control that was never built.
  await expect(card.getByText(/Bedding and joint readings/)).toBeVisible();

  // And the surface comparison is named and placed, rather than being absent without explanation.
  await expect(card.getByText(/asked of an area, on that area's own page/)).toBeVisible();
});
