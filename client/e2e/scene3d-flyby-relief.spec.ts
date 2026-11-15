// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt } from './helpers.ts';

// A flyby that stretches the relief, in the real scene.
//
// The unit tests put a flyby's relief on a scene made of plain objects. Only the real view can show
// that the factor the flyby ramps reaches the engine while the flyby flies, that the picture says
// so in its corner, and that stopping puts back the factor the viewer keeps — not true height, and
// not the flyby's — while the viewer's own setting in the layer panel is never touched.

type WithHandle = { __silexgisPresenter?: { scene3d?: { getVerticalExaggeration(): number } } };

/** The factor the engine draws the scene at, and whether the flyby is playing at that moment. */
const reading = (page: Page) =>
  page.evaluate(() => {
    const scene = (window as unknown as WithHandle).__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read it through');
    }
    const status = document.querySelector<HTMLElement>('[data-testid="flyby-time"]')?.dataset.status;
    return { factor: scene.getVerticalExaggeration(), status };
  });

const factor = async (page: Page) => (await reading(page)).factor;

/** Picks an option of a drop-down list by its title, then closes it. */
async function pickFrom(page: Page, select: string, title: string) {
  await page.getByTestId(select).click();
  const list = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)');
  await list.getByTitle(title, { exact: true }).click();
  await expect(list).toHaveCount(0);
}

test('a flyby grows the relief while it flies, says so over the picture, and gives back the viewer\'s own', async ({ page }) => {
  test.slow();
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);

  // The viewer keeps the relief at ×1.5: what a stopped flyby has to give back.
  await page.getByTestId('scene3d-layers-trigger').click();
  await page.getByRole('slider', { name: 'Vertical exaggeration' }).focus();
  for (let step = 0; step < 5; step += 1) {
    await page.keyboard.press('ArrowRight');
  }
  await expect(page.getByTestId('scene3d-relief-value')).toHaveText('×1.5');
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect.poll(() => factor(page)).toBe(1.5);

  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  // Around what the view shows, one turn of 24 s after the approach: long enough to be read
  // mid-rise on a machine that draws a few frames a second.
  await page.getByTestId('flyby-load-route').uncheck();
  await page.getByTestId('flyby-turns').fill('1');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('24');
  await page.getByTestId('flyby-seconds-per-turn').blur();

  // Opens at ×2 of its own, rises to ×3 during the circle and falls back to ×2.
  await page.getByText('Look at the start').click();
  await page.getByTestId('flyby-look-relief-on').check();
  await expect(page.getByRole('slider', { name: 'Vertical exaggeration, times true height' })).toHaveAttribute('aria-valuenow', '2');
  await page.getByText('Ready-made changes').click();
  await page.getByTestId('flyby-recipe-relief-factor').fill('3');
  await page.getByTestId('flyby-recipe-relief-factor').blur();
  await pickFrom(page, 'flyby-recipe-relief-section', '2. Circle');
  await pickFrom(page, 'flyby-recipe-relief-section', 'In every circle');
  await page.getByTestId('flyby-recipe-relief-add').click();
  await page.getByText('2 changes').click();
  await expect(page.getByTestId('flyby-changes')).toContainText('Relief to ×3');
  await expect(page.getByTestId('flyby-changes')).toContainText('Relief back to how the flyby opened');
  // The mark in the corner is on unless switched off.
  await page.getByText('Texts over the picture').click();
  await expect(page.getByTestId('flyby-text-relief')).toBeChecked();

  await page.getByTestId('flyby-play').click();
  await expect(page.getByTestId('flyby-time')).toHaveAttribute('data-status', 'playing', { timeout: 180_000 });
  // Mid-flight: the engine draws the flyby's factor, above its opening ×2 on the way to ×3, and
  // the picture says the relief is stretched.
  await expect
    .poll(
      async () => {
        const now = await reading(page);
        return now.status === 'playing' && now.factor > 2.3 && now.factor <= 3;
      },
      { message: 'the relief never rose while the flyby played', timeout: 120_000, intervals: [250] },
    )
    .toBe(true);
  await expect(page.getByTestId('flyby-texts')).toHaveAttribute('data-shown', /relief/);
  await expect(page.getByTestId('flyby-time')).toHaveAttribute('data-status', 'ended', { timeout: 180_000 });
  // At the end it has fallen back to the flyby's own opening factor.
  expect(await factor(page)).toBeCloseTo(2, 6);

  // Dragged back to the start: the opening factor again, worked out from the moment alone.
  await page.getByTestId('flyby-position').getByRole('slider').focus();
  await page.keyboard.press('Home');
  await expect(page.getByTestId('flyby-time')).toHaveAttribute('data-status', 'paused');
  await expect.poll(() => factor(page)).toBeCloseTo(2, 6);

  // Stopped: the viewer's own ×1.5 again, and their setting never moved.
  await page.getByTestId('flyby-stop').click();
  await expect(page.getByTestId('flyby-time')).toHaveAttribute('data-status', 'idle');
  await expect.poll(() => factor(page)).toBe(1.5);
  await expect(page.getByTestId('flyby-texts')).toHaveAttribute('data-shown', '');
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect(page.getByTestId('scene3d-relief-value')).toHaveText('×1.5');
});
