// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt } from './helpers.ts';

// A flyby built by hand, in the real scene: views captured from the scene's own camera, a section
// added, moved, copied and taken out again, a change of the look written by hand — and then played,
// to see that the camera really goes where the edit says. The unit tests prove the arithmetic on a
// camera made of plain objects; only the engine itself can show that a view captured from it is a
// view the flyby comes back to.

interface Camera {
  longitude: number;
  latitude: number;
  height: number;
  heading: number;
  pitch: number;
  roll: number;
}

type SceneHandle = {
  getCamera(): Camera;
  groundHeight(longitude: number, latitude: number): number;
  setCamera(camera: Camera): void;
  whenSettled(timeoutMs?: number): Promise<boolean>;
};

/** The scene's camera, read through the handle the scene offers a driven browser. */
function camera(page: Page): Promise<Camera> {
  return page.evaluate(() => {
    const scene = (window as unknown as { __silexgisPresenter?: { scene3d?: SceneHandle } }).__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read its camera through');
    }
    const { longitude, latitude, height, heading, pitch, roll } = scene.getCamera();
    return { longitude, latitude, height, heading, pitch, roll };
  });
}

/** Puts the scene's camera somewhere, and waits for what it shows there to be drawn. */
async function moveCamera(page: Page, to: Camera) {
  await page.evaluate(async (next) => {
    const scene = (window as unknown as { __silexgisPresenter?: { scene3d?: SceneHandle } }).__silexgisPresenter!.scene3d!;
    scene.setCamera(next);
    await scene.whenSettled(60_000);
  }, to);
}

/** Whether the scene's camera is below the ground under it. */
function underground(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const scene = (window as unknown as { __silexgisPresenter?: { scene3d?: SceneHandle } }).__silexgisPresenter!.scene3d!;
    const { longitude, latitude, height } = scene.getCamera();
    return height < scene.groundHeight(longitude, latitude);
  });
}

/** Whether two cameras stand in the same place, to a few metres, facing the same way. */
function sameCamera(a: Camera, b: Camera): boolean {
  const turn = Math.abs(((a.heading - b.heading + 540) % 360) - 180);
  return (
    Math.abs(a.longitude - b.longitude) < 6e-5 &&
    Math.abs(a.latitude - b.latitude) < 6e-5 &&
    Math.abs(a.height - b.height) < 5 &&
    Math.abs(a.pitch - b.pitch) < 0.5 &&
    turn < 0.5
  );
}

const status = (page: Page) => page.getByTestId('flyby-time');

async function choose(page: Page, testId: string, option: string) {
  await page.getByTestId(testId).click();
  await page.getByTitle(option, { exact: true }).last().click();
}

async function openFlyby(page: Page) {
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
}

test('a flyby built by hand flies through the captured views, with its sections and changes as edited', async ({ page }) => {
  test.slow();
  await openFlyby(page);
  await choose(page, 'flyby-preset', 'My own views');
  await expect(page.getByTestId('flyby-views')).toBeVisible();

  // Three views: the scene as it opened, tilted, and two more from elsewhere around the cave.
  const opened = await camera(page);
  const first: Camera = { ...opened, heading: 20, pitch: -50 };
  await moveCamera(page, first);
  await page.getByTestId('flyby-view-add').click();
  const second: Camera = { ...first, longitude: first.longitude + 0.004, latitude: first.latitude - 0.002, heading: 300, pitch: -35 };
  await moveCamera(page, second);
  await page.getByTestId('flyby-view-add').click();
  const third: Camera = { ...first, longitude: first.longitude - 0.003, latitude: first.latitude - 0.003, heading: 70, pitch: -40 };
  await moveCamera(page, third);
  await page.getByTestId('flyby-view-add').click();
  await expect(page.getByTestId('flyby-view-list').getByRole('listitem')).toHaveCount(3);
  await expect(page.getByTestId('flyby-holds-place')).toBeVisible();

  // The third is flown to second, the old second taken out, and the leg into it shortened.
  await page.getByRole('button', { name: 'Move View 3 up' }).click();
  await page.getByRole('button', { name: 'Remove View 3' }).click();
  await expect(page.getByTestId('flyby-view-list').getByRole('listitem')).toHaveCount(2);
  await page.getByTestId('flyby-view-1-seconds').fill('4');
  await page.getByTestId('flyby-view-1-seconds').blur();
  // Taken again from the camera as it is — which is where the third view was taken.
  await page.getByRole('button', { name: 'Take View 2 again from the camera as it is now' }).click();

  // A section added, copied, the copy taken out, and the added one moved to the front.
  await page.getByText(/^\d+ sections?$/).click();
  await choose(page, 'flyby-section-add-kind', 'Hold');
  await page.getByTestId('flyby-section-add').click();
  await expect(page.getByText('2 sections')).toBeVisible();
  await page.getByRole('button', { name: 'Duplicate 2. Hold' }).click();
  await expect(page.getByText('3 sections')).toBeVisible();
  await page.getByRole('button', { name: 'Remove 3. Hold' }).click();
  await page.getByRole('button', { name: 'Move 2. Hold up' }).click();
  await expect(page.getByTestId('flyby-edited')).toBeVisible();
  const sections = page.getByTestId('flyby-sections').locator('.scene3d-flyby-section-name');
  await expect(sections).toHaveText([/^Hold/, /^To a view/]);
  await page.getByTestId('flyby-section-hold-2-seconds').fill('2');
  await page.getByTestId('flyby-section-hold-2-seconds').blur();
  // Added after the last leg it was about the last view; held at the first instead.
  await page.getByRole('button', { name: 'Edit what 1. Hold does' }).click();
  await choose(page, 'flyby-section-hold-2-subject', 'View 1');
  await page.getByRole('button', { name: 'Edit what 1. Hold does' }).click();
  // What a section does, opened and changed: the leg into the last view comes to rest at its end.
  await page.getByRole('button', { name: 'Edit what 2. To a view does' }).click();
  await expect(page.getByTestId('flyby-section-keyframe-1-editor')).toBeVisible();
  await page.getByTestId('flyby-section-keyframe-1-rest').check();
  await page.getByRole('button', { name: 'Edit what 2. To a view does' }).click();

  // A change of the look written by hand: the background at half strength during the leg.
  await page.getByText('0 changes').click();
  await choose(page, 'flyby-add-change-kind', 'Strength of the background');
  await choose(page, 'flyby-add-change-section', '2. To a view');
  await page.getByTestId('flyby-add-change-seconds').fill('1');
  await page.getByTestId('flyby-add-change-seconds').blur();
  await page.getByTestId('flyby-add-change-add').click();
  await expect(page.getByTestId('flyby-changes')).toContainText('Background at 50% · 2. To a view');

  // Played without loading the route first: this is about where the camera goes.
  await page.getByTestId('flyby-load-route').uncheck();
  const home = await camera(page);
  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 120_000 });
  await expect(status(page)).toHaveAttribute('data-status', 'ended', { timeout: 120_000 });
  await expect(status(page)).toContainText('0:06 / 0:06');
  // It ends where the last view was taken.
  expect(sameCamera(await camera(page), third)).toBe(true);

  // And starts — holding still, as the moved section says — where the first was taken.
  await page.getByTestId('flyby-position').getByRole('slider').focus();
  await page.keyboard.press('Home');
  await expect(status(page)).toHaveAttribute('data-status', 'paused');
  await expect.poll(async () => sameCamera(await camera(page), first)).toBe(true);
  // A second in, a tenth of a second a press: still in the hold.
  for (let press = 0; press < 10; press += 1) {
    await page.keyboard.press('ArrowRight');
  }
  await expect(status(page)).toContainText('0:01 / 0:06');
  expect(sameCamera(await camera(page), first)).toBe(true);

  await page.getByTestId('flyby-stop').click();
  await expect(status(page)).toHaveAttribute('data-status', 'idle');
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
});

test('the further ready-made flights are offered, with their options, and a descent plays down a demo cave', async ({ page }) => {
  test.slow();
  await openFlyby(page);
  await page.getByTestId('flyby-source-inView').check();
  await expect(page.getByTestId('flyby-panel').getByRole('checkbox').first()).toBeChecked();

  // A tour that stops at each cave, saying its name and size when asked.
  await expect(page.getByTestId('flyby-pause-say')).toHaveCount(0);
  await page.getByTestId('flyby-pause-seconds').fill('3');
  await page.getByTestId('flyby-pause-seconds').blur();
  await choose(page, 'flyby-pause-say', 'Its name, length and depth');

  // Opened from far away.
  await page.getByTestId('flyby-from-far').check();
  await page.getByText(/^\d+ sections?$/).click();
  await expect(page.getByTestId('flyby-sections').locator('.scene3d-flyby-section-name').first()).toHaveText(/^From far away/);
  await page.getByTestId('flyby-from-far').uncheck();

  // An imported file is a thing a flyby can be about.
  await page.getByTestId('flyby-source-file').check();
  await expect(page.getByTestId('flyby-panel')).toContainText(/No imported file is ready|The file to follow/);

  // Down the first cave in view.
  await page.getByTestId('flyby-source-inView').check();
  await choose(page, 'flyby-preset', 'Descent');
  // A flyby about a file not yet chosen had no sections to show; the list opens closed again.
  await page.getByText(/^\d+ sections?$/).click();
  await expect(page.getByTestId('flyby-sections')).toContainText('Down the survey');
  await page.getByTestId('flyby-load-route').uncheck();
  const home = await camera(page);
  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 180_000 });
  // The demo cave's survey is drawn with its depths, so there is a way down it to follow — and the
  // camera following it goes under the ground.
  await expect(page.getByTestId('flyby-line-fallback-descent')).toHaveCount(0);
  await expect.poll(() => underground(page), { timeout: 90_000, intervals: [500] }).toBe(true);
  await page.getByTestId('flyby-stop').click();
  await expect(status(page)).toHaveAttribute('data-status', 'idle');
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
});
