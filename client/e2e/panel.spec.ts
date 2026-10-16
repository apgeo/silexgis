// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { registerAccount } from './arrange.ts';
import { login, registryRow } from './helpers.ts';

/**
 * The selection panel as somebody arranges it.
 *
 * End to end because the arrangement is the point: it is stored with the person's account,
 * reloaded from there, merged with whatever the installation publishes, and none of those steps
 * is visible to a unit test of the component. What is asserted here is the behaviour the
 * requirement asked for — history closed by default, sections that hide and come back, and an
 * arrangement that survives a reload — rather than the pixels.
 *
 * As an account of its own: the arrangement follows the account, so changing the administrator's
 * would change it for every other spec signing in as the administrator while this one runs.
 */
test.describe('lateral panel', () => {
  test('history starts closed, sections hide and come back, and the arrangement is remembered', async ({
    page,
  }) => {
    const member = await registerAccount(page.request, 'panel');
    await login(page, member.email, member.password);

    // Something to select: a demonstration feature everybody may read, picked from the registry
    // with its "show on map", which selects what it frames.
    const select = async () => {
      const row = await registryRow(page, 'Dolina Demo');
      await expect(row).toBeVisible({ timeout: 15_000 });
      await row.getByRole('button', { name: 'aim' }).click();
      await expect(page.getByRole('heading', { name: 'Dolina Demo' })).toBeVisible({ timeout: 15_000 });
    };
    await select();

    // History starts closed — the explicit ask. Its body is absent from the document rather than
    // merely hidden, which is what makes a closed section cost no request at all.
    const history = page.getByTestId('panel-section-history');
    await expect(history).toHaveAttribute('data-open', 'false');
    await expect(history.locator('#panel-section-body-history')).toHaveCount(0);

    // Details is open, so the panel is useful before anybody arranges anything.
    await expect(page.getByTestId('panel-section-details')).toHaveAttribute('data-open', 'true');

    // Open history, hide the links section, and reload: both choices survive, because the
    // arrangement is stored rather than held in the page.
    await history.getByRole('button', { expanded: false }).click();
    await expect(history).toHaveAttribute('data-open', 'true');

    const links = page.getByTestId('panel-section-links');
    await links.getByRole('button', { name: /Hide Links/ }).click();
    await expect(page.getByTestId('panel-section-links')).toHaveCount(0);

    // The selection is in the address, so the reload comes back with the feature selected.
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Dolina Demo' })).toBeVisible({ timeout: 45_000 });
    await expect(page.getByTestId('panel-section-history')).toHaveAttribute('data-open', 'true');
    await expect(page.getByTestId('panel-section-links')).toHaveCount(0);

    // A hidden section is offered back rather than being lost.
    await page.getByRole('button', { name: 'Links' }).first().click();
    await expect(page.getByTestId('panel-section-links')).toBeVisible();

    // Put the panel back the way it ships.
    await page.getByRole('button', { name: 'Restore the default arrangement' }).click();
  });
});
