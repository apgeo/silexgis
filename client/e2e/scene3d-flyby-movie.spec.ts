// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt } from './helpers.ts';

// A flyby written to a movie file by the scene itself.
//
// The recorder's own suite drives a stage that is a plain object, and the engine's suite a stand-in
// for the 3D library. Neither can say whether the real library draws a frame of the size it was
// asked for, at a resolution of its own, with something in it — a drawing surface read at the
// wrong moment answers with a blank picture and no error. So the session is driven here in the
// real scene first, and then a movie is made through the dialog a person uses and its file is read
// back.

/**
 * The scene's own modules, as the development server serves them to the page. The page's copy is
 * the one the view is using: one scene per window, held through the module that made it.
 */
const SCENE_MODULES = { context: '/src/scene3d/scene3dContext.ts', surface: '/src/scene3d/sceneSurface.ts' };

/** What one frame taken through the scene's capture session held. */
interface Taken {
  width: number;
  height: number;
  /** Share of the frame's pixels that are not fully transparent. */
  opaque: number;
  /** Spread of the pixels' brightness: a frame of one flat colour has none. */
  spread: number;
  /** The frame as a PNG, for a person to look at. */
  png: string;
}

/**
 * Boxes the scene's element at `css` pixels and takes one frame of `frame` pixels from it through
 * the engine's capture session, read in the task it was drawn in, then hands the surface back.
 */
function takeFrame(
  page: Page,
  css: { width: number; height: number },
  frame: { width: number; height: number },
): Promise<Taken & { after: { width: number; height: number } }> {
  return page.evaluate(
    async ({ css, frame, modules }) => {
      const { acquireScene3D } = (await import(/* @vite-ignore */ modules.context)) as typeof import('../src/scene3d/scene3dContext.ts');
      const { sceneSurfaceElement } = (await import(/* @vite-ignore */ modules.surface)) as typeof import('../src/scene3d/sceneSurface.ts');
      const slot = document.querySelector<HTMLElement>('[data-testid="scene3d-container"]')!;
      slot.style.width = `${css.width}px`;
      slot.style.height = `${css.height}px`;
      const hold = acquireScene3D(sceneSurfaceElement());
      const { engine } = hold;
      try {
        // Two frames of the browser for the layout and the engine's own loop to see the new size.
        await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        engine.beginCapture(frame);
        let taken: Omit<Taken, 'png'> & { copy: HTMLCanvasElement };
        try {
          const surface = engine.captureFrame();
          const copy = document.createElement('canvas');
          copy.width = surface.width;
          copy.height = surface.height;
          const context = copy.getContext('2d', { willReadFrequently: true })!;
          context.drawImage(surface, 0, 0);
          const pixels = context.getImageData(0, 0, copy.width, copy.height).data;
          let opaque = 0;
          let sum = 0;
          let squares = 0;
          const count = pixels.length / 4;
          for (let at = 0; at < pixels.length; at += 4) {
            if (pixels[at + 3] > 0) opaque += 1;
            const light = (pixels[at] + pixels[at + 1] + pixels[at + 2]) / 3;
            sum += light;
            squares += light * light;
          }
          const mean = sum / count;
          taken = {
            width: surface.width,
            height: surface.height,
            opaque: opaque / count,
            spread: Math.sqrt(Math.max(0, squares / count - mean * mean)),
            copy,
          };
        } finally {
          engine.endCapture();
        }
        const canvas = (document.querySelector('[data-testid="scene3d-surface"] canvas') as HTMLCanvasElement);
        const after = { width: canvas.width, height: canvas.height };
        const { copy, ...rest } = taken;
        return { ...rest, png: copy.toDataURL('image/png'), after };
      } finally {
        hold.release();
        slot.style.width = '';
        slot.style.height = '';
      }
    },
    { css, frame, modules: SCENE_MODULES },
  );
}

/** Keeps a picture where a person can look at it, when the run was asked to keep them. */
async function keep(name: string, png: string) {
  const directory = process.env.SILEXGIS_FLYBY_MOVIE_SHOTS;
  if (directory) {
    await writeFile(`${directory}/${name}.png`, Buffer.from(png.replace(/^data:image\/png;base64,/, ''), 'base64'));
  }
}

test('the scene draws frames of the size asked for, at the element\'s own size and at twice it, and hands the surface back', async ({ page }) => {
  test.slow();
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await expect
    .poll(() => page.evaluate(() => (window as unknown as { __silexgisPresenter?: { scene3d?: unknown } }).__silexgisPresenter?.scene3d !== undefined))
    .toBe(true);

  const once = await takeFrame(page, { width: 600, height: 400 }, { width: 600, height: 400 });
  await keep('capture-1x', once.png);
  expect([once.width, once.height]).toEqual([600, 400]);
  expect(once.opaque).toBeGreaterThan(0.99);
  expect(once.spread).toBeGreaterThan(4);
  // Handed back at the element's own size.
  expect([once.after.width, once.after.height]).toEqual([600, 400]);

  const twice = await takeFrame(page, { width: 600, height: 400 }, { width: 1200, height: 800 });
  await keep('capture-2x', twice.png);
  expect([twice.width, twice.height]).toEqual([1200, 800]);
  expect(twice.opaque).toBeGreaterThan(0.99);
  expect(twice.spread).toBeGreaterThan(4);
  expect([twice.after.width, twice.after.height]).toEqual([600, 400]);

  // A shape that is not the element's is refused, and nothing is left changed.
  const refused = await page.evaluate(async (modules) => {
    const { acquireScene3D } = (await import(/* @vite-ignore */ modules.context)) as typeof import('../src/scene3d/scene3dContext.ts');
    const { sceneSurfaceElement } = (await import(/* @vite-ignore */ modules.surface)) as typeof import('../src/scene3d/sceneSurface.ts');
    const hold = acquireScene3D(sceneSurfaceElement());
    try {
      hold.engine.beginCapture({ width: 1080, height: 1920 });
      hold.engine.endCapture();
      return 'taken';
    } catch (error) {
      return error instanceof Error ? error.message : String(error);
    } finally {
      hold.release();
    }
  }, SCENE_MODULES);
  expect(refused).toMatch(/not the shape/);
});

// ---- a movie made through the panel a person uses ----

interface Camera {
  longitude: number;
  latitude: number;
  height: number;
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
    const { longitude, latitude, height, pitch } = scene.getCamera();
    return { longitude, latitude, height, pitch };
  });
}

function sameCamera(a: Camera, b: Camera): boolean {
  return (
    Math.abs(a.longitude - b.longitude) < 2e-5 &&
    Math.abs(a.latitude - b.latitude) < 2e-5 &&
    Math.abs(a.height - b.height) < 2 &&
    Math.abs(a.pitch - b.pitch) < 0.5
  );
}

/** The flyby tool over the demo data's caves: one quick turn round what the view shows, with nothing loaded first. */
async function openFlyby(page: Page) {
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  await page.getByTestId('flyby-turns').fill('1');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('6');
  await page.getByTestId('flyby-seconds-per-turn').blur();
  await page.getByTestId('flyby-load-route').uncheck();
}

/** Picks an option of one of the panel's drop-downs by its whole text. */
async function choose(page: Page, panel: Locator, testId: string, text: string) {
  await panel.getByTestId(testId).click();
  const dropdown = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').last();
  await expect(dropdown).toBeVisible();
  // A click made while the list is still sliding open is lost on a machine that draws in software.
  await expect(dropdown).not.toHaveClass(/ant-slide-(up|down)-(appear|enter)/, { timeout: 15_000 });
  await dropdown.locator('.ant-select-item-option-content').filter({ hasText: new RegExp(`^${text}$`) }).first().click();
  await expect(panel.getByTestId(testId)).toContainText(text);
}

/** Types a number into one of the panel's boxes and leaves it, which is when it is taken. */
async function typeNumber(page: Page, box: Locator, value: number) {
  await box.focus();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.type(String(value));
  await page.keyboard.press('Tab');
  await expect(box).toHaveAttribute('aria-valuenow', String(value));
}

/** The shape the scene's element has on the page, as width over height. */
async function sceneShape(page: Page): Promise<number> {
  const box = (await page.getByTestId('scene3d-container').boundingBox())!;
  return box.width / box.height;
}

/** How many frames a WebM holds: its blocks, counted in the file's own structure. */
function webmFrames(bytes: Buffer): number {
  const SEGMENT = 0x18538067;
  const CLUSTER = 0x1f43b675;
  const SIMPLE_BLOCK = 0xa3;
  const vint = (at: number, keepMarker: boolean) => {
    const first = bytes[at];
    let length = 1;
    while (length <= 8 && !(first & (0x80 >> (length - 1)))) length += 1;
    let value = keepMarker ? first : first & (0xff >> length);
    for (let index = 1; index < length; index += 1) value = value * 256 + bytes[at + index];
    return { value, length };
  };
  let blocks = 0;
  const walk = (from: number, to: number) => {
    let at = from;
    while (at < to) {
      const id = vint(at, true);
      const size = vint(at + id.length, false);
      const body = at + id.length + size.length;
      if (id.value === SEGMENT || id.value === CLUSTER) {
        walk(body, Math.min(to, body + size.value));
      } else if (id.value === SIMPLE_BLOCK) {
        blocks += 1;
      }
      at = body + size.value;
    }
  };
  walk(0, bytes.length);
  return blocks;
}

/** What a browser makes of a WebM: its length and its frame's size. */
function playedAs(page: Page, bytes: Buffer) {
  return page.evaluate(async (base64: string) => {
    const data = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([data], { type: 'video/webm' }));
    const video = document.createElement('video');
    video.muted = true;
    video.preload = 'auto';
    try {
      await new Promise<void>((resolve, reject) => {
        video.onloadedmetadata = () => resolve();
        video.onerror = () => reject(new Error(`the video did not load: ${video.error?.message ?? 'no detail'}`));
        setTimeout(() => reject(new Error('the video never said how long it is')), 30_000);
        video.src = url;
      });
      return { duration: video.duration, width: video.videoWidth, height: video.videoHeight };
    } finally {
      URL.revokeObjectURL(url);
    }
  }, bytes.toString('base64'));
}

test('a short draft of a flyby is written to a WebM file, frame by frame, at the size and length asked for', async ({ page }) => {
  test.setTimeout(600_000);
  await openFlyby(page);
  const home = await camera(page);
  await page.getByTestId('flyby-movie').click();
  const panel = page.getByTestId('flyby-movie-dialog');
  await expect(panel).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('flyby-panel')).toHaveCount(0);
  await expect(panel.getByTestId('flyby-movie-privacy')).toHaveText(
    'The movie shows where these caves are. Share it only with people who may see that.',
  );

  // The ready-made files set the file and nothing else; the chat one is a GIF every browser writes.
  await panel.getByTestId('flyby-movie-preset-chat').click();
  await expect(panel.getByTestId('flyby-movie-format-gif')).toBeChecked();
  await expect(panel.getByTestId('flyby-movie-size')).toContainText('480 × 270');
  await panel.getByTestId('flyby-movie-preset-hd').click();
  await expect(panel.getByTestId('flyby-movie-size')).toContainText('1280 × 720');

  // The scene is boxed to the frame's shape beside the panel: what is seen is what the file holds.
  await expect(page.locator('.scene3d-wrap')).toHaveClass(/scene3d-wrap-framed/);
  await expect.poll(() => sceneShape(page)).toBeCloseTo(16 / 9, 1);
  await choose(page, panel, 'flyby-movie-size', '720 × 720');
  await expect.poll(() => sceneShape(page)).toBeCloseTo(1, 1);
  await choose(page, panel, 'flyby-movie-size', '640 × 360');
  await expect.poll(() => sceneShape(page)).toBeCloseTo(16 / 9, 1);

  // A WebM, which every Chromium writes.
  await panel.locator('label').filter({ has: page.getByTestId('flyby-movie-format-webm') }).click();
  await expect(panel.getByTestId('flyby-movie-format-webm')).toBeChecked();
  await choose(page, panel, 'flyby-movie-fps', '24');
  await panel.locator('label').filter({ hasText: 'Medium' }).click();
  await panel.locator('label').filter({ hasText: 'Fine, larger than the screen' }).click();
  await expect(panel.getByTestId('flyby-movie-level-fineLarge')).toBeChecked();
  await panel.getByTestId('flyby-movie-level-quick').click();
  await expect(panel.getByTestId('flyby-movie-level-quick')).toBeChecked();
  // A draft: half the size, eight frames a second, taken quickly.
  await panel.getByTestId('flyby-movie-draft').check();
  // The first three seconds alone, and the last of them held for half a second.
  await panel.getByText('A stretch of it').click();
  await typeNumber(page, panel.getByTestId('flyby-movie-from'), 0);
  await typeNumber(page, panel.getByTestId('flyby-movie-to'), 3);
  await typeNumber(page, panel.getByTestId('flyby-movie-hold'), 0.5);
  // 24 frames of the flight and 4 of its end, at a draft's 320 by 180.
  await expect(panel.getByTestId('flyby-movie-summary')).toContainText('28 frames of 320 × 180');
  await expect(panel.getByTestId('flyby-movie-file')).toContainText(/silexgis-flyby-\d{4}-\d{2}-\d{2}\.webm/);

  const download = page.waitForEvent('download', { timeout: 420_000 });
  const started = Date.now();
  await panel.getByTestId('flyby-movie-export').click();
  // While it is being made, the bar is held and the view keeps the camera to the recorder.
  await expect(panel.getByTestId('flyby-movie-progress')).toBeVisible();
  const file = await download;
  const seconds = (Date.now() - started) / 1000;
  expect(file.suggestedFilename()).toMatch(/^silexgis-flyby-\d{4}-\d{2}-\d{2}\.webm$/);
  const bytes = await readFile(await file.path());
  const kept = process.env.SILEXGIS_FLYBY_MOVIE_SHOTS;
  if (kept) {
    await writeFile(path.join(kept, 'draft.webm'), bytes);
  }
  console.log(`[flyby movie] a draft of 28 frames of 320 x 180 was made in ${seconds.toFixed(1)} s`);
  await expect(panel.getByTestId('flyby-movie-progress')).toBeHidden({ timeout: 30_000 });
  await expect(panel.getByTestId('flyby-movie-failed')).toHaveCount(0);

  // ---- read back ----
  expect(bytes.subarray(0, 4).toString('hex')).toBe('1a45dfa3');
  expect(webmFrames(bytes)).toBe(28);
  const played = await playedAs(page, bytes);
  expect([played.width, played.height]).toEqual([320, 180]);
  expect(played.duration).toBeGreaterThan(3.3);
  expect(played.duration).toBeLessThan(3.7);

  // The camera is where it was before the movie was made.
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);

  // Closed, the scene fills its view again.
  await panel.getByTestId('flyby-movie-close').click();
  await expect(panel).toHaveCount(0);
  await expect(page.locator('.scene3d-wrap')).not.toHaveClass(/scene3d-wrap-framed/);
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
});

test('a movie being made stops when asked, saves nothing and hands the scene back as it was', async ({ page }) => {
  test.setTimeout(600_000);
  await openFlyby(page);
  const home = await camera(page);
  await page.getByTestId('flyby-movie').click();
  const panel = page.getByTestId('flyby-movie-dialog');
  await expect(panel).toBeVisible({ timeout: 30_000 });
  // Each frame waited for: long enough to be stopped half-way.
  await panel.locator('label').filter({ has: page.getByTestId('flyby-movie-format-webm') }).click();
  await choose(page, panel, 'flyby-movie-size', '640 × 360');
  await panel.getByTestId('flyby-movie-level-fine').click();
  await expect(panel.getByTestId('flyby-movie-level-fine')).toBeChecked();
  const element = await page.getByTestId('scene3d-container').boundingBox();
  let downloads = 0;
  page.on('download', () => {
    downloads += 1;
  });

  await panel.getByTestId('flyby-movie-export').click();
  // Under way.
  await expect(panel.getByTestId('flyby-movie-progress')).toContainText(/Frame [2-9]\d* of/, { timeout: 300_000 });
  // The close button asks first, and "keep going" keeps it going.
  await panel.getByTestId('flyby-movie-close').click();
  await page.getByRole('button', { name: 'Keep going' }).click();
  await expect(panel).toBeVisible();
  await expect(panel.getByTestId('flyby-movie-progress')).toBeVisible();

  await panel.getByTestId('flyby-movie-cancel').click();
  await expect(panel.getByTestId('flyby-movie-progress')).toBeHidden({ timeout: 60_000 });
  await expect(panel.getByTestId('flyby-movie-failed')).toHaveCount(0);
  expect(downloads).toBe(0);
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
  // The surface is drawn at its element's own size again, and the element has not moved.
  expect(await page.getByTestId('scene3d-container').boundingBox()).toEqual(element);
  const surface = await page.evaluate(() => {
    const canvas = document.querySelector('[data-testid="scene3d-surface"] canvas') as HTMLCanvasElement;
    return { width: canvas.width, height: canvas.height, clientWidth: canvas.clientWidth, clientHeight: canvas.clientHeight };
  });
  expect([surface.width, surface.height]).toEqual([surface.clientWidth, surface.clientHeight]);
});
