// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * The selection panel as somebody arranges it.
 *
 * End to end because the arrangement is the point: it is stored per person, reloaded from that
 * store, merged with whatever the installation publishes, and none of those steps is visible to a
 * unit test of the component. What is asserted here is the behaviour the requirement asked for —
 * history closed by default, sections that hide and come back, and an arrangement that survives a
 * reload — rather than the pixels.
 */
test.describe('lateral panel', () => {
  test('history starts closed, sections hide and come back, and the arrangement is remembered', async ({
    page,
  }) => {
    const featureName = `E2E Panel ${Date.now()}`;
    await login(page);

    // Draw something to select. Same flow the surface-feature test uses, because that is the one
    // path known to put a real object under a known pixel.
    const toolbar = page.locator('.map-edit-overlay');
    await expect(toolbar).toBeVisible();
    await toolbar.getByRole('button', { name: /Feature type/ }).click();
    await page.getByRole('button', { name: 'Sinkhole / Doline' }).click();
    await toolbar.getByRole('button', { name: 'edit' }).click();

    const canvas = page.locator('.map-canvas');
    await canvas.click({ position: { x: 380, y: 300 } });

    const modal = page.getByRole('dialog');
    await expect(modal.getByText('New feature')).toBeVisible();
    await modal.getByLabel('Name').fill(featureName);
    await modal.getByRole('button', { name: 'OK' }).click();

    const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
    await toolbar.getByRole('button', { name: /Save/ }).click();
    await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
    await reloaded;

    await canvas.click({ position: { x: 380, y: 300 } });
    await expect(page.getByRole('heading', { name: featureName })).toBeVisible({ timeout: 15_000 });

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

    await page.reload();
    await canvas.click({ position: { x: 380, y: 300 } });
    await expect(page.getByRole('heading', { name: featureName })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('panel-section-history')).toHaveAttribute('data-open', 'true');
    await expect(page.getByTestId('panel-section-links')).toHaveCount(0);

    // A hidden section is offered back rather than being lost.
    await page.getByRole('button', { name: 'Links' }).first().click();
    await expect(page.getByTestId('panel-section-links')).toBeVisible();

    // Put the panel back the way it ships, so the next run starts from the default.
    await page.getByRole('button', { name: 'Restore the default arrangement' }).click();

    // Cleanup.
    await page.goto('/features');
    const row = page.getByRole('row', { name: new RegExp(featureName) });
    await expect(row).toBeVisible({ timeout: 15_000 });
    await row.getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  });
});
