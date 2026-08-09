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
 * Files unique to this run — in their names as well as their bytes, and both matter.
 *
 * The bytes, because the store warns about content it already holds: a fixture with fixed
 * content would be skipped on the second run of the suite. The names, because the archive
 * keeps what earlier runs put in it: a row matched by a name two runs have used is two rows,
 * and the assertion fails for a reason that has nothing to do with the flow.
 *
 * The path a browser reports for a dropped folder is the file's name, so the marker goes on
 * the last segment and the folders keep the names the tree assertions look for.
 */
function payload(paths: string[]) {
  const run = randomUUID().slice(0, 8);

  // Folder names carry the marker too: the mirrored shelves they become outlive the run, and a
  // second "bulletins" under a different archive is a shelf the tree assertions would find
  // first and legitimately be empty.
  const mark = (segment: string) => {
    const dot = segment.lastIndexOf('.');
    return dot < 0 ? `${segment}-${run}` : `${segment.slice(0, dot)}-${run}${segment.slice(dot)}`;
  };

  const named = paths.map((path) => {
    const segments = path.split('/').map(mark);
    return { path: segments.join('/'), fileName: segments[segments.length - 1] };
  });

  return {
    files: named.map(({ path }) => ({
      name: path,
      mimeType: 'text/plain',
      buffer: Buffer.from(`${path} :: ${run}`),
    })),
    /** The names as stored, for the rows each one becomes. */
    names: named.map(({ fileName }) => fileName),
    /** The marker every name here carries, for the shelves the folders become. */
    run,
  };
}

/**
 * Picks a shelf in the tree.
 *
 * The row's own box includes the indent and the expander, so clicking the tree item can land
 * on neither — the label is what selects. Clicking a shelf that is already selected would
 * deselect it, so this is only ever used to move somewhere else.
 */
async function selectShelf(page: Page, label: string | RegExp) {
  await shelfLabel(page, label).click();
}

/**
 * A shelf's label in the tree.
 *
 * Matched on the label rather than through the tree item's accessible name: a nested node's
 * name is built from its icons alone, so a role-and-name locator finds every shelf in the tree
 * and none of them by the word a person would look for.
 */
function shelfLabel(page: Page, label: string | RegExp) {
  return page.locator('.ant-tree-title').filter({ hasText: label }).first();
}

/** Opens a shelf so the shelves under it are drawn. */
async function expandShelf(page: Page, label: string | RegExp) {
  await page
    .locator('.ant-tree-treenode')
    .filter({ has: page.locator('.ant-tree-title').filter({ hasText: label }) })
    .first()
    .locator('.ant-tree-switcher')
    .click();
}

/**
 * Creates a cabinet and leaves it selected — which is what the page itself does, so clicking
 * it afterwards would toggle the selection off again.
 */
async function createCabinet(page: Page): Promise<string> {
  const name = shelfName();

  await gotoRoute(page, '/cabinets');
  // Reaching a route by address is a full sign-in round trip, and the address matches before
  // the round trip has finished. Waiting for something only this page draws is what makes the
  // click below act on the page rather than on a redirect.
  await expect(shelfLabel(page, 'Not filed')).toBeVisible({ timeout: 45_000 });
  await page.getByRole('button', { name: /New$/ }).click();
  await page.getByLabel('Name').fill(name);
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The new shelf is open, which is what every test here goes on to upload into.
  await expect(page.getByRole('heading', { level: 4, name })).toBeVisible({ timeout: 15_000 });
  return name;
}

test('a drop of several files sends every one of them', async ({ page }) => {
  await login(page);
  await createCabinet(page);
  await page.getByRole('button', { name: /Upload$/ }).click();

  const drawer = page.getByRole('dialog');
  await expect(drawer.getByText('Before you start')).toBeVisible();

  // Set on the drop zone's own input rather than a page-wide locator: there is more than one
  // upload control in the document, and an unscoped locator has previously posted a batch to
  // the wrong endpoint entirely.
  const dropped = payload(['one.txt', 'two.txt', 'three.txt']);
  await drawer
    .getByTestId('upload-files')
    .locator('input[type=file]')
    .setInputFiles(dropped.files);

  // Three rows, all stored. The defect this pins sent one and reported success.
  await expect(drawer.getByText('3 of 3 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByTestId('upload-done').click();

  // And they are actually on the shelf, which is the only assertion the server can answer.
  for (const name of dropped.names) {
    await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible({ timeout: 15_000 });
  }
});

test('a dropped folder keeps its structure as cabinets', async ({ page }) => {
  await login(page);
  const shelf = await createCabinet(page);
  await page.getByRole('button', { name: /Upload$/ }).click();

  const drawer = page.getByRole('dialog');
  const dropped = payload(['1987/bulletins/march.txt', '1987/bulletins/april.txt']);
  await drawer
    .getByTestId('upload-files')
    .locator('input[type=file]')
    .setInputFiles(dropped.files);

  await expect(drawer.getByText('2 of 2 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByTestId('upload-done').click();

  // The folders became shelves under the one that was chosen. What this proves that no server
  // test can is that the path a browser reports for a dropped folder reached the request at
  // all: Playwright's setInputFiles carries these names as the files' own, exactly as a
  // browser does for a folder drop.
  //
  // Expanded by hand: the tree opens every node it knows about when it first draws, and these
  // shelves did not exist then.
  //
  // The nested shelf below it and what is filed on it are deliberately not asserted here.
  // Those are a walk down a tree this run shares with every earlier one, and they are already
  // covered against the server — where a shelf is named by id rather than clicked towards, and
  // the whole mirrored subtree is checked. What only a browser can answer is whether the path
  // was reported and sent, and the shelf below is the answer to that.
  await expandShelf(page, shelf);
  await expect(shelfLabel(page, `1987-${dropped.run}`)).toBeVisible({ timeout: 15_000 });
});

test('uploading the same bytes again is warned about and can be accepted', async ({
  page,
  consoleErrors,
}) => {
  // The refusal this flow exists to drive. The browser logs every 409 to the console, so the
  // one this test provokes on purpose is declared rather than left to look like a defect.
  consoleErrors.allow(
    /Failed to load resource.*409/,
    'the duplicate refusal this test asks for on purpose',
  );

  await login(page);
  await createCabinet(page);
  const { files } = payload(['survey.txt']);

  for (const attempt of [1, 2]) {
    await page.getByRole('button', { name: /Upload$/ }).click();
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

    await drawer.getByTestId('upload-done').click();
  }
});

test('what is uploaded with no destination waits in the inbox and can be filed in bulk', async ({
  page,
}) => {
  await login(page);
  const shelf = await createCabinet(page);

  await selectShelf(page, 'Not filed');
  await page.getByRole('button', { name: /Upload$/ }).click();

  const drawer = page.getByRole('dialog');
  const dropped = payload(['inbox-a.txt', 'inbox-b.txt']);
  await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(dropped.files);
  await expect(drawer.getByText('2 of 2 stored', { exact: false })).toBeVisible({ timeout: 60_000 });
  await drawer.getByTestId('upload-done').click();

  // Filing is a later decision, taken over a selection rather than one document at a time.
  const rowA = page.getByRole('row', { name: new RegExp(dropped.names[0]) });
  await expect(rowA).toBeVisible({ timeout: 15_000 });
  await rowA.getByRole('checkbox').check();
  await page.getByRole('row', { name: new RegExp(dropped.names[1]) }).getByRole('checkbox').check();

  await page.getByTestId('bulk-filing-bar').getByRole('button', { name: 'Also file in…' }).click();
  const modal = page.getByRole('dialog').filter({ hasText: 'Also file in…' });
  // Typed rather than scrolled to: the list of cabinets grows with every run of this suite and
  // the control only renders the part of it that is on screen. Scoped to the open dropdown as
  // well, because the shelf's name is also the title of its node in the tree behind the dialog.
  await modal.getByRole('combobox').fill(shelf);
  await page.locator('.ant-select-dropdown').getByTitle(shelf, { exact: true }).click();
  await modal.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByText('2 filed.')).toBeVisible({ timeout: 15_000 });

  // Filed is filed: they are on the shelf, and the inbox no longer holds them.
  const filed = new RegExp(dropped.names[0]);
  await selectShelf(page, shelf);
  await expect(page.getByRole('row', { name: filed })).toBeVisible({ timeout: 15_000 });

  await selectShelf(page, 'Not filed');
  await expect(page.getByRole('row', { name: filed })).toBeHidden({ timeout: 15_000 });
});
