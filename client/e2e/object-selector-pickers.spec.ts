// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { login, openCaveFromRegistry } from './helpers.ts';

interface QueryAnswer {
  worlds: { world: string; hits: { title: string }[] }[];
}

/**
 * A picker over one of the filter model's worlds, driven in a real browser against the real
 * answering route rather than a mocked one.
 *
 * Every picker that chooses a feature, a cave, a trip or a document is the same object selector,
 * so what is proved here once is proved for the kind of control rather than for one screen: that a
 * scope narrowed to one kind of feature is honoured by the server (a term that finds entrances
 * finds nothing here), that a row the screen withholds (the cave a closest approach is measured
 * from) is not offered, and that the whole thing — opening it included — can be worked from the
 * keyboard alone — and that a cave is found by its code as well as its name, saying so. The
 * closest-approach picker on a cave's page is the vehicle
 * because choosing in it changes nothing stored — the run leaves no trace to clear away.
 */
test('the closest-approach picker offers other caves only, and is worked from the keyboard', async ({ page }) => {
  const own = 'Peștera Demo Mare';
  await login(page);
  await openCaveFromRegistry(page, own);
  await expect(page.getByRole('heading', { name: own })).toBeVisible({ timeout: 15_000 });

  const trigger = page.getByTestId('closest-approach-other');
  await expect(trigger).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Choose a second cave to measure how close the two come to each other.')).toBeVisible();

  // Opened from the keyboard, and the keyboard lands in the search box: the panel is drawn in a
  // layer at the end of the page, so a box that had to be tabbed to would be out of reach.
  await trigger.focus();
  await page.keyboard.press('Enter');
  const box = page.getByTestId('closest-approach-other-input');
  await expect(box).toBeVisible();
  await expect(box).toBeFocused();

  // The scope is "caves". "entrance" is a term the demonstration entrances carry ("Main
  // entrance", "Upper entrance") and no cave does, so the picker's own question is captured and
  // asked again with only the kind changed: the entrances it then finds are what the scope keeps
  // out, and the picker's empty answer is the scope's doing rather than the term's.
  const asked = page.waitForRequest(
    (r) => r.url().includes('/api/v1/filters/query') && (r.postData() ?? '').includes('entrance'),
  );
  await page.keyboard.type('entrance');
  const question = (await asked).postData() ?? '';
  expect(question.split('"cave"').length - 1, 'the question names the cave kind exactly once').toBe(1);
  const asEntrances = await asPerson<QueryAnswer>(
    page,
    'POST',
    '/api/v1/filters/query',
    JSON.parse(question.replace('"cave"', '"caveEntrance"')),
  );
  const entranceTitles = asEntrances.worlds.flatMap((w) => w.hits.map((h) => h.title));
  expect(entranceTitles.some((title) => /entrance/i.test(title))).toBe(true);
  await expect(page.getByTestId('closest-approach-other-rows').getByText('Nothing matches')).toBeVisible({ timeout: 15_000 });

  // A cave is found by its registry code too, and its row says that the code is what found it —
  // the title alone would not explain why it is listed.
  await box.fill('demo-0004');
  const found = page.getByTestId('closest-approach-other-row');
  await expect(found.filter({ hasText: 'Peștera Demo Ursului' })).toHaveCount(1, { timeout: 15_000 });
  await expect(page.getByTestId('closest-approach-other-matched').first()).toHaveText('code DEMO-0004');

  await box.fill('Demo');

  // The list is fetched, so the first row is waited for. Every demonstration cave carries "Demo"
  // in its name, so the answer is not empty — and it never holds the cave this page is about.
  const rows = page.getByTestId('closest-approach-other-row');
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  await expect(rows.filter({ hasText: own })).toHaveCount(0);

  // Keyboard only: the down arrow reaches the first row from the box, Enter chooses it, and the
  // single-choice control closes and names what was chosen on its trigger.
  const firstTitle = (await rows.first().locator('.ant-typography').first().innerText()).trim();
  expect(firstTitle.length).toBeGreaterThan(0);
  await box.press('ArrowDown');
  await expect(rows.first().getByRole('button').first()).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(box).toBeHidden();
  await expect(trigger).toContainText(firstTitle);
  await expect(trigger).toBeFocused();
  await expect(page.getByText('Choose a second cave to measure how close the two come to each other.')).toBeHidden();
});
