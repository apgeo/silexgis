// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * The two human surfaces a printed cave code has: the page a visitor with no account reaches by
 * pointing a phone at a label, and the dialog where somebody decides whether that page says
 * anything.
 *
 * The landing page is driven as a real browser navigation rather than through the API, because
 * the failure this guards against is not a wrong answer — it is a camera app handing the address
 * to a browser and the browser showing a caver a page of JSON. That only shows up when the address
 * is actually opened.
 */
test.describe('printed cave codes', () => {
  test('a code nobody issued lands on a page, not on a payload', async ({ page, consoleErrors }) => {
    // Declared rather than left to look like a defect: the 404 IS the subject of this test. A
    // code nobody issued must answer exactly as an unpublished or revoked one does, so the
    // failing fetch is the behaviour being proved, not a symptom of a broken page.
    consoleErrors.allow(
      /Failed to load resource.*404/,
      'the not-found answer this test navigates to on purpose',
    );
    // Signed out on purpose: this is the whole point of the route.
    await page.goto('/q/definitely-not-a-real-code-000');

    await expect(page.getByText('Nothing to show for this code')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('qr-landing-code')).toHaveText('definitely-not-a-real-code-000');

    // Nothing on it invites or requires an account, and nothing on it is a cave.
    await expect(page).toHaveURL(/\/q\/definitely-not-a-real-code-000$/);
  });

  test('a malformed code answers exactly as an unknown one does', async ({ page, consoleErrors }) => {
    consoleErrors.allow(
      /Failed to load resource.*404/,
      'the not-found answer this test navigates to on purpose',
    );
    // Over the longest code the device can store — a different fact about the world, and
    // deliberately the same page, because telling them apart tells somebody which codes exist.
    await page.goto(`/q/${'z'.repeat(120)}`);

    await expect(page.getByText('Nothing to show for this code')).toBeVisible({ timeout: 20_000 });
  });

  test('a cave is published and withdrawn from its own page', async ({ page }) => {
    await login(page);

    // A cave of this run's own. The claim below is about every cave there is — one nobody has
    // decided about is not published — and only a cave made a moment ago is certainly one nobody
    // has decided about: a shared demo cave carries whatever an earlier run, stopped between its
    // publish and its withdrawal, last left on it. Unprotected, as a new cave is unless somebody
    // classes it: publishing must be provable on the caves whose coordinates are withheld from
    // nobody, since those are the ones a leaking implementation would expose at full precision.
    const caveName = `E2E Printed Codes Cave ${Date.now()}`;
    await page.goto('/caves/new');
    await page.getByLabel('Name', { exact: true }).fill(caveName);
    await page.getByLabel('Type', { exact: true }).click();
    await page.locator('.ant-select-item-option').first().click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('button', { name: 'Printed codes' })).toBeVisible({ timeout: 20_000 });

    await page.getByRole('button', { name: 'Printed codes' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText('What publishing does')).toBeVisible();

    // Every cave that has ever existed starts here: an absent decision is "not published".
    await expect(dialog.getByTestId('qr-state-unpublished')).toBeVisible();

    await dialog.getByRole('button', { name: 'Publish' }).click();
    await expect(dialog.getByTestId('qr-state-published')).toBeVisible({ timeout: 15_000 });

    // The square is not offered here, and the dialog says why rather than showing nothing: the
    // codes the caving app prints live on the places inside a cave, not on the cave itself.
    await expect(dialog.getByText('This cave carries no printed code of its own', { exact: false }))
      .toBeVisible();
    await expect(dialog.getByTestId('qr-code-square')).toHaveCount(0);

    await dialog.getByRole('button', { name: 'Stop publishing' }).click();
    // Exact: the cave page behind the dialog offers "Look this cave up in Grottocenter", whose name
    // holds "ok" too.
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(dialog.getByTestId('qr-state-unpublished')).toBeVisible({ timeout: 15_000 });

    // Clean up: the cave this run made.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await page.locator('button', { hasText: 'Delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await page.waitForURL(/\/caves$/);
  });
});
