// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * A doline drawn as an outline, and the outline measured.
 *
 * What this shows that no server test can. The measuring is proved against known shapes by the
 * integration tests; what only a browser can show is that a person can get there at all — that the
 * doline kind, which used to be a marker and nothing else, now offers the choice of drawing an
 * outline, and that having drawn one they are shown its area, how round it is, how long and how
 * wide it is and which way it lies, rather than a feature page with nothing new on it.
 *
 * The chooser is the load-bearing half. Widening a kind to accept a second shape without offering
 * the choice arms the tool with a default forever, and the shape the kind was widened for can never
 * be drawn — a change that passes every server test and ships a feature nobody can reach.
 */
test('a doline is drawn as an outline and its measured shape is shown', async ({ page }) => {
  const featureName = `E2E Doline ${Date.now()}`;
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();

  await toolbar.getByRole('button', { name: /Feature type/ }).click();
  await page.getByRole('button', { name: 'Sinkhole / Doline' }).click();

  // The kind accepts a marker and an outline, so the toolbar offers both. A kind accepting one
  // shape shows no chooser at all, which is why its presence here is worth asserting.
  const shapeChoice = toolbar.getByTestId('draw-shape-choice');
  await expect(shapeChoice).toBeVisible();
  await shapeChoice.getByText('Outline').click();

  // Three corners and a double-click to close the ring — the ordinary way a polygon is finished
  // with a mouse.
  const canvas = page.locator('.map-canvas');
  await canvas.click({ position: { x: 380, y: 220 } });
  await canvas.click({ position: { x: 470, y: 230 } });
  await canvas.click({ position: { x: 450, y: 320 } });
  await canvas.dblclick({ position: { x: 380, y: 320 } });

  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible({ timeout: 15_000 });
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK' }).click();

  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // Reached from the table rather than from the map, so the assertion below is about the feature
  // page and not about whatever the map's selection panel happens to show.
  await page.goto('/features');
  const row = page.getByRole('row', { name: new RegExp(featureName) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  // The name cell is text, not an anchor: this table carries its navigation on the row itself,
  // so the row is what a person clicks to open the feature.
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const card = page.locator('.ant-card').filter({ hasText: 'Measured shape' });
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card.getByTestId('feature-morphometry')).toBeVisible();

  // Metres, not degrees: the area is stated in square metres. An outline of this size on the demo
  // map is thousands of them, whereas the same ring measured in the coordinates it is stored in
  // would be a millionth of one — so a unit is worth asserting, and so is the parameter set being
  // there at all rather than a card of dashes.
  await expect(card.getByText('m\u00b2')).toBeVisible();
  await expect(card.getByText('Circularity')).toBeVisible();
  await expect(card.getByText('Elongation')).toBeVisible();
  await expect(card.getByTestId('feature-morphometry').getByText('Long-axis bearing')).toBeVisible();

  // The alignment is stated as an alignment. A long axis has no direction, and a page presenting
  // it as a bearing would let two readings of the same doline be recorded as opposite.
  await expect(card.getByText(/alignment, not a direction/)).toBeVisible();

  // Cleanup, so a re-run of this file starts from the same map.
  await page.goto('/features');
  const cleanup = page.getByRole('row', { name: new RegExp(featureName) });
  await cleanup.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
});
