// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { registerAccount } from './arrange.ts';
import { gotoRoute, login, overlayTreeNode } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';
import { CORNER_CONTROLS, overlapsWith, withheldLines } from './workspaceFlow.ts';

// Runs in the phone projects. A phone keeps one pane, the map, with its two docks as drawers: the
// arrangement the account carries from a desktop — the table under the map, the docks swapped — is
// a desktop arrangement and changes nothing here, and neither the workspace menu nor the key that
// fills the workspace with a pane is offered.

test('on a phone the docks stay drawers, whatever the account arranged on a desktop', async ({ page }) => {
  const member = await registerAccount(page.request, 'workspace-phone');
  await login(page, member.email, member.password);
  // The arrangement a desktop would have stored: a table under the map, the docks swapped.
  const stored = await page.request.fetch('/api/v1/me/preferences', {
    method: 'PATCH',
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    data: {
      preferences: {
        workspace: { scene3dPlace: 'bottom', caveViewPlace: 'left', table: true, docksSwapped: true },
      },
    },
  });
  expect(stored.ok(), `storing the arrangement answered ${stored.status()}`).toBeTruthy();
  await gotoRoute(page, '/map');

  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);
  await expect(page.locator('[data-workspace-pane="table"]')).toHaveCount(0);
  await expect(page.getByTestId('workspace-menu')).toHaveCount(0);
  await expect(page.getByTestId('map-maximise')).toHaveCount(0);

  // The key does nothing on a phone.
  await page.getByRole('application', { name: /^Map\./ }).focus();
  await page.keyboard.press('m');
  await expect(page.locator('.workspace-pane-maximised')).toHaveCount(0);

  // The layer drawer is still the left one, and the details drawer keeps its "In view" tab: there
  // is no table here to have taken it.
  await page.getByTestId('map-dock-toggle-left').click();
  await expect(page.getByTestId('map-left-drawer').getByText('Base layers')).toBeVisible();
  await page.locator('.ant-drawer-mask').click({ position: { x: 370, y: 400 } });
  await expect(page.getByText('Base layers')).toBeHidden();
  await page.getByTestId('map-dock-toggle-right').click();
  await expect(page.getByTestId('map-right-drawer').getByRole('tab', { name: 'In view' })).toBeVisible();
});

test('on a phone the hidden survey lines notice never lies over the map controls', async ({ page }) => {
  const member = await registerAccount(page.request, 'workspace-phone-notice');
  await withheldLines(page);
  await login(page, member.email, member.password);
  await gotoRoute(page, '/map');
  await page.getByTestId('map-dock-toggle-left').click();
  const node = overlayTreeNode(page, 'Cave centerlines');
  await expect(node).toBeAttached({ timeout: 15_000 });
  // Switched on by an event sent to the box itself: the node sits low in the drawer's virtual list,
  // which keeps moving under a pointer that scrolls it into view, and what is under test here is the
  // notice on the map, not reaching that row by finger.
  await node.locator('.ant-tree-checkbox').dispatchEvent('click');
  await expect(node.locator('.ant-tree-checkbox-checked')).toHaveCount(1);
  await page.locator('.ant-drawer-mask').click({ position: { x: 370, y: 400 } });
  await expect(page.getByText('Base layers')).toBeHidden();

  const notice = page.getByTestId('map-centerline-notice');
  await expect(notice).toBeVisible({ timeout: 15_000 });
  const clear = Object.fromEntries(CORNER_CONTROLS.map((selector) => [selector, false]));
  expect(await overlapsWith(page, notice, CORNER_CONTROLS)).toEqual(clear);
});
