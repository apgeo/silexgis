// SPDX-License-Identifier: AGPL-3.0-or-later
import { randomUUID } from 'node:crypto';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Dropping a batch of files into the archive, driven end to end in a real browser.
 *
 * <p>
 * Three things here can only be caught in a browser, and each has bitten this project before:
 * a multi-file drop that sends one file because the control announced them one at a time;
 * a folder whose structure is thrown away because the browser's relative path never reached
 * the request; and a duplicate warning that cannot be answered because the dialog it needs is
 * never shown.
 * </p>
 * <p>
 * Every file is given content unique to the run. The store warns about content it already
 * holds, so a fixture with fixed bytes would be skipped on the second run of the suite and the
 * assertions would pass or fail by how recently somebody had run it.
 * </p>
 */
test.describe.configure({ mode: 'serial', timeout: 120_000 });

/** A cabinet name nothing else in the archive uses, so the tree assertions are unambiguous. */
const shelfName = () => `E2E archive ${randomUUID().slice(0, 8)}`;

/**
 * Files whose bytes are unique to this run, named by the folder path a browser would report
 * for a dropped folder.
 */
function payload(paths: string[]) {
  const run = randomUUID();
  return paths.map((path) => ({
    name: path,
    mimeType: 'text/plain',
    buffer: Buffer.from(`${path} :: ${run}`),
  }));
}

/** Creates a cabinet from the page's own control and returns its name. */
async function createCabinet(page: Page): Promise<string> {
  const name = shelfName();

  await gotoRoute(page, '/cabinets');
  await page.getByRole('button', { name: 'New', exact: true }).click();
  await page.getByLabel('Name').fill(name);
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  return name;
}

test('a drop of several files sends every one of them', async ({ page }) => {
  await login(page);
  const shelf = await createCabinet(page);

  await page.getByRole('treeitem', { name: new RegExp(shelf) }).click();
  await page.getByRole('button', { name: 'Upload', exact: true }).click();

  const drawer = page.getByRole('dialog');
  await expect(drawer.getByText('Before you start')).toBeVisible();

  // Set on the drop zone's own input rather than a page-wide locator: there is more than one
  // upload control in the document, and an unscoped locator has previously posted a batch to
  // the wrong endpoint entirely.
  await drawer
    .getByTestId('upload-files')
    .locator('input[type=file]')
    .setInputFiles(payload(['one.txt', 'two.txt', 'three.txt']));

  // Three rows, all stored. The defect this pins sent one and reported success.
  await expect(drawer.getByText('3 of 3 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByRole('button', { name: 'Close' }).click();

  // And they are actually on the shelf, which is the only assertion the server can answer.
  await expect(page.getByRole('row', { name: /one\.txt/ })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('row', { name: /two\.txt/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /three\.txt/ })).toBeVisible();
});

test('a dropped folder keeps its structure as cabinets', async ({ page }) => {
  await login(page);
  const shelf = await createCabinet(page);

  await page.getByRole('treeitem', { name: new RegExp(shelf) }).click();
  await page.getByRole('button', { name: 'Upload', exact: true }).click();

  const drawer = page.getByRole('dialog');
  await drawer
    .getByTestId('upload-files')
    .locator('input[type=file]')
    .setInputFiles(payload(['1987/bulletins/march.txt', '1987/bulletins/april.txt']));

  await expect(drawer.getByText('2 of 2 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByRole('button', { name: 'Close' }).click();

  // The folders became shelves under the one that was chosen. Playwright's setInputFiles
  // reports these names as the files' own, which is exactly what a browser does for a folder
  // drop — so this is the real path, not a simulation of one.
  await expect(page.getByRole('treeitem', { name: /1987/ })).toBeVisible({ timeout: 15_000 });
  await page.getByRole('treeitem', { name: /bulletins/ }).click();
  await expect(page.getByRole('row', { name: /march\.txt/ })).toBeVisible({ timeout: 15_000 });
});

test('uploading the same bytes again is warned about and can be accepted', async ({ page }) => {
  await login(page);
  const shelf = await createCabinet(page);
  const files = payload(['survey.txt']);

  await page.getByRole('treeitem', { name: new RegExp(shelf) }).click();

  for (const attempt of [1, 2]) {
    await page.getByRole('button', { name: 'Upload', exact: true }).click();
    const drawer = page.getByRole('dialog').last();
    await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(files);

    if (attempt === 1) {
      await expect(drawer.getByText('1 of 1 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
    } else {
      // The second attempt is refused until somebody answers, which is what "a warning
      // first" means when it is written down as a flow rather than a sentence.
      const warning = page.getByRole('dialog').filter({ hasText: 'This file is already here' });
      await expect(warning).toBeVisible({ timeout: 60_000 });
      await warning.getByRole('button', { name: 'Store anyway' }).click();
      await expect(drawer.getByText('1 of 1 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
    }

    await drawer.getByRole('button', { name: 'Close' }).click();
  }
});

test('what is uploaded with no destination waits in the inbox and can be filed in bulk', async ({
  page,
}) => {
  await login(page);
  const shelf = await createCabinet(page);

  await gotoRoute(page, '/cabinets');
  await page.getByRole('treeitem', { name: 'Not filed' }).click();
  await page.getByRole('button', { name: 'Upload', exact: true }).click();

  const drawer = page.getByRole('dialog');
  const files = payload(['inbox-a.txt', 'inbox-b.txt']);
  await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(files);
  await expect(drawer.getByText('2 of 2 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByRole('button', { name: 'Close' }).click();

  // Filing is a later decision, taken over a selection rather than one document at a time.
  const rowA = page.getByRole('row', { name: /inbox-a\.txt/ });
  await expect(rowA).toBeVisible({ timeout: 15_000 });
  await rowA.getByRole('checkbox').check();
  await page.getByRole('row', { name: /inbox-b\.txt/ }).getByRole('checkbox').check();

  await page.getByTestId('bulk-filing-bar').getByRole('button', { name: 'Also file in…' }).click();
  const modal = page.getByRole('dialog').filter({ hasText: 'Also file in…' });
  await modal.getByRole('combobox').click();
  await page.getByTitle(shelf, { exact: true }).click();
  await modal.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByText('2 filed.')).toBeVisible({ timeout: 15_000 });

  // Filed is filed: they are on the shelf, and the inbox no longer holds them.
  await page.getByRole('treeitem', { name: new RegExp(shelf) }).click();
  await expect(page.getByRole('row', { name: /inbox-a\.txt/ })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('treeitem', { name: 'Not filed' }).click();
  await expect(page.getByRole('row', { name: /inbox-a\.txt/ })).toBeHidden({ timeout: 15_000 });
});
