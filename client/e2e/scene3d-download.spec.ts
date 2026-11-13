// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login, waitForScene3dReady } from './helpers.ts';

// The 3D view's engine is the largest download of the application and is fetched only when a 3D
// view opens, so it is the one a slow or dropped connection loses. These drop it on purpose, in a
// real browser — the only place where asking for a module again after it failed can be shown to
// fetch it again rather than repeat the failure.

/** The engine module, as the development server and a built installation each name it. */
const ENGINE = /\/scene3dContext[^/]*\.(ts|js)(\?.*)?$/;

/** Drops the next `count` downloads of the engine (every one, without a count); answers how many were asked for. */
async function dropEngineDownloads(page: Page, count = Number.POSITIVE_INFINITY) {
  const asked = { times: 0 };
  await page.route(ENGINE, (route) => {
    asked.times += 1;
    return asked.times <= count ? route.abort('connectionreset') : route.fallback();
  });
  return asked;
}

const DROPPED = [
  /Failed to load resource: net::ERR_CONNECTION_RESET/,
  'the engine download is dropped on purpose, as a slow or broken connection drops it',
] as const;

test('a dropped download of the 3D view is asked for again, and the view starts without a word', async ({
  page,
  consoleErrors,
}) => {
  consoleErrors.allow(...DROPPED);
  await login(page);
  const asked = await dropEngineDownloads(page, 1);

  await page.goto('/map3d');
  await waitForScene3dReady(page, 60_000);

  expect(asked.times).toBeGreaterThanOrEqual(2);
  await expect(page.getByTestId('scene3d-download-failed')).toHaveCount(0);
});

test('a download that never arrives is said to be a download, and Try again starts the view', async ({
  page,
  consoleErrors,
}) => {
  consoleErrors.allow(...DROPPED);
  await login(page);
  const asked = await dropEngineDownloads(page);

  await page.goto('/map3d');
  const notice = page.getByTestId('scene3d-download-failed');
  // Three attempts, a few seconds apart, before the person is asked.
  await expect(notice).toBeVisible({ timeout: 30_000 });
  expect(asked.times).toBe(3);
  await expect(notice).toContainText('The 3D view did not finish downloading');
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);
  await expect(notice.getByRole('button', { name: 'Reload the page' })).toBeVisible();

  // The connection is back.
  await page.unroute(ENGINE);
  await page.getByTestId('scene3d-download-retry').click();
  await waitForScene3dReady(page, 60_000);
  await expect(notice).toHaveCount(0);
});
