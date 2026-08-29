// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

/** The archive's own card, so nothing here can accidentally drive the compiled-models uploader. */
function archive(page: Page) {
  return page.locator('.ant-card').filter({ hasText: 'Survey sources' });
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
 * The raw survey material a compiled export was made from, archived against its cave.
 *
 * What this shows that no server test can. The archive is a second list on a page that already has
 * one for compiled models, and the two accept different files for different reasons — so what is
 * proved here is that a person on that page can reach the archive at all, that the file they chose
 * is listed as the kind it is, and that the refusal for bytes which are not what their name claims
 * arrives as a sentence about the file rather than as a status number. A screenshot of a stored row
 * would show none of that.
 */
test('a survey source is archived beside the compiled models', async ({ page }) => {
  const caveName = `E2E Survey Source Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  const card = archive(page);
  await expect(card.getByRole('button', { name: 'Archive a source' })).toBeVisible();
  await expect(card.getByText('No sources archived yet')).toBeVisible();

  await card.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'e2e-survey.th'));

  // Listed as what it is, not as a nameless attachment: the kind is what tells a later reader
  // whether this archive holds anything a re-compilation could actually start from.
  await expect(card.getByText('e2e-survey.th')).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('Therion source')).toBeVisible();
});

test('bytes that are not the format the name claims are refused, in words', async ({
  page,
  consoleErrors,
}) => {
  // The refusal this flow exists to drive: a file the archive turns away on its content. The
  // browser logs every 400 to the console, so the one asked for on purpose is declared here
  // rather than left to read as a defect.
  consoleErrors.allow(
    /Failed to load resource.*400/,
    'the content-mismatch refusal this test asks for on purpose',
  );

  const caveName = `E2E Survey Source Refusal ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  const card = archive(page);
  // A PNG named as a Survex source. The extension is the only thing about it that is a survey,
  // which is exactly the case an allow-list of extensions alone would let through.
  await card.locator('input[type="file"]').setInputFiles({
    name: 'not-a-survey.svx',
    mimeType: 'application/octet-stream',
    buffer: Buffer.from([
      0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44,
      0x52,
    ]),
  });

  await expect(page.getByText(/not what its name says/)).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('No sources archived yet')).toBeVisible();
});
