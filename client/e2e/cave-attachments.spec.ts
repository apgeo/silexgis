// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { uniquePng } from './png.ts';
import { login, openCaveFromRegistry } from './helpers.ts';

// The pictures and documents attached to a cave: uploaded, shown, corrected by a new version,
// described, and taken away again.
//
// These three were part of the smoke spec and are a file of their own for one reason: they work
// on the attachments of the same demonstration cave, and each begins or ends by sweeping away
// every picture named `e2e-photo` it finds there. One after another that is tidy; side by side
// one test removes the picture another has just uploaded. So they take turns whatever the rest of
// the suite does — including in the runner's fast form, which otherwise spreads the tests of a
// file over the workers.
test.describe.configure({ mode: 'default' });

test('cave photo attachment round-trip', async ({ page }) => {
  await login(page);

  // Any visible demo cave works; the gallery lives on the detail page.
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });

  // Upload a photo through the attachment drop zone (scoped to the gallery card).
  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  // Bytes unique to this run, under the fixture's own name. The archive refuses content it
  // already holds until somebody answers a dialog, and deleting a figure detaches the
  // attachment without deleting the document behind it — so fixed bytes are accepted the first
  // time this suite is ever run and refused every time after, with the refusal landing on the
  // "Saved." below.
  await gallery.locator('input[type=file]').setInputFiles([{ name: 'e2e-photo.png', mimeType: 'image/png', buffer: uniquePng(4) }]);
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The gallery renders the thumbnail through the token-authenticated URL.
  //
  // Found by its own name rather than by being the first picture in the gallery: this cave is
  // demonstration data that anybody may add a photograph to, and the first tile is then
  // somebody else's — whose thumbnail is a perfectly good one of the wrong size, so the
  // assertion below fails while saying nothing about the upload this test just made.
  const photo = page
    .locator('figure')
    .filter({ hasText: 'e2e-photo' })
    .locator('img[src*="/thumbnail"]')
    .first();
  await expect(photo).toBeVisible({ timeout: 15_000 });
  await expect(photo).toHaveJSProperty('naturalWidth', 4); // decoded, not a broken image

  // Cleanup: remove every e2e photo (earlier aborted runs may have left extras).
  const figures = page.locator('figure').filter({ hasText: 'e2e-photo' });
  for (let remaining = await figures.count(); remaining > 0; remaining--) {
    await figures.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(figures).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
  await expect(page.getByText('e2e-photo.png')).not.toBeVisible();
});

test('cave attachment file versioning', async ({ page }) => {
  await login(page);
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });

  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await deletePhotoFigures(page); // start clean
  // Bytes unique to this run, under the fixture's own name. The archive refuses content it
  // already holds until somebody answers a dialog, and deleting a figure detaches the
  // attachment without deleting the document behind it — so fixed bytes are accepted the first
  // time this suite is ever run and refused every time after, with the refusal landing on the
  // "Saved." below.
  await gallery.locator('input[type=file]').setInputFiles([{ name: 'e2e-photo.png', mimeType: 'image/png', buffer: uniquePng(4) }]);
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  const figure = page.locator('figure').filter({ hasText: 'e2e-photo' }).first();
  await expect(figure).toBeVisible({ timeout: 15_000 });

  // Open the versions popover and upload a corrected version onto the head.
  await figure.getByRole('button', { name: 'history' }).click();
  const popover = page.locator('.ant-popover');
  await expect(popover.getByText('Upload new version')).toBeVisible();
  // Unique again, and doubly so here: a corrected version that is byte-identical to what it
  // corrects is not a second version of anything.
  await popover.locator('input[type=file]').setInputFiles([{ name: 'e2e-photo.png', mimeType: 'image/png', buffer: uniquePng(4) }]);

  // The chain now has two versions: the head (current) and the superseded v1.
  await expect(popover.getByText('current')).toBeVisible({ timeout: 15_000 });
  await expect(popover.getByText('v1')).toBeVisible();

  // Cleanup (the attachment still points at one document — deleting the figure detaches it).
  await page.keyboard.press('Escape');
  await deletePhotoFigures(page);
});

test('cave attachment details: caption, document date and tags persist', async ({ page }) => {
  await login(page);
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });

  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await deletePhotoFigures(page); // start clean
  // Bytes unique to this run, under the fixture's own name. The archive refuses content it
  // already holds until somebody answers a dialog, and deleting a figure detaches the
  // attachment without deleting the document behind it — so fixed bytes are accepted the first
  // time this suite is ever run and refused every time after, with the refusal landing on the
  // "Saved." below.
  await gallery.locator('input[type=file]').setInputFiles([{ name: 'e2e-photo.png', mimeType: 'image/png', buffer: uniquePng(4) }]);
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  const figure = page.locator('figure').filter({ hasText: 'e2e-photo' }).first();
  await expect(figure).toBeVisible({ timeout: 15_000 });

  // Open the details editor and set caption, the document's own date, and a tag. The
  // caption keeps the file name in it on purpose: a figure is labelled by its caption once
  // it has one, so a caption without it would make this figure — and the cleanup sweep at
  // the end — unable to find the very photo they just set up.
  await figure.getByRole('button', { name: 'Details' }).click();
  const popover = page.locator('.ant-popover');
  await popover.locator('input').first().fill('e2e-photo winter caption');
  await popover.getByPlaceholder('Select date').fill('2019-08-01');
  await page.keyboard.press('Enter');
  await popover.getByText('Add tag').click();
  await popover.getByRole('combobox').last().fill('e2e-detail-tag');
  await page.keyboard.press('Enter');
  // Let the earlier toasts retire first: a second "Saved." raised while one is still
  // fading matches twice, which fails the assertion below on ambiguity rather than on
  // anything having gone wrong.
  await expect(page.getByText('Saved.')).toHaveCount(0, { timeout: 10_000 });
  await popover.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Reopen: caption, date and tag round-tripped through the server.
  await figure.getByRole('button', { name: 'Details' }).click();
  await expect(popover.locator('input').first()).toHaveValue('e2e-photo winter caption');
  await expect(popover.getByPlaceholder('Select date')).toHaveValue('2019-08-01');
  await expect(popover.getByText('e2e-detail-tag')).toBeVisible();

  await page.keyboard.press('Escape');
  await deletePhotoFigures(page);
});

async function deletePhotoFigures(page: Page) {
  const figures = page.locator('figure').filter({ hasText: 'e2e-photo' });
  for (let remaining = await figures.count(); remaining > 0; remaining--) {
    await figures.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(figures).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
}
