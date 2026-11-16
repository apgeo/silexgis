// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, overlayTreeNode } from './helpers.ts';

/**
 * The depth-coloured passage overlay in the map composer: switched on it asks the server for the
 * passage in view, explains what it got in its row, and a saved view puts it back.
 *
 * What only a browser shows. The integration tests prove which caves are drawn, in which class and
 * for whom; they cannot show that the toggle reaches the loader, that the answer lands in the
 * layer's row rather than nowhere, or that a saved view brings the layer back and asks again. The
 * development instance may or may not hold an elevation model and a cave with altitudes, so the
 * assertion is that the row says one of the two true things — a key, or that no model is built —
 * and never a refusal for this signed-in administrator.
 */
const plateau = { lat: 45.525, lon: 25.44, zoom: 11 };

test('the passage depth overlay asks for the window, explains the answer and comes back with a saved view', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const viewName = `E2E Depth View ${Date.now()}`;
  await login(page);
  await gotoRoute(page, mapAddress(plateau));

  const answered = page.waitForResponse((response) => response.url().includes('/api/v1/map/passage-depth'), {
    timeout: 60_000,
  });
  const node = overlayTreeNode(page, 'Rock over the passage');
  await expect(node).toBeVisible({ timeout: 30_000 });
  await node.locator('.ant-tree-checkbox').click();
  expect((await answered).status()).toBe(200);

  const row = page.getByTestId('map-passage-depth');
  await expect(row).toBeVisible({ timeout: 30_000 });
  await expect(row.getByTestId('map-passage-depth-refused')).toHaveCount(0);
  const key = row.getByTestId('map-passage-depth-legend-row');
  const noModel = row.getByTestId('map-passage-depth-no-terrain');
  await expect(key.first().or(noModel)).toBeVisible({ timeout: 30_000 });
  if (await key.first().isVisible()) {
    // Seven classes: one for passage reading above its ground and six bands of rock.
    await expect(key).toHaveCount(7);
    await expect(key.last()).toContainText('More than 200 m');
  }

  const dock = page.locator('.map-workspace-panel').first();
  await dock.getByPlaceholder('View name…').fill(viewName);
  await dock.getByRole('button', { name: 'Save current view', exact: true }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  await overlayTreeNode(page, 'Rock over the passage').locator('.ant-tree-checkbox').click();
  await expect(page.getByTestId('map-passage-depth')).toHaveCount(0);

  const dockAgain = page.locator('.map-workspace-panel').first();
  const viewLink = dockAgain.getByRole('button', { name: viewName, exact: true });
  await expect(viewLink).toBeVisible({ timeout: 15_000 });
  const askedAgain = page.waitForResponse((response) => response.url().includes('/api/v1/map/passage-depth'), {
    timeout: 60_000,
  });
  await viewLink.click();
  await expect(overlayTreeNode(page, 'Rock over the passage').locator('.ant-tree-checkbox-checked')).toBeVisible({
    timeout: 30_000,
  });
  expect((await askedAgain).status()).toBe(200);
  await expect(page.getByTestId('map-passage-depth')).toBeVisible({ timeout: 30_000 });

  await page
    .getByRole('listitem')
    .filter({ hasText: viewName })
    .getByRole('button', { name: `Delete the view ${viewName}`, exact: true })
    .click();
  await expect(dockAgain.getByRole('button', { name: viewName, exact: true })).toHaveCount(0, { timeout: 15_000 });
});
