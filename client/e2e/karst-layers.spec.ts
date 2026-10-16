// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, overlayTreeNode } from './helpers.ts';

/**
 * The two karst overlays in the map composer: the server's density grid and the
 * karstification index over the areas in view, each with its key, and both kept by a saved view.
 *
 * What only a browser shows. The integration tests prove the cells and the index are counted
 * and protected correctly; they cannot show that the client asks for them at all, draws what it
 * was sent rather than nothing, explains what the colours mean, and puts both layers back when a
 * saved view is opened again. A layer that silently drew nothing and a layer that drew the
 * server's answer look the same in a payload test.
 *
 * The camera is the demo karst plateau at a zoom where the window needs no coarser cell than the
 * installation's finest, so the drawn cells are the floor's own. Nothing asserts a number that
 * depends on what the demo instance holds beyond "at least one".
 */
const plateau = { lat: 45.525, lon: 25.44, zoom: 11 };

test('the density grid and the karstification index draw, explain themselves and come back with a saved view', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const viewName = `E2E Karst View ${Date.now()}`;
  await login(page);
  await gotoRoute(page, mapAddress(plateau));

  // The density grid: switched on, it asks the server for its cells and draws them.
  const densityAnswer = page.waitForResponse(
    (response) => response.url().includes('/api/v1/map/density') && response.status() === 200,
    { timeout: 60_000 },
  );
  const densityNode = overlayTreeNode(page, 'Cave density (grid)');
  await expect(densityNode).toBeVisible({ timeout: 30_000 });
  await densityNode.locator('.ant-tree-checkbox').click();
  const grid = (await (await densityAnswer).json()) as {
    cellMetres: number;
    minimumCellMetres: number;
    cells: { count: number }[];
  };
  expect(grid.cells.some((cell) => cell.count > 0)).toBe(true);
  // At this zoom the floor fits, so the cell drawn is the finest the installation publishes.
  expect(grid.cellMetres).toBe(grid.minimumCellMetres);

  // The row states the cell actually used, and the key has a band for the cells that hold caves —
  // a legend with bands is only drawn when at least one cell has a value above nothing.
  const densityRow = page.getByTestId('map-karst-density');
  await expect(densityRow.getByTestId('map-karst-density-cell')).toContainText(`${Math.round(grid.cellMetres)} m`, {
    timeout: 30_000,
  });
  await expect(densityRow.getByTestId('map-karst-density-legend-row').first()).toBeVisible();
  await expect(densityRow.getByTestId('map-karst-density-refused')).toHaveCount(0);

  // The karstification index: switched on, it fills the areas in view and shows its key.
  const indexAnswer = page.waitForResponse(
    (response) => response.url().includes('/api/v1/map/karst-index') && response.status() === 200,
    { timeout: 60_000 },
  );
  const indexNode = overlayTreeNode(page, 'Karstification index (areas)');
  await indexNode.locator('.ant-tree-checkbox').click();
  const index = (await (await indexAnswer).json()) as { areas: { name: string | null }[] };
  expect(index.areas.length).toBeGreaterThan(0);
  const indexRow = page.getByTestId('map-karst-index');
  await expect(indexRow.getByTestId('map-karst-index-shown')).toContainText(String(index.areas.length), {
    timeout: 30_000,
  });
  await expect(indexRow.getByTestId('map-karst-index-legend-row')).toHaveCount(6);
  await expect(indexRow.getByText('Very high')).toBeVisible();

  // Save the workspace with both layers on.
  const dock = page.locator('.map-workspace-panel').first();
  await dock.getByPlaceholder('View name…').fill(viewName);
  await dock.getByRole('button', { name: 'Save current view', exact: true }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Switch both off, then open the saved view: it puts both back, and each asks the server again
  // rather than showing nothing. (Switched off by hand rather than by reloading: the workspace
  // may carry its own layer state across a reload, which would prove nothing about the view.)
  await overlayTreeNode(page, 'Cave density (grid)').locator('.ant-tree-checkbox').click();
  await overlayTreeNode(page, 'Karstification index (areas)').locator('.ant-tree-checkbox').click();
  await expect(overlayTreeNode(page, 'Cave density (grid)').locator('.ant-tree-checkbox-checked')).toHaveCount(0);
  await expect(page.getByTestId('map-karst-density')).toHaveCount(0);
  await expect(page.getByTestId('map-karst-index')).toHaveCount(0);

  const dockAgain = page.locator('.map-workspace-panel').first();
  const viewLink = dockAgain.getByRole('button', { name: viewName, exact: true });
  await expect(viewLink).toBeVisible({ timeout: 15_000 });
  await viewLink.click();

  await expect(overlayTreeNode(page, 'Cave density (grid)').locator('.ant-tree-checkbox-checked')).toBeVisible({
    timeout: 30_000,
  });
  await expect(overlayTreeNode(page, 'Karstification index (areas)').locator('.ant-tree-checkbox-checked')).toBeVisible();
  await expect(page.getByTestId('map-karst-density-legend-row').first()).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('map-karst-index-shown')).toBeVisible({ timeout: 30_000 });

  // Clean up the view.
  await page
    .getByRole('listitem')
    .filter({ hasText: viewName })
    .getByRole('button', { name: `Delete the view ${viewName}`, exact: true })
    .click();
  await expect(dockAgain.getByRole('button', { name: viewName, exact: true })).toHaveCount(0, { timeout: 15_000 });
});
