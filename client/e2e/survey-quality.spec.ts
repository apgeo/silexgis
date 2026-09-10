// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * The compiler log the server tests read, not a second copy of it. A log is a long file whose
 * numbers this suite asserts on, and two copies of one drift into two different surveys the first
 * time somebody corrects one of them.
 */
const compilerLog = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  'server',
  'tests',
  'SilexGis.Api.Tests',
  'Fixtures',
  'therion-compiler.log',
);

/** The archive card, so nothing here drives the compiled-models uploader by accident. */
function archive(page: Page) {
  return page.locator('.ant-card').filter({ hasText: 'Survey sources' });
}

/**
 * The closure panel — a separate card, below the archive it reads from. By its own mark rather than
 * by its heading: the cave page also carries a history card, which quotes what was done to the cave
 * and so contains the panel's own words once a log has been archived.
 */
function closure(page: Page) {
  return page.getByTestId('survey-closure');
}

async function createCave(page: Page, name: string) {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
}

/**
 * How well a survey closed, read out of an archived compilation log and shown to the person whose
 * survey it is.
 *
 * What this shows that no server test can. A stored row proves the figures were parsed; it cannot
 * prove that anyone ever sees them, that they arrive without a reload after the background reading
 * finishes, or — the one that matters most here — that the two error measures are readable as the
 * different quantities they are. The compiler prints a relative error and an absolute error side by
 * side: one is a ratio, a percentage of the loop's own length, and the other is a distance in
 * metres. A short loop is routinely the worse ratio and the smaller distance at the same time, so a
 * reader who takes one column for the other is told the opposite of the truth about half the loops.
 * Only the rendered page can be asked whether each column says which kind of quantity it holds.
 */
test('a survey says how well it closes, in the compiler\'s own words', async ({ page }) => {
  const caveName = `E2E Survey Closure ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  // Nothing to report before a log is archived, and the panel says nothing rather than showing an
  // empty card on every cave in the registry.
  await expect(closure(page)).toHaveCount(0);

  await archive(page).locator('input[type="file"]').setInputFiles(compilerLog);
  await expect(archive(page).getByText('therion-compiler.log')).toBeVisible({ timeout: 15_000 });

  // Reading the log is background work, and the panel is expected to fill itself in when that work
  // finishes — a reader who has to reload to see the figures has not been shown them.
  const card = closure(page);
  await expect(card).toBeVisible({ timeout: 60_000 });
  await expect(card.getByText('REL-ERR').first()).toBeVisible({ timeout: 60_000 });

  // The two measures, each saying which kind of quantity it is. This is the assertion the whole
  // spec exists for: a ratio labelled as a ratio, a distance labelled as a distance.
  // .first(): a scrolling antd table renders its heading row twice, once to measure it.
  await expect(card.getByText(/^Ratio: /).first()).toBeVisible();
  await expect(card.getByText(/^Distance: /).first()).toBeVisible();
  await expect(card.getByText('ABS-ERR').first()).toBeVisible();

  // And they read as those quantities in the rows too: a percentage under the ratio, metres under
  // the distance. Read from one row, so a column of the wrong unit cannot pass by sitting elsewhere.
  const firstRow = card.locator('tbody tr.ant-table-row').first();
  await expect(firstRow.locator('td').nth(1)).toHaveText(/\d %$/);
  await expect(firstRow.locator('td').nth(2)).toHaveText(/\d m$/);

  // Traceable to the run that produced it: which compiler said so, and when this application read
  // it. A log carries no timestamp of its own, so that second date is labelled as the reading and
  // never as the compilation.
  await expect(card.getByText(/therion 5\.5\.7/)).toBeVisible();
  await expect(card.getByText('Log read here')).toBeVisible();
  await expect(card.getByText('Compiler released')).toBeVisible();
});
