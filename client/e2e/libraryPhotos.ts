// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, type Page } from '@playwright/test';

import { mapAddress } from './helpers.ts';

// What the photo-library specs share.
//
// They run only against libraries stood up for them, holding nothing but pictures a generator
// made: scripts/e2e-photo-libraries.mjs starts both libraries, fills them from
// deploy/photo-fixtures/make-fixtures.py, and says so through the two variables below. A library
// somebody uses holds that person's photographs, and a spec that prints what a page says and
// keeps a picture of the screen has no business being pointed at one — so there is no way to
// point these at one by setting a single switch.

/**
 * Why the photo-library specs are not running, or null when they may.
 *
 * Both variables, not one: the first alone used to be the whole switch, and it said only that
 * some library was there.
 */
export const noLibrariesBecause: string | null =
  process.env.SILEXGIS_E2E_PHOTO_LIBRARY && process.env.SILEXGIS_E2E_PHOTO_FIXTURES
    ? null
    : 'runs through scripts/e2e-photo-libraries.mjs, which stands up libraries of invented pictures';

/** One rectangle the generator filled, and how many pictures it put there. */
export interface FixtureRectangle {
  key: string;
  latMin: number;
  latMax: number;
  lonMin: number;
  lonMax: number;
  count: number;
}

/** One generated picture and where the generator says it was taken. */
export interface FixturePicture {
  file: string;
  rect: string;
  lat: number;
  lon: number;
}

/** The generator's own account of what it wrote: the only population these specs know the size of. */
export function fixtureManifest(): { rectangles: FixtureRectangle[]; files: FixturePicture[] } {
  const directory = process.env.SILEXGIS_E2E_PHOTO_FIXTURES;
  if (!directory) {
    throw new Error('SILEXGIS_E2E_PHOTO_FIXTURES names no directory');
  }
  return JSON.parse(readFileSync(join(directory, 'manifest.json'), 'utf8'));
}

/** The rectangle the generator put its pictures in — the one whose answer must not be empty. */
export function fixtureRectangle(): FixtureRectangle {
  const rectangle = fixtureManifest().rectangles.find((candidate) => candidate.key === 'A');
  if (!rectangle) {
    throw new Error('the fixture manifest has no rectangle A');
  }
  return rectangle;
}

/** The map's address with the whole of a rectangle in view and nothing of the next one. */
export function mapOver(rectangle: FixtureRectangle): string {
  // Zoom ten draws a fifth of a degree as about two hundred pixels, which fits beside the docks
  // of the narrowest desktop window and is nowhere near wide enough to reach another rectangle.
  return mapAddress({
    lat: (rectangle.latMin + rectangle.latMax) / 2,
    lon: (rectangle.lonMin + rectangle.lonMax) / 2,
    zoom: 10,
  });
}

/**
 * The picture of a rectangle that stands furthest from every other one.
 *
 * A pin is clicked by where it is, and the map hides a pin that would sit on top of another. The
 * loneliest one is the one whose pin is surely drawn and surely the only thing under the pointer.
 */
export function loneliestPicture(rectangle: FixtureRectangle): FixturePicture {
  const inside = fixtureManifest().files.filter((file) => file.rect === rectangle.key);
  const gap = (a: FixturePicture, b: FixturePicture) => Math.hypot(a.lat - b.lat, a.lon - b.lon);
  const nearest = (picture: FixturePicture) =>
    Math.min(...inside.filter((other) => other !== picture).map((other) => gap(picture, other)));
  return inside.reduce((best, picture) => (nearest(picture) > nearest(best) ? picture : best));
}

/**
 * True when a request went to one of the libraries itself rather than to this application.
 *
 * Told by port, because a browser may spell the same machine `localhost` or `127.0.0.1`. The
 * ports are the ones the libraries were actually started on — a check against the ports they
 * usually have would pass, having looked at nothing, the day they were started on others.
 */
export function reachesALibrary(url: string): boolean {
  const addresses = (process.env.SILEXGIS_E2E_LIBRARY_ADDRESSES ?? '').split(',').filter(Boolean);
  if (addresses.length === 0) {
    throw new Error('SILEXGIS_E2E_LIBRARY_ADDRESSES names no library');
  }
  const port = new URL(url).port;
  return addresses.some((address) => new URL(address).port === port);
}

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
