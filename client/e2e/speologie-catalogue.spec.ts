// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * Reaching the Romanian cave catalogue from the rail, and being told plainly that this
 * installation cannot use it.
 *
 * A development installation has no API key for speologie.org and must not have one: the key is
 * personal to whoever obtained it, and a browser test that carried one would send real traffic to
 * a small volunteer-run service every time the suite ran. So what is driven here is the state the
 * catalogue screens are in for most installations — offered, reachable, and honest about being
 * switched off — which is also the state that is easiest to ship broken, because nobody
 * developing the feature ever sees it.
 *
 * Everything the screens do once a key is present is decided on the server and covered where it
 * is decided: the search, the merge of diacritic spellings, the conversion of the catalogue's
 * markup and the import itself all have tests that need no browser. What needs one is that the
 * route resolves, the rail offers it, the words exist in both languages, and nothing on the way
 * writes to the console.
 */

test('the cave catalogue is offered in the rail and says when no key is configured', async ({ page }) => {
  await login(page);

  await page.goto('/catalogue/speologie');

  await expect(page.getByRole('heading', { name: 'Cave catalogue' })).toBeVisible();

  // The whole point of the screen in this state: a sentence naming what is missing and who can
  // supply it, rather than a search box that fails on the first press.
  const notice = page.getByTestId('speologie-not-configured');
  await expect(notice).toBeVisible();
  await expect(notice).toContainText('API key');

  // The button is there and refuses to be pressed, rather than being absent — a missing control
  // reads as a missing feature, and this feature is present and unconfigured.
  await expect(page.getByTestId('speologie-search')).toBeDisabled();

  // The link out is the one thing that still works without a key, because it is just a link.
  await expect(page.getByRole('link', { name: 'Open speologie.org' })).toHaveAttribute(
    'href',
    /speologie\.org/,
  );
});

test('the import screen asks for nothing when no caves were chosen', async ({ page }) => {
  await login(page);

  // Reached directly, the way a stale bookmark or a hand-edited address reaches it. It must say
  // what to do rather than sit empty or ask the catalogue for nothing.
  await page.goto('/catalogue/speologie/import');

  await expect(page.getByText('Nothing was chosen')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Back to the search' })).toBeVisible();
});
