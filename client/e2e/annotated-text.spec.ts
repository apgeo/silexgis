// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * Reading a link-annotated text, and following a passage of it into the views that are open.
 *
 * The behaviour worth an end-to-end test here is the one no unit test can reach: a passage
 * clicked in one window moving a map in another. Everything either side of that — the offsets,
 * the partition, the re-measuring — is covered where it is decided, in tests that do not need a
 * browser; what needs one is that two windows of the same application agree about which views
 * exist and that a reference published in one arrives in the other.
 *
 * The seeded demonstration text is the subject. Two of the three flows only read it. The third
 * marks a passage of it and takes that link down again at the end, so a second run finds the text
 * as the first one did — an accumulating pile of links over the same words would leave every
 * later run asserting against a document the seed never described.
 */

/**
 * The seeded demonstration text, opened the way somebody looking for it would open it: down the
 * filing tree to the shelf it is on, then the listing.
 *
 * The shelf is picked before the listing is read because the listing *is* the shelf's — a
 * cabinets page with nothing selected lists nothing, so looking for the document straight away
 * waits out its timeout against a page that was never going to show it. There is no
 * `/documents` listing route to shortcut through, and asking for one navigates the router to a
 * page that does not exist, which the console guard reports rather than ignores.
 */
async function openSeededText(page: Page): Promise<string> {
  await page.goto('/cabinets');

  // The archive first: its child shelf is not in the tree until its parent is expanded, and
  // selecting the parent is what expands it.
  const archive = page.getByRole('treeitem').filter({ hasText: 'Demo archive' }).first();
  await expect(archive).toBeVisible({ timeout: 20_000 });
  await archive.click();

  const shelf = page.getByRole('treeitem').filter({ hasText: 'Survey reports' }).first();
  await expect(shelf).toBeVisible({ timeout: 20_000 });
  await shelf.click();

  const link = page.getByRole('link', { name: /notes on the 1987 survey/i }).first();
  await expect(link).toBeVisible({ timeout: 20_000 });
  await link.click();
  await expect(page.getByTestId('tl-passage').first()).toBeVisible({ timeout: 20_000 });
  return new URL(page.url()).pathname.split('/').pop()!;
}

test.describe('link-annotated text', () => {
  test('draws the seeded passages, and says what each one links to', async ({ page }) => {
    await login(page);
    await openSeededText(page);

    const passages = page.getByTestId('tl-passage');
    await expect(passages).toHaveCount(3);
    await expect(passages.filter({ hasText: 'Dolina Demo' })).toBeVisible();
    await expect(passages.filter({ hasText: 'the 1987 survey report' })).toBeVisible();

    // The card names the relation and the target, which is what makes a highlight worth
    // hovering rather than merely coloured.
    await passages.filter({ hasText: 'Dolina Demo' }).hover();
    const card = page.locator('.ant-popover').filter({ hasText: 'Dolina Demo' }).first();
    await expect(card).toBeVisible({ timeout: 15_000 });
    // Matched on the button's text rather than its accessible name: an antd icon renders as
    // `role="img"` with a label of its own, so the name computes to "aim Show" and an exact
    // match on "Show" finds nothing. The text is what discriminates it from "Show in…" beside it.
    await expect(card.getByRole('button').filter({ hasText: /^Show$/ })).toBeVisible();
  });

  test('a passage followed in one window moves the map in another', async ({ page, context }) => {
    await login(page);
    // The map's own address carries where it is looking, so "did it move" is answerable without
    // reaching into the renderer.
    await page.goto('/');
    await expect(page.locator('canvas, .ol-viewport').first()).toBeVisible({ timeout: 30_000 });
    await page.waitForTimeout(2000);
    const before = page.url();

    const text = await context.newPage();
    const documentId = await openSeededText(text);
    await text.goto(`/panel/text?document=${documentId}`);
    await expect(text.getByTestId('tl-passage').first()).toBeVisible({ timeout: 20_000 });

    // The roster is gathered when the menu opens, and the map is in another window, so this is
    // also the assertion that the roll call crossed.
    await text.getByRole('button', { name: /Where links go|Unde duc/i }).click();
    await expect(text.getByText(/another window|altă fereastră/i).first()).toBeVisible({ timeout: 15_000 });
    await text.keyboard.press('Escape');

    await text.getByTestId('tl-passage').filter({ hasText: 'Dolina Demo' }).first().click();

    await expect
      .poll(() => page.url(), { timeout: 20_000, message: 'the map never moved' })
      .not.toBe(before);
    // Where it moved to, not merely that it moved: a map that jumped anywhere would pass the
    // weaker assertion, and the whole point is that it went to the passage's own feature.
    expect(page.url()).toMatch(/45\.53/);
    await text.close();
  });

  test('marks a new passage through the ordinary link dialog', async ({ page }) => {
    await login(page);
    await openSeededText(page);
    const before = await page.getByTestId('tl-passage').count();

    await page.getByRole('button', { name: /Edit links|Editează legăturile/i }).click();
    await expect(page.getByText(/Select any part of the text|Selectează orice parte/i)).toBeVisible();

    // Selected through the DOM rather than by dragging: a drag across wrapped prose lands on a
    // different word depending on the width the browser happened to lay it out at, and what is
    // under test is what the application does with a selection, not the browser's hit testing.
    await page.evaluate(() => {
      const root = document.querySelector('.tl-root')!;
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const at = node.textContent!.indexOf('boulder choke');
        if (at >= 0) {
          const range = document.createRange();
          range.setStart(node, at);
          range.setEnd(node, at + 'boulder choke'.length);
          const selection = getSelection()!;
          selection.removeAllRanges();
          selection.addRange(range);
          root.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
          return;
        }
      }
      throw new Error('the phrase to link is not in the seeded text');
    });

    const dialog = page.locator('.ant-modal, .ant-drawer').last();
    await expect(dialog).toBeVisible({ timeout: 15_000 });
    await dialog.getByPlaceholder(/two letters|două litere/i).fill('Falia');
    await dialog.getByText('Falia Demo').first().click();
    await dialog.getByRole('button', { name: /^OK$/ }).click();

    await expect(page.getByTestId('tl-passage')).toHaveCount(before + 1, { timeout: 20_000 });
    const marked = page.getByTestId('tl-passage').filter({ hasText: 'boulder choke' }).first();
    await expect(marked).toBeVisible();

    // Taken down through the card that offers it, which is both the cleanup and the only cover
    // the delete has.
    await marked.hover();
    const card = page.locator('.ant-popover').filter({ hasText: 'Falia Demo' }).first();
    await expect(card).toBeVisible({ timeout: 15_000 });
    await card.getByRole('button', { name: /Delete link|Șterge legătura/i }).click();
    await page.getByRole('button', { name: /^OK$/ }).last().click();

    await expect(page.getByTestId('tl-passage')).toHaveCount(before, { timeout: 20_000 });
  });
});
