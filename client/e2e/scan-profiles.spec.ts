// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync, writeFileSync } from 'node:fs';
import { expect } from '@playwright/test';
import { asPerson, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

// Processing profiles in Settings › Scans: HDMapping's own parameter file read in, what the
// import made of it shown key by key before anything is kept, kept as one's own profile, and
// written out again as HDMapping's file. An import is checked against the defaults the installed
// HDMapping announced; the browser lane runs no worker, so its runner puts an invented set of
// defaults where a worker leaves them (client/e2e/fixtures/e2e-hdmapping-odometry-defaults.json),
// and the keys below are that fixture's.

/** A hand-written parameter file in HDMapping's layout: two known settings changed, one it does not have. */
const TUNED = `[version_info]
software_version = "9.9.9"
config_version = "1.0"

[paths]
current_output_dir = "/home/somebody/run"

[odometry_control]
iterations = 12
resolution = 0.5
no_such_key = 3
`;

const madeProfiles: string[] = [];

test.afterEach(async ({ page }) => {
  for (const id of madeProfiles.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/scan-profiles/${id}`);
  }
});

test('a TOML parameter file is imported with its report shown, kept as a profile and exported again', async ({
  page,
}) => {
  test.setTimeout(120_000);
  await login(page);

  // The lane's API has to have been started with the invented defaults; without them every
  // import is refused as "nothing to check against", which is a different test from this one.
  const schema = await asPerson<{ source: string }>(page, 'GET', '/api/v1/scan-profiles/schemas/hdmapping.odometry');
  expect(schema.source, 'the API was started without the invented HDMapping defaults; start it through scripts/e2e.mjs').not.toBe(
    'registry',
  );

  const name = `E2E tuned odometry ${Date.now()}`;
  const file = test.info().outputPath('tuned.toml');
  writeFileSync(file, TUNED);

  await gotoRoute(page, '/settings/scan-profiles');
  await expect(page.getByTestId('profile-scope-installation')).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('profile-import').click();
  // The upload passes its test id to its own file input, so the id names the input itself.
  await page.getByTestId('profile-import-file').setInputFiles(file);

  // The dry run's report: two settings taken, one the installed version does not have, and the
  // sections a profile never holds dropped — nothing kept yet.
  const report = page.getByTestId('profile-import-report');
  await expect(report).toBeVisible({ timeout: 15_000 });
  await expect(report).toContainText('The file can be kept as a profile.');
  await expect(page.getByTestId('profile-import-taken')).toHaveText('2');
  await expect(page.getByTestId('profile-import-unknown')).toContainText('odometry_control.no_such_key');
  await expect(report).toContainText('paths');
  await expect(page.getByTestId('profile-import-version')).toHaveCount(0);

  await page.getByTestId('profile-import-name').fill(name);
  await page.getByTestId('profile-import-keep').click();
  const row = page.getByTestId('profile-scope-personal').getByTestId('profile-row').filter({ hasText: name });
  await expect(row).toBeVisible({ timeout: 15_000 });
  const id = await row.getAttribute('data-profile-id');
  expect(id).toBeTruthy();
  madeProfiles.push(id!);
  // Where it came from is said on the list.
  await expect(row).toContainText('tuned.toml');

  // Written out again as HDMapping's own file: the kept settings, without the sections it dropped.
  const downloading = page.waitForEvent('download');
  await row.getByTestId('profile-export').click();
  await page.getByRole('menuitem', { name: 'HDMapping parameter file (TOML)' }).click();
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/\.toml$/);
  const text = readFileSync(await download.path(), 'utf8');
  expect(text).toContain('iterations = 12');
  expect(text).toContain('resolution = 0.5');
  expect(text).not.toContain('[paths]');
  expect(text).not.toContain('no_such_key');
});
