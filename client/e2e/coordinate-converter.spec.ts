// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, tryAsPerson } from './arrange.ts';
import { gotoRoute, login, confirmOk } from './helpers.ts';

/** Picks the option of a select whose text is as given, from the keyboard — see `chooseOption`. */
async function choose(page: Page, select: Locator, text: string) {
  await select.click();
  const option = page
    .locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`) });
  await expect(option).toBeVisible({ timeout: 15_000 });
  for (let step = 0; step < 20; step++) {
    if (await option.evaluate((el) => el.classList.contains('ant-select-item-option-active'))) {
      break;
    }
    await page.keyboard.press('ArrowDown');
  }
  await expect(option).toHaveClass(/ant-select-item-option-active/);
  await page.keyboard.press('Enter');
}

const FORMAT_IDS = ['dd', 'ddm', 'dms', 'utm', 'mgrs', 'stereo70', 'lv95', 'lv03', 'pluscode'];

/**
 * The converter, end to end: a pasted DMS position written in every format with the member's own
 * settings, one of them copied, three lines converted at once and downloaded as CSV. A registered
 * account of its own, because it changes the account's coordinate settings and nothing set here
 * may reach another spec.
 */
test('a position pasted in one format is shown, copied and downloaded in every format', async ({ page, context }) => {
  const member = await registerAccount(page.request, 'converter');
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await login(page, member.email, member.password);

  // The member's own setting reaches the converter: UTM written to ten metres.
  await gotoRoute(page, '/settings/coordinates');
  const written = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await choose(page, page.getByRole('combobox', { name: 'UTM, with zone — Precision' }), '10 m');
  expect((await written).ok()).toBe(true);
  await expect(page.getByTestId('coordinate-preview-utm')).toHaveText(/^35T \d{5}0 E \d{6}0 N$/);

  await gotoRoute(page, '/tools/coordinates');
  await page.getByTestId('converter-input').fill('45°35′58″N 24°44′10″E');
  await expect(page.getByTestId('converter-feedback')).toContainText('Read as Degrees, minutes, seconds');
  for (const id of FORMAT_IDS) {
    await expect(page.getByTestId(`converter-result-${id}`)).toBeVisible();
  }
  await expect(page.getByTestId('converter-result-dd')).toHaveText('45.59944°N 24.73611°E');
  await expect(page.getByTestId('converter-result-stereo70')).toHaveText(/^E \d{6} N \d{6}$/);
  await expect(page.getByTestId('converter-result-lv95')).toHaveText('Not available here');
  const utm = page.getByTestId('converter-result-utm');
  await expect(utm).toHaveText(/^35T \d{5}0 E \d{6}0 N$/);

  // Copy the UTM reference: what lands on the clipboard is exactly what is shown.
  await utm.locator('.ant-typography-copy').click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(await utm.innerText());

  // A temporary marker on a map of its own; nothing is saved by looking.
  await page.getByRole('button', { name: 'Show on a map' }).click();
  await expect(page.getByTestId('converter-map').locator('.ol-viewport')).toBeVisible();

  // Three lines in three formats, converted at once and downloaded with every format per line.
  await page
    .getByTestId('converter-batch-input')
    .fill(['45°35′58″N 24°44′10″E', '35T 401234 5049876', 'E 535046 N 448044'].join('\n'));
  await page.getByTestId('converter-batch-convert').click();
  await expect(page.getByTestId('converter-batch-summary')).toHaveText('Lines read: 3; not converted: 0.');
  const downloading = page.waitForEvent('download');
  await page.getByTestId('converter-batch-download').click();
  const download = await downloading;
  expect(download.suggestedFilename()).toBe('coordinates.csv');
  const lines = (await readFile(await download.path(), 'utf8')).replace(/^﻿/, '').trimEnd().split('\r\n');
  expect(lines).toHaveLength(4);
  expect(lines[0]).toContain('UTM, with zone');
  expect(lines[0]).toContain('Stereo 70 (Romania)');
  expect(lines[3]).toMatch(/^3,E 535046 N 448044,45\.53127\d*,25\.44721\d*,/);
});

/**
 * A Stereo 70 reference typed into the entrance editor is stored as the WGS84 point it names: the
 * typed text only fills the longitude and latitude, which are what is saved. The administrator,
 * because adding an entrance needs the right to write a cave; the cave is made for the test and
 * removed after it.
 */
test('a Stereo 70 reference typed into the entrance editor stores its WGS84 point', async ({ page }) => {
  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Converter Cave ${Date.now()}`,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  try {
    await gotoRoute(page, `/caves/${cave.id}`);
    await page.getByRole('button', { name: /Add entrance/ }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.locator('.ol-viewport')).toBeVisible();

    // Published reference: PROJ puts 25.44721°E 45.53127°N at E 535045.778 N 448043.847.
    await dialog.getByTestId('position-text-input').fill('E 535046 N 448044');
    await expect(dialog.getByTestId('position-text-feedback')).toContainText('Read as Stereo 70 (Romania)');
    await dialog.getByRole('button', { name: 'Use', exact: true }).click();
    await expect(dialog.getByLabel('Longitude')).toHaveValue(/^25\.4472/);
    await confirmOk(page, { exact: true });
    await expect(dialog).toBeHidden({ timeout: 15_000 });

    const entrances = await asPerson<{ geom: { coordinates: [number, number] } }[]>(
      page,
      'GET',
      `/api/v1/caves/${cave.id}/entrances`,
    );
    expect(entrances).toHaveLength(1);
    const [lon, lat] = entrances[0].geom.coordinates;
    // Within about a metre of the reference point: the typed value is rounded to the metre.
    expect(Math.abs(lon - 25.44721)).toBeLessThan(1.5e-5);
    expect(Math.abs(lat - 45.53127)).toBeLessThan(1e-5);
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${cave.id}`);
  }
});

/**
 * The order two bare numbers are read in is the member's own: chosen in Settings, kept with the
 * account, and followed by the converter, which says which order it read. A registered account of
 * its own, so the choice reaches no other spec.
 */
test('a pair pasted after choosing longitude first is read longitude first', async ({ page }) => {
  const member = await registerAccount(page.request, 'reading-order');
  await login(page, member.email, member.password);

  await gotoRoute(page, '/settings/coordinates');
  const written = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await choose(
    page,
    page.getByRole('combobox', { name: 'Two bare numbers' }),
    'Longitude first, then latitude (25.44, 45.53)',
  );
  expect((await written).ok()).toBe(true);

  await gotoRoute(page, '/tools/coordinates');
  await page.getByTestId('converter-input').fill('25.44, 45.53');
  await expect(page.getByTestId('converter-feedback')).toContainText(
    'Two bare numbers were read as longitude, then latitude.',
  );
  await expect(page.getByTestId('converter-result-dd')).toHaveText('45.53000°N 25.44000°E');
});
