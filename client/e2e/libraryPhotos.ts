// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';

/** The neighbouring libraries an installation can be connected to, as the map's requests name them. */
export type LibrarySource = 'photoprism' | 'immich';

/**
 * Turns the wheel once over the map, and returns once every library named has answered for the view
 * the map came to rest on, the layer panel says what it answered, and the map has drawn it.
 *
 * The overlay asks each enabled library about the rectangle on screen once the map stops moving —
 * one request per library and per move — so those answers are what a zoom is waiting for. The
 * panel's line is waited for as well because callers read the number from there rather than from
 * the answer, and a line read a moment too early still shows the view before. The two frames at the
 * end are the map drawing the pins it was handed, which a click aimed at one of them needs.
 */
export async function wheelAndAwaitLibraries(
  page: Page,
  deltaY: number,
  sources: readonly LibrarySource[],
): Promise<void> {
  const answers = sources.map((source) =>
    page.waitForResponse(
      (response) => new URL(response.url()).pathname === `/api/v1/photo-libraries/${source}/map`,
      { timeout: 30_000 },
    ),
  );
  await page.mouse.wheel(0, deltaY);
  for (const [index, answer] of (await Promise.all(answers)).entries()) {
    if (!answer.ok()) {
      continue;
    }
    const shown = ((await answer.json()) as { features: unknown[] }).features.length;
    const status = page.getByTestId(`library-photos-status-${sources[index]}`);
    if (shown > 0) {
      await expect(status).toContainText(`${shown} photographs shown`);
    } else {
      await expect(status).not.toContainText(/\d+ photographs shown/);
    }
  }
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
      }),
  );
}
