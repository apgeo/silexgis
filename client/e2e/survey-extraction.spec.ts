// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

/**
 * A compiled survey no longer arrives finished: it is read into its stations and shots by a
 * background job, so the row it creates passes through a waiting state on its way to being ready.
 *
 * What this shows that no unit test can. The list is what a person watches while that happens, and
 * it polls itself — so the states have to name work that is really outstanding, the list has to
 * notice on its own when the work is done, and the plot has to stay viewable throughout, since the
 * viewer reads the uploaded file and never waited on any of this. A page that only ever saw a
 * finished row would show none of it.
 */
test('a compiled survey is read in the background and the list follows it there', async ({ page }) => {
  const caveName = `E2E Survey Cave ${Date.now()}`;
  await login(page);

  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();

  // The row exists as soon as the upload is accepted, and it says what the plot is.
  const models = page.getByRole('table').filter({ hasText: 'Survex .3d' });
  await expect(models.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  // Viewable while it is being read. The viewer parses the uploaded file itself, so nothing it
  // needs is waiting on the job — an offer that appeared only afterwards would be a regression
  // nobody watching a small fixture finish quickly would ever notice.
  await expect(models.getByRole('button', { name: 'View in 3D' })).toBeVisible();

  // And the list settles by itself: it keeps asking while there is work outstanding and stops
  // when there is not. Nothing here reloads the page.
  await expect(models.getByText('Ready')).toBeVisible({ timeout: 60_000 });
  await expect(models.getByText('Could not be processed')).toHaveCount(0);

  // The reading is what produced the cave's centerline: the survey as it was measured, recorded
  // as the cave's own shape rather than uploaded a second time by hand.
  await expect(page.getByRole('table').filter({ hasText: 'Extracted' })).toBeVisible({
    timeout: 30_000,
  });

  // ---- The same file read again, without uploading it again ----
  // Offered on a model that has been read, asked about first because it replaces what the last
  // reading produced, and answered by the server with the model still ready — it goes on holding
  // the reading it has until the next one replaces it — and said to be on its way to being read
  // again. That second half is what makes the rest mean something: a row that says "Ready"
  // afterwards was ready before too, so the status alone could not tell a reading that ran from a
  // press that did nothing.
  const asked = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/v1\/survey-models\/[0-9a-f-]+\/reading$/.test(new URL(response.url()).pathname),
  );
  await models.getByRole('button', { name: 'Read again' }).click();
  await page.locator('.ant-popconfirm:visible').getByRole('button', { name: 'OK' }).click();
  const answer = await asked;
  expect(answer.status()).toBe(200);
  const queued = (await answer.json()) as { id: string; status: string; readingAgain: boolean };
  expect(queued.status).toBe('ready');
  expect(queued.readingAgain).toBe(true);
  await expect(page.getByText('The reading is queued.', { exact: false })).toBeVisible();

  // The server finishes the second reading: only the job ending stops the model being answered
  // as read again, and it is ready at every moment on the way.
  const token = await bearerToken(page);
  await expect
    .poll(
      async () => {
        const now = (await apiJson(page, token, 'GET', `/api/v1/survey-models/${queued.id}`)) as {
          status: string;
          readingAgain: boolean;
        };
        expect(now.status).toBe('ready');
        return now.readingAgain;
      },
      { timeout: 90_000, message: 'the second reading never finished' },
    )
    .toBe(false);
  // And the list arrives there by itself, as it did the first time: no reload, nothing left
  // saying the work is outstanding, the plot viewable throughout.
  await expect(models.getByText('Being read again')).toHaveCount(0, { timeout: 30_000 });
  await expect(models.getByText('Waiting its turn')).toHaveCount(0);
  await expect(models.getByText('In progress')).toHaveCount(0);
  await expect(models.getByText('Ready')).toBeVisible();
  await expect(models.getByText('Could not be processed')).toHaveCount(0);
  await expect(models.getByRole('button', { name: 'View in 3D' })).toBeVisible();

  // A second reading replaces the first one's work rather than adding to it: the cave still has
  // one centerline drawn from this survey, not two.
  await expect(page.getByRole('row').filter({ hasText: 'Extracted' })).toHaveCount(1);
});
