// SPDX-License-Identifier: AGPL-3.0-or-later
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { uploaderSeesEachDecision } from './reviewUploaderFlow.ts';

/**
 * The reviewer's side of the same walk on a phone: someone deciding an upload from a cave
 * entrance or a car park. The queue, the batch with its lines and the controls over them are
 * drawn for a finger, and the page must not grow wider than the screen.
 */
test('on a phone, a reviewer turns one upload down and accepts the next whole', async ({ page, browser }) => {
  test.setTimeout(300_000);
  await uploaderSeesEachDecision(page, browser, { phone: true });
});
