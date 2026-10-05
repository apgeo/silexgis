// SPDX-License-Identifier: AGPL-3.0-or-later
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { expect, type Locator, type Page } from '@playwright/test';

/**
 * A picture of the page, or of one part of it, taken once it has stopped changing.
 *
 * A map or a model goes on drawing for several frames after the answer it shows has arrived: a
 * sheet decodes, a pin lands on the frame after the link that placed it, a viewport that has just
 * been resized lays itself out again, a marker slides to where a replay puts it. A picture taken the
 * moment the assertions pass catches whichever of those frames happened to be up. Two captures in a
 * row that are the same byte for byte are the drawing at rest, which is the test Playwright's own
 * screenshot assertion applies before it compares anything.
 *
 * CSS animations are finished rather than caught halfway, so a drawer still sliding in does not
 * keep the picture from ever settling. Nothing drawn on a canvas is touched by that.
 */
export async function settledPicture(
  target: Page | Locator,
  options: { fullPage?: boolean } = {},
): Promise<Buffer> {
  let previous: Buffer | null = null;
  let settled: Buffer | null = null;
  await expect
    .poll(
      async () => {
        const current =
          'goto' in target
            ? await target.screenshot({ fullPage: options.fullPage, animations: 'disabled' })
            : await target.screenshot({ animations: 'disabled' });
        if (previous !== null && previous.equals(current)) {
          settled = current;
        }
        previous = current;
        return settled !== null;
      },
      { message: 'the picture never stopped changing', timeout: 30_000 },
    )
    .toBe(true);
  return settled!;
}

/** Writes a settled picture to a file: the screenshots a run is asked to leave behind. */
export async function settledScreenshot(
  target: Page | Locator,
  path: string,
  options: { fullPage?: boolean } = {},
): Promise<void> {
  const picture = await settledPicture(target, options);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, picture);
}
