// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { caveWithSurvey, chooseSize, coveredControls, markedSize, openModel } from './caveviewLabelSizeFlow.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

// The size control under a finger, on a phone. Its own spec because nothing about where it sits
// here is what the desktop run sees: the viewer lays its tabs along the top instead of down the
// side, in a box that takes every press along that edge; the toolbar is a shorter row of larger
// buttons underneath, reaching both sides; and the control itself is grown to a finger's size.
// Whether it can be pressed among those is a fact about measured boxes at this width.

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

test("on a phone the size of a model's station names is pressed clear of the viewer's own controls", async ({
  page,
}) => {
  test.setTimeout(180_000);
  await login(page);
  const caveId = await caveWithSurvey(page, madeCaves);
  const viewer = await openModel(page, caveId);

  // The layout this is about: a finger, and the viewer's narrow arrangement.
  expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true);
  await expect(viewer.getByTestId('caveview-container')).toHaveClass(/cv-mobile/);

  // Nothing over the control, and the control over nothing: every tab and every toolbar button is
  // still the topmost thing at its own middle.
  expect(await coveredControls(page)).toEqual([]);
  // Large enough to press.
  const box = (await viewer.getByTestId('caveview-label-scale').boundingBox())!;
  expect(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(40);

  // And pressed: the menu opens, a size is taken, and the menu then marks it.
  expect(await markedSize(page, viewer)).toBe('100%');
  await chooseSize(page, viewer, 60);
  expect(await markedSize(page, viewer)).toBe('60%');
  expect(await coveredControls(page)).toEqual([]);
});
