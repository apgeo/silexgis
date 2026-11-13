// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt } from './helpers.ts';

// A flyby in the real scene. The unit tests fly a camera that is a plain object; here the engine
// itself is moved, frame by frame, by the tool a person presses — which is the only way to know
// that the camera really travels, that the view's own address is left alone meanwhile, and that
// the camera really is handed back.

interface Camera {
  longitude: number;
  latitude: number;
  height: number;
  heading: number;
  pitch: number;
}

/** The scene's camera, read through the handle the scene offers a driven browser. */
function camera(page: Page): Promise<Camera> {
  return page.evaluate(() => {
    const scene = (window as unknown as { __silexgisPresenter?: { scene3d?: { getCamera(): Camera } } })
      .__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read its camera through');
    }
    const { longitude, latitude, height, heading, pitch } = scene.getCamera();
    return { longitude, latitude, height, heading, pitch };
  });
}

/** Whether two cameras stand in the same place, to a metre or so, facing the same way. */
function sameCamera(a: Camera, b: Camera): boolean {
  return (
    Math.abs(a.longitude - b.longitude) < 2e-5 &&
    Math.abs(a.latitude - b.latitude) < 2e-5 &&
    Math.abs(a.height - b.height) < 2 &&
    Math.abs(a.pitch - b.pitch) < 0.5
  );
}

const status = (page: Page) => page.getByTestId('flyby-time');

async function openFlybyOverDemoCave(page: Page) {
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  // Nothing is selected, so the tool opens on what the view shows; the caves drawn in it are the
  // other thing it can be about.
  await expect(page.getByTestId('flyby-source-view')).toBeChecked();
  await page.getByTestId('flyby-source-inView').check();
  await expect(page.getByTestId('flyby-panel').getByRole('checkbox').first()).toBeChecked();
  // A quarter of a turn at the quickest rate offered: long enough to see the camera travel, short
  // enough for a scene drawn in software.
  await page.getByTestId('flyby-turns').fill('0.25');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('6');
  await page.getByTestId('flyby-seconds-per-turn').blur();
}

test('a flyby takes the camera round the cave, leaves the address alone, and hands the camera back', async ({ page }) => {
  test.slow();
  await openFlybyOverDemoCave(page);
  const home = await camera(page);
  const address = page.url();

  await page.getByTestId('flyby-play').click();
  // Getting ready first: the caves are found and the route loaded, each stop waited on.
  await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 180_000 });
  // The layers are the flyby's to show while it flies.
  await expect(page.getByTestId('scene3d-layers-trigger')).toBeDisabled();

  const early = await camera(page);
  expect(sameCamera(early, home)).toBe(false);
  await expect
    .poll(async () => sameCamera(await camera(page), early), { timeout: 30_000 })
    .toBe(false);

  // It ends by itself on its last pose, having been somewhere other than where it started.
  await expect(status(page)).toHaveAttribute('data-status', 'ended', { timeout: 120_000 });
  const last = await camera(page);
  expect(sameCamera(last, home)).toBe(false);

  // The position can be moved back, which a flyby that had clicked its way here could not be.
  await page.getByTestId('flyby-position').getByRole('slider').focus();
  await page.keyboard.press('Home');
  await expect(status(page)).toHaveAttribute('data-status', 'paused');
  await expect.poll(async () => sameCamera(await camera(page), last)).toBe(false);

  // The address still names where the viewer stood, not a moment of the flight.
  expect(page.url()).toBe(address);

  await page.getByTestId('flyby-stop').click();
  await expect(status(page)).toHaveAttribute('data-status', 'idle');
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
  await expect(page.getByTestId('scene3d-layers-trigger')).toBeEnabled();
  expect(page.url()).toBe(address);
});

test('a press on the scene takes the camera back from a flyby, and the flyby goes on when asked', async ({ page }) => {
  test.slow();
  await openFlybyOverDemoCave(page);
  // Without the route loaded first: this is about the flight, not about getting ready for it.
  await page.getByTestId('flyby-load-route').uncheck();
  await page.getByTestId('flyby-loop').click();
  await expect(page.getByTestId('flyby-loop')).toHaveAttribute('aria-pressed', 'true');

  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 120_000 });

  const box = (await page.getByTestId('scene3d-surface').boundingBox())!;
  // Well to the right of the panel and above the bar, on the scene itself.
  await page.mouse.click(box.x + box.width * 0.75, box.y + box.height * 0.35);
  await expect(status(page)).toHaveAttribute('data-status', 'paused');
  const held = await camera(page);

  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing');
  await expect.poll(async () => sameCamera(await camera(page), held), { timeout: 30_000 }).toBe(false);
  await page.getByTestId('flyby-pause').click();
  await expect(status(page)).toHaveAttribute('data-status', 'paused');
  await page.getByTestId('flyby-stop').click();
  await expect(status(page)).toHaveAttribute('data-status', 'idle');
});

test('a flyby is kept under a name, changed section by section, and played on a clean screen', async ({ page }) => {
  test.slow();
  await openFlybyOverDemoCave(page);
  await page.getByTestId('flyby-load-route').uncheck();

  // Every control of the panel does what it says.
  await page.getByTestId('flyby-direction').getByText('Counter-clockwise').click();
  await page.getByTestId('flyby-underground').check();
  await page.getByTestId('flyby-speed').getByRole('slider').focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('flyby-panel')).toContainText('Speed of the whole flyby: ×1.25');
  await page.getByText(/^\d+ sections$/).click();
  const firstSeconds = page.getByTestId('flyby-sections').getByRole('spinbutton').first();
  await firstSeconds.fill('3');
  await firstSeconds.blur();
  await expect(page.getByTestId('flyby-edited')).toBeVisible();

  await page.getByTestId('flyby-name').fill('Demo flyby');
  await page.getByTestId('flyby-save').click();
  await expect(page.getByTestId('flyby-saved')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByTestId('flyby-file-save').click();
  expect((await download).suggestedFilename()).toBe('demo-flyby.flyby.json');

  // Opened again from the kept ones, after the panel was rebuilt from scratch.
  await page.getByTestId('flyby-underground').uncheck();
  await expect(page.getByTestId('flyby-edited')).toHaveCount(0);
  await page.getByTestId('flyby-saved').click();
  await page.getByTitle('Demo flyby').click();
  await expect(page.getByTestId('flyby-edited')).toBeVisible();
  await expect(firstSeconds).toHaveValue('3');

  // The clean screen: nothing over the scene but the scene's own credits, a countdown, the flight.
  const home = await camera(page);
  await page.getByTestId('flyby-clean').click();
  await expect(page.getByTestId('flyby-clean-hint')).toBeAttached({ timeout: 120_000 });
  await expect(page.getByTestId('flyby-panel')).toHaveCount(0);
  await expect(page.getByTestId('flyby-transport')).toHaveCount(0);
  await expect(page.getByTestId('scene3d-layers-trigger')).toBeHidden();
  await expect(page.locator('.cesium-widget-credits')).toBeVisible();
  await expect(page.getByTestId('flyby-countdown')).toBeVisible({ timeout: 120_000 });
  await expect(page.getByTestId('flyby-countdown')).toHaveCount(0, { timeout: 30_000 });
  const flying = await camera(page);
  await expect.poll(async () => sameCamera(await camera(page), flying), { timeout: 30_000 }).toBe(false);

  await page.keyboard.press('Escape');
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  await expect(status(page)).toHaveAttribute('data-status', 'paused');

  // Closing the tool with a flyby held ends it, and the camera is where it was found.
  await page.getByTestId('flyby-delete').click();
  await expect(page.getByTestId('flyby-saved')).toHaveCount(0);
  await page.getByTestId('flyby-close').click();
  await expect(page.getByTestId('flyby-panel')).toHaveCount(0);
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
});
