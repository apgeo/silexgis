// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { chooseOption, confirmOk, gotoRoute, login, mapAddress, registryRow } from './helpers.ts';

// The three places a person meets geology: the area page's split by rock class, the map's style
// for a geological unit, and the import's step that turns a file's rock names into classes.
//
// What this file shows that no other test can. The server's tests prove the split is computed
// over the right ground and the matcher resolves the right names; the component tests prove each
// piece draws what it is given. Neither shows that the pieces are wired: that an area page asks
// for the split and writes the answer, that the map style reaches the units the server sent and
// the key names the classes it actually drew, and that the preview of a real upload carries the
// file's rock names to the card. The demonstration dataset has a limestone and a sandstone unit
// over its plateau, so the first two read them; the third uploads an outline file made here.

const PLATEAU = { lat: 45.525, lon: 25.44, zoom: 11 };

test('an area says how much of it is each rock, and which of it can hold a cave', async ({ page }) => {
  await login(page);

  const row = await registryRow(page, 'Platoul Demo');
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const section = page.getByTestId('area-lithology');
  await expect(section).toBeVisible({ timeout: 30_000 });

  // The demonstration plateau has two mapped classes, so the "nothing mapped" answer is gone and
  // each class has a row — the karstifiable one carrying the tag that says so.
  await expect(section.getByTestId('area-lithology-none')).toHaveCount(0);
  await expect(section.getByRole('row').filter({ hasText: 'Limestone' })).toBeVisible();
  await expect(section.getByRole('row').filter({ hasText: 'Limestone' }).getByText('karstifiable')).toBeVisible();
  const sandstone = section.getByRole('row').filter({ hasText: 'Sandstone' });
  await expect(sandstone).toBeVisible();
  await expect(sandstone.getByText('karstifiable')).toHaveCount(0);

  // The density over the karstifiable ground is a figure, not the "not measured" of an area with
  // no outcrop of it, and the remainder of the outline is reported as ground nobody mapped.
  await expect(section.getByTestId('area-lithology-karst-density')).not.toHaveText('not measured');
  await expect(section.getByText('Ground with no unit mapped (km²)')).toBeVisible();
});

test('the map draws geological units by their rock and keys the classes it drew', async ({ page }) => {
  test.setTimeout(180_000);
  await login(page);

  const unitsAnswered = page.waitForResponse(
    (response) => response.url().includes('/api/v1/map/features') && response.status() === 200,
    { timeout: 60_000 },
  );
  await gotoRoute(page, mapAddress(PLATEAU));
  await unitsAnswered;

  // The key reads the loaded units, so it lists a class once a unit of it has arrived — and
  // lists the two the demonstration plateau holds, each in the standing its style gave it.
  const key = page.getByTestId('lithology-legend');
  await expect(key).toBeVisible({ timeout: 60_000 });
  await expect(key.locator('li[data-standing="karstifiable"]')).toContainText('Limestone');
  await expect(key.locator('li[data-standing="nonKarstifiable"]')).toContainText('Sandstone');
});

test('an import of an outline file asks which class each rock name becomes', async ({ page }) => {
  test.setTimeout(180_000);
  const stamp = Date.now();
  const unrecognised = `Zona-${stamp}`;
  const polygon = (west: number, rock: string) => ({
    type: 'Feature',
    properties: { rock },
    geometry: {
      type: 'Polygon',
      coordinates: [
        [
          [west, 45.7],
          [west + 0.01, 45.7],
          [west + 0.01, 45.71],
          [west, 45.71],
          [west, 45.7],
        ],
      ],
    },
  });
  // One name every installation recognises (the Romanian word for limestone), one nobody could.
  const file = JSON.stringify({
    type: 'FeatureCollection',
    features: [polygon(25.6, 'Calcar'), polygon(25.62, unrecognised)],
  });

  await login(page);
  await page.goto('/geodata');
  await page.setInputFiles('input[type="file"]', {
    name: `e2e-geology-${stamp}.geojson`,
    mimeType: 'application/geo+json',
    buffer: Buffer.from(file, 'utf8'),
  });

  const upload = page.getByRole('row', { name: new RegExp(`e2e-geology-${stamp}`) });
  await expect(upload).toBeVisible({ timeout: 30_000 });
  await expect(upload.getByText('Imported')).toBeVisible({ timeout: 60_000 });
  await upload.getByRole('button', { name: 'Review and import into the registry' }).click();
  await page.waitForURL(/\/geodata\/[0-9a-f-]+\/import$/, { timeout: 30_000 });

  // The card lists each distinct rock name with how it was matched: the Romanian word on its
  // own, the other left for the reviewer, and the card says how many are still unanswered.
  const card = page.getByTestId('import-lithology-mapping');
  await expect(card).toBeVisible({ timeout: 30_000 });
  await expect(card.getByText('Rock names matching no class: 1.')).toBeVisible();
  const recognised = card.getByRole('row').filter({ hasText: 'Calcar' });
  await expect(recognised.getByText('Recognised')).toBeVisible();
  const open = card.getByRole('row').filter({ hasText: unrecognised });
  await expect(open.getByText('No match')).toBeVisible();

  // Answering it by hand moves it to "your choice" and leaves nothing unanswered.
  await chooseOption(page, open.locator('.ant-select'), 'Sandstone');
  await expect(open.getByText('Your choice')).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('Every rock name in the file matches a class.')).toBeVisible();

  // Cleanup: nothing was confirmed, so only the upload itself is left behind.
  await page.goto('/geodata');
  const leftover = page.getByRole('row', { name: new RegExp(`e2e-geology-${stamp}`) });
  await expect(leftover.first()).toBeVisible({ timeout: 20_000 });
  for (let remaining = await leftover.count(); remaining > 0; remaining--) {
    await leftover.first().getByRole('button', { name: 'delete' }).click();
    await confirmOk(page);
    await expect(leftover).toHaveCount(remaining - 1, { timeout: 20_000 });
  }
});
