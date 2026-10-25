// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { uniquePng } from './png.ts';

/**
 * Choosing which attachment a phone fetches with "the usual files".
 *
 * Done on a cave of this run's own, so the flag is read back from the server rather than from a
 * screen that might simply remember what it was told: the switch is flipped in the details popover,
 * the page is reloaded, and the answer of the attachments route is read directly.
 */
test('the phones\' default download switch on an attachment is kept', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;

  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Default Set Cave ${stamp}`,
    description: 'For the default download switch.',
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });

  await gotoRoute(page, `/caves/${cave.id}`);
  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await expect(gallery).toBeVisible({ timeout: 15_000 });
  await gallery.locator('input[type=file]').setInputFiles([
    { name: `default-set-${stamp}.png`, mimeType: 'image/png', buffer: uniquePng(6) },
  ]);
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  const flag = async () => {
    const attachments = await asPerson<{ inDeviceDefaultSet: boolean }[]>(
      page,
      'GET',
      `/api/v1/attachments?entityType=feature&entityId=${cave.id}`,
    );
    expect(attachments).toHaveLength(1);
    return attachments[0].inDeviceDefaultSet;
  };
  expect(await flag(), 'a new attachment is not in the default set').toBe(false);

  const figure = page.locator('figure').filter({ hasText: `default-set-${stamp}` });
  await figure.getByRole('button', { name: 'Details' }).click();
  const popover = page.locator('.ant-popover');
  const toggle = popover.getByRole('switch', { name: "Include in the phones' default download" });
  await expect(toggle).toHaveAttribute('aria-checked', 'false');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-checked', 'true');
  await expect.poll(flag).toBe(true);

  // Read again from nothing: the switch shows what the server holds.
  await page.reload();
  await figure.getByRole('button', { name: 'Details' }).click();
  await expect(
    page.locator('.ant-popover').getByRole('switch', { name: "Include in the phones' default download" }),
  ).toHaveAttribute('aria-checked', 'true', { timeout: 15_000 });

  // And off again.
  await page.locator('.ant-popover').getByRole('switch', { name: "Include in the phones' default download" }).click();
  await expect.poll(flag).toBe(false);
});
