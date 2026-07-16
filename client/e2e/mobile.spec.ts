// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';
import { login, overlayTreeNode } from './helpers.ts';

// Runs in the `mobile-android` project only (Pixel 7, 393x851, touch). These cover the
// phone *layout*: the docks-as-drawers swap and the chrome that has to step aside at
// this width. Touch editing gestures land with the editing batch.

test('docks become drawers, and the details drawer opens when something is picked', async ({ page }) => {
  await login(page);

  // Neither dock is mounted at this width — the map gets the whole canvas.
  await expect(page.getByText('Base layers')).toHaveCount(0);
  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);

  // The same dock toggle that collapses the pane on desktop opens the drawer here.
  await page.getByTestId('map-dock-toggle-left').click();
  await expect(page.getByText('Base layers')).toBeVisible();

  // The layer composer is fully usable from inside the drawer.
  const entrances = overlayTreeNode(page, 'Cave entrances');
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toBeVisible();
  await entrances.locator('.ant-tree-checkbox').click();
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toHaveCount(0);
  await entrances.locator('.ant-tree-checkbox').click();
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toBeVisible();

  // Close it by tapping the map beside the drawer, the way a thumb would. The mask spans
  // the viewport but the 320px panel sits on top of most of it, so aim at the strip of
  // mask the panel leaves exposed rather than at its centre.
  await page.locator('.ant-drawer-mask').click({ position: { x: 370, y: 400 } });
  await expect(page.getByText('Base layers')).toBeHidden();

  // Centre on the demo cave through the search strip, then zoom out below the
  // clustering threshold so the entrances aggregate at the view centre.
  await page.getByPlaceholder('Search caves or places…').fill('Peștera Demo');
  await page.locator('.ant-select-dropdown').getByText(/Peștera Demo Mare/).first().click();
  for (let i = 0; i < 6; i++) {
    await page.locator('.ol-zoom-out').click();
  }

  // Picking on the map opens the details drawer by itself: with no dock on screen, a
  // selection would otherwise appear to do nothing at all. Retried because zoom
  // animations and the debounced bbox loader settle at their own pace.
  const canvas = page.locator('.map-canvas');
  await expect(async () => {
    await canvas.click();
    await expect(page.getByText(/entrances in this area/)).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // ...and it is a real drawer over the map, not the desktop pane.
  await expect(page.locator('.ant-drawer-right')).toBeVisible();
  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);

  // The cluster's members are reachable and select their entrance from in there.
  await page.getByRole('button', { name: /entrance|Peșter/i }).first().click();
  await expect(page.getByRole('heading', { name: /Peștera Demo Mare/ })).toBeVisible({ timeout: 15_000 });
});

test('desktop-only chrome steps aside and the save cluster outlives the tool strip', async ({ page }) => {
  await login(page);

  // Pop-out windows and the jump-to-scale combo are meaningless/clutter at this width.
  await expect(page.getByTestId('map-popout-registry')).toHaveCount(0);
  await expect(page.getByTestId('map-popout-viewer3d')).toHaveCount(0);
  await expect(page.locator('.map-scale-overlay')).toHaveCount(0);
  // Locate-me stays: it is more useful on a phone than anywhere else.
  await expect(page.locator('.map-popout-overlay')).toBeVisible();

  // Search takes the width the pop-outs gave up.
  const search = page.getByPlaceholder('Search caves or places…');
  await expect(search).toBeVisible();
  const searchWidth = (await search.boundingBox())!.width;
  expect(searchWidth).toBeGreaterThan(200);

  // The edit bar splits in two: a strip that scrolls, and a save cluster that does not.
  const strip = page.getByTestId('edit-tool-strip');
  const saveCluster = page.getByTestId('edit-save-cluster');
  await expect(strip).toBeVisible();
  await expect(saveCluster).toBeVisible();

  // The strip really does overflow at this width — otherwise the rest of this proves
  // nothing about scrolling.
  expect(await strip.evaluate((el) => el.scrollWidth > el.clientWidth)).toBe(true);

  // Scrolled to its far end, every tool has been reachable and Save is still on screen.
  await strip.evaluate((el) => el.scrollTo({ left: el.scrollWidth }));
  await expect(strip.getByTestId('tool-add-entrance')).toBeInViewport();
  await expect(saveCluster.getByRole('button', { name: /Save/ })).toBeInViewport();
});
