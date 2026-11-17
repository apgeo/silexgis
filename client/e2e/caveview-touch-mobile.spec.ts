// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { caveWithSurvey, openModel } from './caveviewLabelSizeFlow.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { differingPixels, settledPicture } from './settled.ts';

// The real survey viewer under a finger, on a phone. The unit tests of the panel drive a stand-in
// viewer that emits the events the real one is said to emit for a tap; a viewer release that
// changed what a stationary tap dispatches — a second tap needed, or the camera flown away from
// under the finger — would pass every one of them. So here the vendored viewer is tapped, and what
// is checked is read off its drawing: a tap on the survey shows what was tapped where it stands,
// and a tap on empty space takes it off again and leaves the very picture there was before, which
// it would not be had the camera moved in between.
//
// And the dialog opened and closed over and over on the phone, where the browser allows fewer
// live drawing contexts and takes the oldest away past that.

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/** A drawing changing by fewer pixels than this is the same drawing drawn twice. */
const SOMETHING_SHOWN = 100;

interface Spots {
  /** Page points over the drawn survey, spread out, nearest the middle first. */
  drawn: { x: number; y: number }[];
  /** A page point over the model's surface with nothing drawn anywhere near it. */
  empty: { x: number; y: number } | null;
}

/**
 * Where on the page the survey is drawn and where nothing is, read off a picture of the viewer:
 * the colour most of the picture is in is the background, and only points whose topmost element
 * is the viewer's own drawing surface are offered — a tap on a toolbar button is not a tap on the
 * model.
 */
async function spotsOn(page: Page, container: Locator, picture: Buffer): Promise<Spots> {
  const box = (await container.boundingBox())!;
  return page.evaluate(
    async ({ data, box }) => {
      const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const { data: pixels, width, height } = context.getImageData(0, 0, bitmap.width, bitmap.height);
      const scale = width / box.width;
      const at = (x: number, y: number) => (y * width + x) * 4;
      const counts = new Map<number, number>();
      for (let i = 0; i < pixels.length; i += 4) {
        const key = (pixels[i]! << 16) | (pixels[i + 1]! << 8) | pixels[i + 2]!;
        counts.set(key, (counts.get(key) ?? 0) + 1);
      }
      const background = [...counts.entries()].sort((a, b) => b[1] - a[1])[0]![0];
      const isBackground = (x: number, y: number) => {
        const i = at(x, y);
        return (
          Math.abs(pixels[i]! - ((background >> 16) & 255)) <= 24 &&
          Math.abs(pixels[i + 1]! - ((background >> 8) & 255)) <= 24 &&
          Math.abs(pixels[i + 2]! - (background & 255)) <= 24
        );
      };
      const onTheModel = (x: number, y: number) => {
        const element = document.elementFromPoint(box.x + x / scale, box.y + y / scale);
        return element instanceof HTMLCanvasElement && element.closest('[data-testid="caveview-container"]') !== null;
      };
      const step = Math.max(1, Math.round(4 * scale));
      const drawn: { x: number; y: number; distance: number }[] = [];
      let empty: { x: number; y: number } | null = null;
      const clear = Math.round(14 * scale);
      for (let y = Math.round(height * 0.2); y < height * 0.8; y += step) {
        for (let x = Math.round(width * 0.1); x < width * 0.9; x += step) {
          if (!isBackground(x, y)) {
            if (onTheModel(x, y)) {
              drawn.push({ x, y, distance: Math.hypot(x - width / 2, y - height / 2) });
            }
          } else if (empty === null && x > clear && y > clear && x < width - clear && y < height - clear) {
            let allClear = true;
            for (let dy = -clear; dy <= clear && allClear; dy += step) {
              for (let dx = -clear; dx <= clear && allClear; dx += step) {
                allClear = isBackground(x + dx, y + dy);
              }
            }
            if (allClear && onTheModel(x, y)) {
              empty = { x, y };
            }
          }
        }
      }
      // Spread out, so that a few tries cover different parts of the survey.
      drawn.sort((a, b) => a.distance - b.distance);
      const spread: { x: number; y: number }[] = [];
      for (const spot of drawn) {
        if (spread.every((other) => Math.hypot(other.x - spot.x, other.y - spot.y) > 20 * scale)) {
          spread.push(spot);
        }
      }
      const toPage = ({ x, y }: { x: number; y: number }) => ({ x: box.x + x / scale, y: box.y + y / scale });
      return { drawn: spread.slice(0, 24).map(toPage), empty: empty === null ? null : toPage(empty) };
    },
    { data: picture.toString('base64'), box },
  );
}

test('on a phone a tap on the survey shows what was tapped without moving the model, and a tap beside it takes that off', async ({
  page,
}) => {
  test.setTimeout(300_000);
  await login(page);
  const caveId = await caveWithSurvey(page, madeCaves);
  const viewer = await openModel(page, caveId);
  const container = viewer.getByTestId('caveview-container');

  // The case this is about: a finger, and the viewer's narrow arrangement.
  expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true);
  await expect(container).toHaveClass(/cv-mobile/);

  const before = await settledPicture(container, { stillFor: 1_500 });
  const spots = await spotsOn(page, container, before);
  expect(spots.drawn.length, 'the survey is drawn on the model').toBeGreaterThan(0);
  expect(spots.empty, 'some of the model is empty space').not.toBeNull();

  // Tapped where the survey is drawn until a tap shows something: a line between two stations
  // may answer nothing, and nothing here knows where the stations themselves are drawn.
  let shown: Buffer | null = null;
  let shownBy = 0;
  for (const spot of spots.drawn) {
    await page.touchscreen.tap(spot.x, spot.y);
    const now = await settledPicture(container, { stillFor: 1_500 });
    const changed = await differingPixels(page, before, now);
    if (changed > SOMETHING_SHOWN) {
      shown = now;
      shownBy = changed;
      break;
    }
  }
  expect(shown, 'a tap on the drawn survey showed what was tapped').not.toBeNull();

  // A tap on empty space takes it off, and what is left is the picture from before the first tap:
  // the camera did not move. A model flown towards what was tapped would differ everywhere.
  await page.touchscreen.tap(spots.empty!.x, spots.empty!.y);
  const after = await settledPicture(container, { stillFor: 1_500 });
  expect(
    await differingPixels(page, before, after),
    'after the tap beside the survey the drawing is the one from before the first tap',
  ).toBeLessThan(shownBy / 5);
});

/** Past the number of live drawing contexts a phone's browser is likely to allow at once. */
const OPENINGS = 10;

test('on a phone a survey opened and closed again and again leaves one live drawing', async ({ page }) => {
  test.setTimeout(600_000);
  const warnings: string[] = [];
  page.on('console', (message) => {
    if (/too many active webgl contexts|context lost/i.test(message.text())) {
      warnings.push(message.text());
    }
  });
  await login(page);
  const caveId = await caveWithSurvey(page, madeCaves);

  // Opened from the cave's page once, and from then on again from the same page: a page load
  // would hand every context back by itself and the test would prove nothing.
  let viewer = await openModel(page, caveId);
  await page.evaluate(() => {
    (window as unknown as { samePage?: boolean }).samePage = true;
  });

  for (let opening = 1; opening <= OPENINGS; opening++) {
    if (opening > 1) {
      await page
        .locator('.ant-card', { hasText: '3D survey models' })
        .getByRole('row')
        .filter({ hasText: 'Label sizes' })
        .getByRole('button', { name: 'View in 3D' })
        .tap();
      viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
      await expect(viewer.getByTestId('caveview-container').locator('canvas').first()).toBeAttached({
        timeout: 30_000,
      });
      await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
    }
    // One drawing on the page at a time, whichever opening this is.
    await expect(page.locator('[data-testid="caveview-container"] canvas')).toHaveCount(1);
    if (opening === OPENINGS) {
      const lost = await viewer
        .getByTestId('caveview-container')
        .locator('canvas')
        .first()
        .evaluate((canvas: HTMLCanvasElement) => {
          const context = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
          return context === null ? 'no context' : context.isContextLost();
        });
      expect(lost).toBe(false);
      break;
    }
    await viewer.getByRole('button', { name: 'Close' }).first().tap();
    await expect(page.getByTestId('caveview-container')).toHaveCount(0, { timeout: 30_000 });
  }

  expect(warnings, 'the browser took a drawing context away').toEqual([]);
  expect(await page.evaluate(() => (window as unknown as { samePage?: boolean }).samePage)).toBe(true);
});
