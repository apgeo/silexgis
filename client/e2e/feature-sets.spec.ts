// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, paletteEntry } from './helpers.ts';

/**
 * A feature set's members are named with what they are, the same word whether the member was
 * saved before or picked a moment ago: the feature type for a feature that has one, the kind for a
 * cave. The two halves come from different places — the member list the server answers, and the
 * search row the pick came from — which is exactly how they once disagreed.
 */
test('a feature set member is named by its type, picked or saved', async ({ page }) => {
  const setName = `E2E Set ${Date.now()}`;
  await login(page);
  await gotoRoute(page, '/admin/feature-sets');

  await page.getByRole('button', { name: 'New feature set' }).click();
  const create = page.getByRole('dialog');
  await create.getByLabel('Name').fill(setName);
  await create.getByRole('button', { name: 'OK' }).click();
  const row = page.getByRole('row').filter({ hasText: setName });
  await expect(row).toBeVisible({ timeout: 15_000 });

  await row.getByRole('button', { name: 'Edit features' }).click();
  const drawer = page.locator('.ant-drawer').filter({ hasText: setName });
  await addMember(page, 'Dolina Demo');
  await addMember(page, 'Peștera Demo Mare');
  await expect(memberTag(page, 'Dolina Demo')).toHaveText('Sinkhole / Doline');
  await expect(memberTag(page, 'Peștera Demo Mare')).toHaveText('Cave');
  await drawer.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Opened again, the members come from the saved list and carry the same words.
  await row.getByRole('button', { name: 'Edit features' }).click();
  await expect(memberTag(page, 'Dolina Demo')).toHaveText('Sinkhole / Doline', { timeout: 15_000 });
  await expect(memberTag(page, 'Peștera Demo Mare')).toHaveText('Cave');

  // Cleanup: an empty set with no rules deletes.
  await page.locator('.ant-drawer').getByRole('button', { name: 'Cancel' }).click();
  await row.locator('button.ant-btn-dangerous').click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'OK' }).click();
  await expect(row).toHaveCount(0, { timeout: 15_000 });
});

/**
 * A kind that only makes sense inside something else refuses to be saved without a parent. The
 * form's rule follows the chosen type; this shows it holds in a real browser and not only in a
 * unit test, where the form could be submitted before it had heard which type was chosen.
 */
test('a feature whose type needs a parent is not submitted without one', async ({ page }) => {
  await login(page);
  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();

  await toolbar.getByRole('button', { name: /Feature type/ }).click();
  await paletteEntry(page, 'Stalactite / Speleothem').click();
  await page.locator('.map-canvas').click({ position: { x: 420, y: 260 } });

  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible({ timeout: 15_000 });
  await modal.getByRole('button', { name: 'OK' }).click();

  await expect(modal.locator('.ant-form-item-explain-error')).toBeVisible();
  await expect(modal.getByText('New feature')).toBeVisible();

  await modal.getByRole('button', { name: 'Cancel' }).click();
});

async function addMember(page: Page, name: string) {
  await page.getByTestId('feature-set-member-picker-input').fill(name);
  await page.getByTestId('feature-set-member-picker-row').filter({ hasText: name }).first().click();
  await page.locator('.ant-drawer').getByRole('button', { name: 'Add' }).click();
}

function memberTag(page: Page, name: string) {
  return page.locator('.ant-drawer').getByRole('listitem').filter({ hasText: name }).locator('.ant-tag');
}
