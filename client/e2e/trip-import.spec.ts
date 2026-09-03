// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * A club's spreadsheet of past trips becomes trips in the archive, through one review screen
 * that says what confirming would do before anything is written.
 *
 * The flow is an end-to-end test because it is the only place four things can be shown to
 * agree: what the reader made of the sheet, what the review screen warns about, what the
 * confirmation actually wrote, and what undoing it takes back. Each of the four is a different
 * piece of code and any pair of them can agree while the whole is wrong.
 *
 * The load-bearing assertion is the warning. A sheet naming people who cannot be created reads
 * exactly like a sheet that can be imported whole — the names are simply left off the trips
 * they belong to — so the screen must say so, and it must say so before the confirm button and
 * not in the result afterwards. That is asserted here on the real path rather than on a
 * fixture, because the figure it renders is the server's and a screen can be correct about a
 * number the server never sends.
 */
test.describe('trip import', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('a spreadsheet is reviewed, warned about, confirmed and undone', async ({ page }) => {
    // Unique per run: the dev database persists between runs, so fixed names would accumulate
    // and every later assertion would be about an unknown number of earlier runs' rows.
    const stamp = Date.now();
    const firstTrip = `Tura de iarna ${stamp}`;
    const secondTrip = `Tura de vara ${stamp}`;
    // Three shapes of name, chosen so the sheet is one a reviewer must not be told is fine:
    // one an import may invent a person from, and two it may not — a bare initial and a lone
    // given name, neither of which could ever be confidently joined to the right caver later.
    const creatable = `Vasile Nou${stamp}`;
    const initialled = `Ionel${stamp} A.`;
    const mononym = `Gheorghita${stamp}`;

    const sheet = [
      'Nr crt.,Data inceput,Titlu,Masiv/zona,Pesteri,Participanti,Tip',
      `1,17/04/2024,${firstTrip},Masivul Inventat ${stamp},Pestera Inventata ${stamp},`
        + `"${creatable}; ${initialled}",explorare${stamp}`,
      `2,23/05/2024,${secondTrip},Masivul Inventat ${stamp},,"${creatable}; ${mononym}",explorare${stamp}`,
      '',
    ].join('\r\n');

    await page.goto('/trip-logs/import');
    await page.setInputFiles('input[type="file"]', {
      name: `e2e-trips-${stamp}.csv`,
      mimeType: 'text/csv',
      buffer: Buffer.from(sheet, 'utf8'),
    });

    // The upload answers with a file id and the review is about that file, so the address
    // changes and everything below is about a sheet the server has already read.
    await page.waitForURL(/\/trip-logs\/import\/[0-9a-f-]{36}$/, { timeout: 30_000 });

    // The reader found the two rows under the Romanian headers without being told which column
    // is which, and it read the titles rather than the row numbers.
    await expect(page.getByTestId('trip-import-rows')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(firstTrip, { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(secondTrip, { exact: true })).toBeVisible();
    await expect(page.getByTestId('trip-import-counts')).toContainText('2 readable');

    // The judged assertion, and it is made before the confirm button is pressed: two of the
    // three names cannot become cavers, so the screen says so where somebody about to confirm
    // is looking, and it names them rather than only counting them.
    const warning = page.getByTestId('trip-import-people-warning');
    await expect(warning).toBeVisible();
    await expect(warning).toContainText('2 cannot be created');
    await expect(warning).toContainText(initialled);
    await expect(warning).toContainText(mononym);

    // The other half of the same claim, and the half a count alone cannot make: the third name
    // is not in that list. It missed the roster exactly as the other two did, so on the setting
    // this screen opens in it will not be created either — but it is a name a person can be
    // made from, and one switch makes them. Listing it beside the two no switch can help would
    // put a name in a red list under a heading whose number excludes it, which is the same
    // class of lie the count itself exists to remove.
    await expect(page.getByTestId('trip-import-people-uncreatable')).not.toContainText(creatable);

    // Nothing is written yet. What confirming would add is a count, and it starts at nothing
    // because no switch has been asked for — a figure rendered at zero rather than absent.
    const newCavers = page.getByTestId('trip-import-summary').locator('.ant-statistic', {
      hasText: 'New cavers',
    });
    await expect(newCavers).toContainText('0');

    // The section holding them is closed until it is asked for, so a choice about what gets
    // written is never one stray click away from being made.
    await page.getByText('What may be created', { exact: true }).click();

    // Change one option and the answer changes: asking for the cavers the roster lacks makes
    // one of the three names — the only one a person may be invented from — a person the
    // confirmation would add. The other two do not move, which is the whole point of the
    // warning above still standing.
    await page.getByTestId('trip-import-create-cavers').click();
    await expect(newCavers).toContainText('1', { timeout: 30_000 });
    await expect(warning).toContainText('2 cannot be created');

    await page.getByTestId('trip-import-commit').click();
    await expect(page.getByText('The import is done')).toBeVisible({ timeout: 60_000 });
    await expect(page.getByTestId('trip-import-result')).toContainText('Trips created');
    await expect(page.getByTestId('trip-import-result-failures')).toHaveCount(0);
    await page.getByRole('button', { name: 'See the batch' }).click();

    // The archive has them, under the titles the sheet wrote.
    await page.goto('/trip-logs');
    await page.getByPlaceholder('Search by title…').fill(String(stamp));
    await expect(page.getByRole('row', { name: new RegExp(firstTrip) })).toBeVisible({
      timeout: 20_000,
    });
    await expect(page.getByRole('row', { name: new RegExp(secondTrip) })).toBeVisible();

    // The confirmation is listed as a batch, named for where it came from rather than for a
    // file the batch never had — and undoing it takes back the trips as one unit.
    await page.goto('/geodata?tab=batches');
    const batch = page
      .getByTestId('import-batches')
      .getByRole('row', { name: /From a trip spreadsheet/ })
      .first();
    await expect(batch).toBeVisible({ timeout: 20_000 });

    // What the batch says it created, line by line. This drawer was written for batches of caves
    // read off a map file and branched on the feature alone, so every trip in a trip batch fell
    // to "Nothing created" — a column of lines saying nothing was made, directly under a header
    // row saying two were. Asserted before the undo, because a reverted trip keeps its title
    // here and loses the link to itself.
    await batch.getByText(/From a trip spreadsheet/).click();
    const detail = page.getByTestId('import-batch-detail');
    await expect(detail).toBeVisible({ timeout: 20_000 });
    await expect(detail).toContainText(firstTrip);
    await expect(detail).toContainText(secondTrip);
    await expect(detail).not.toContainText('Nothing created');
    await page.keyboard.press('Escape');

    await batch.getByRole('button', { name: 'Undo' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('The import was undone.')).toBeVisible({ timeout: 20_000 });

    await page.goto('/trip-logs');
    await page.getByPlaceholder('Search by title…').fill(String(stamp));
    await expect(page.getByRole('row', { name: new RegExp(firstTrip) })).toHaveCount(0, {
      timeout: 20_000,
    });
  });
});
