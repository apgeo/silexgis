// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { caveWithSurvey, chooseSize, coveredControls, markedSize, openModel } from './caveviewLabelSizeFlow.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { settledPicture } from './settled.ts';

// How large a model's station names are written, chosen on the model. What this shows that the
// unit tests cannot: that the viewer bundle the application ships takes the size it is handed and
// redraws the names at it. The unit tests hand the size to a stand-in, and a stand-in accepts any
// property — so a bundle one release too old would pass them all and draw every name as before.
// Nothing here can ask the viewer what size it holds; it is read off the drawing instead.

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/**
 * How many pixels of two pictures of the same size differ, counted in the page: the browser reads
 * a picture without anything being added to the project to read one.
 */
async function differing(page: Page, one: Buffer, other: Buffer): Promise<number> {
  return page.evaluate(
    async ([a, b]) => {
      const read = async (base64: string) => {
        const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
        const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
        const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
        const context = canvas.getContext('2d')!;
        context.drawImage(bitmap, 0, 0);
        return context.getImageData(0, 0, bitmap.width, bitmap.height);
      };
      const [left, right] = [await read(a), await read(b)];
      if (left.width !== right.width || left.height !== right.height) {
        throw new Error('the two pictures are not the same size');
      }
      let count = 0;
      for (let i = 0; i < left.data.length; i += 4) {
        // A channel a few steps off is the same pixel drawn twice, not a different one.
        if (
          Math.abs(left.data[i] - right.data[i]) > 24 ||
          Math.abs(left.data[i + 1] - right.data[i + 1]) > 24 ||
          Math.abs(left.data[i + 2] - right.data[i + 2]) > 24
        ) {
          count++;
        }
      }
      return count;
    },
    [one.toString('base64'), other.toString('base64')],
  );
}

test("the size of a model's station names is chosen on the model, redraws them, and is kept for the next model", async ({
  page,
}) => {
  test.setTimeout(300_000);
  await login(page);

  const caveId = await caveWithSurvey(page, madeCaves);

  let viewer = await openModel(page, caveId);
  const names = () => viewer.locator('.cv-toolbar-stationLabels');
  /**
   * The drawing at rest, with the pointer off it: a pointer left over the model is over a station,
   * and the viewer says so on the drawing.
   */
  const drawn = async () => {
    await viewer.locator('.ant-modal-title').hover();
    return settledPicture(viewer.getByTestId('caveview-container'), { stillFor: 1_500 });
  };
  /**
   * Brings the camera close to the middle of the cave. The viewer writes a station's name only
   * once the camera is near it — from where a model opens, showing the whole cave, it writes none
   * at any size — so two sizes can only be told apart from close in. Turned on the wheel as a
   * reader would, in steps the viewer has time to take one at a time.
   */
  const closeIn = async () => {
    const box = (await viewer.getByTestId('caveview-container').boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (let turn = 0; turn < 72; turn++) {
      await page.mouse.wheel(0, -400);
      await page.waitForTimeout(60);
    }
  };
  const choose = (percent: number) => chooseSize(page, viewer, percent);
  const marked = () => markedSize(page, viewer);

  // The control is on the model once there is a model, nothing of the viewer's lies over it or it
  // over anything of the viewer's, and it starts at the size the viewer draws by itself.
  expect(await coveredControls(page)).toEqual([]);
  expect(await marked()).toBe('100%');

  // ---- The names, off and then on, from close enough to be written ----
  await closeIn();
  const bare = await drawn();
  await names().click();
  await expect(names()).toHaveAttribute('aria-pressed', 'true');

  // ---- Large, small, and large again ----
  await choose(150);
  const large = await drawn();
  await choose(40);
  const small = await drawn();
  await choose(150);
  const largeAgain = await drawn();

  // What the names add to the bare drawing is their ink: more of it written large than small. And
  // the same choice made twice is the same drawing, so what moved between the two pictures is the
  // names' size and not a camera still drifting or a frame caught halfway.
  const ink = { large: await differing(page, bare, large), small: await differing(page, bare, small) };
  const apart = await differing(page, large, small);
  const again = await differing(page, large, largeAgain);
  // Measured on this survey from this distance: some four thousand pixels of names at the large
  // size, some four hundred at the small, and eight hundred that are the toolbar's own button
  // showing that names are on, which both counts include.
  expect(apart, 'the names are drawn, and differently at the two sizes').toBeGreaterThan(1_500);
  expect(ink.large, 'names written large cover more of the drawing than names written small').toBeGreaterThan(
    ink.small * 1.5,
  );
  expect(again, 'the same size chosen twice draws the same names').toBeLessThan(apart / 10);

  // ---- Kept: the next model opened is drawn at the size chosen on this one ----
  await choose(40);
  // Going to the cave's page by its address loads the application afresh: what is left of the
  // choice is what the browser was asked to keep.
  viewer = await openModel(page, caveId);
  expect(await marked()).toBe('40%');
  await closeIn();
  await names().click();
  await expect(names()).toHaveAttribute('aria-pressed', 'true');
  const reopened = await drawn();
  // A viewer that had only been told the size, and not taken it, would mark 40% and draw at 100%.
  expect(await differing(page, reopened, small)).toBeLessThan(await differing(page, reopened, large));
});
