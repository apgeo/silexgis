// SPDX-License-Identifier: AGPL-3.0-or-later
import { join } from 'node:path';
import type { Page } from '@playwright/test';

/**
 * One published cave whose installation publishes everything it may: the names the cave gives its
 * places, and the photographs hung on the moments of a finished trip.
 *
 * <b>Served by the test, because no installation the suite stands up publishes either.</b> Both
 * are settings an operator turns on, and both are off as installed — which is what every other
 * browser test of the published page runs against, and what proves the page prints nothing for
 * them. What is left to prove is the browser's half of the other side: a row of thumbnails that
 * scrolls inside itself at a phone's width, a picture that opens larger under a press, the same
 * row inside a frame's sheet, a name standing over a station. None of that is visible to a test
 * that renders the component without a layout engine.
 *
 * <b>One fixture for the desktop and the phone runs</b>, so the two look at the same answer and a
 * change to the shape is made once. The survey is the real committed file, so the drawing the
 * strip sits beside is a real one; the photographs are a real image, so a thumbnail that did not
 * decode is a thumbnail that was not drawn.
 */

export const EXTRAS_TOKEN = 'e2e-published-extras-token';
export const EXTRAS_TRIP = 'eeeeeeee-0000-4000-8000-000000000001';
export const EXTRAS_TITLE = 'Peștera Demo Mare — the day of the photographs';

/** Two stations of the committed survey fixture; the cave has named the first. */
export const NAMED_STATION = 'p8.p8.98';
export const PLAIN_STATION = 'p8.bens_dig.217';
export const PLACE_NAME = 'Sala Mare';

/** A moment of the replay before any photograph, and the two moments photographs hang on. */
export const BEFORE_ANY = '2019-07-06T09:30:00Z';
export const FIRST_MOMENT = '2019-07-06T11:00:00Z';
export const SECOND_MOMENT = '2019-07-06T13:00:00Z';
/** How many photographs hang on the first moment: more than a phone's width holds side by side. */
export const AT_FIRST_MOMENT = 4;
export const CAPTION = 'At the pitch head';

const model = {
  format: 'survex3d',
  modelUrl: '/api/v1/files/00000000-0000-0000-0000-000000000001/content?token=e2e',
  meshUrl: null,
  anchorLongitude: null,
  anchorLatitude: null,
  anchorHeightM: null,
  sourceEpsg: null,
  proj4: null,
  pictures: [],
  rasterMaps: [],
  places: [{ station: NAMED_STATION, depthM: 42, label: PLACE_NAME }],
};

const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

/**
 * The link's own trip: one person underground now, at the station the cave has named, and the
 * hour the party planned to be out by where the installation publishes one.
 */
const liveEnvelope = (expectedReturnAt: string | null) => ({
  tripLogId: 'eeeeeeee-0000-4000-8000-000000000002',
  expedition: null,
  title: 'Peștera Demo Mare — explorare',
  tripDate: new Date().toISOString().slice(0, 10),
  tripDateEnd: null,
  state: 'armed',
  armedAt: minutesAgo(180),
  closedAt: null,
  expectedReturnAt,
  positionsWithheld: false,
  model,
  teams: [],
  participants: [
    {
      ordinal: 1,
      label: 'Carmen',
      teamId: null,
      stationName: NAMED_STATION,
      depthM: null,
      lastRecordedAt: minutesAgo(10),
      positionRecordedAt: minutesAgo(10),
      positionOnOtherModel: false,
      in: true,
      out: false,
    },
  ],
});

const fix = (recordedAt: string, stationName: string | null, out = false) => ({
  recordedAt,
  teamId: null,
  stationName,
  depthM: null,
  positionOnOtherModel: false,
  in: !out,
  out,
});

const picture = (file: number, at: string, ordinal: number | null, caption: string | null) => ({
  at,
  ordinal,
  thumbnailUrl: `/api/v1/files/99999999-0000-4000-8000-00000000000${file}/thumbnail?token=e2e-${file}`,
  caption,
});

/**
 * A finished day: two people, the first named by the club and the second not, and photographs on
 * two of its moments — of the party, of each person, with and without a caption.
 */
const pastTrack = () => ({
  tripLogId: EXTRAS_TRIP,
  expedition: null,
  title: EXTRAS_TITLE,
  tripDate: '2019-07-06',
  tripDateEnd: null,
  armedAt: '2019-07-06T08:00:00Z',
  closedAt: '2019-07-06T18:00:00Z',
  positionsWithheld: false,
  trackTruncated: false,
  pictures: [
    picture(1, FIRST_MOMENT, null, null),
    picture(2, FIRST_MOMENT, 1, CAPTION),
    picture(3, FIRST_MOMENT, 2, null),
    picture(4, FIRST_MOMENT, null, 'The way on'),
    picture(5, SECOND_MOMENT, 2, null),
  ],
  model,
  teams: [],
  participants: [
    {
      ordinal: 1,
      label: 'Mircea',
      track: [
        fix('2019-07-06T09:00:00Z', null),
        fix(FIRST_MOMENT, NAMED_STATION),
        fix('2019-07-06T17:00:00Z', null, true),
      ],
    },
    {
      ordinal: 2,
      label: null,
      track: [
        fix('2019-07-06T09:00:00Z', null),
        fix(SECOND_MOMENT, PLAIN_STATION),
        fix('2019-07-06T17:30:00Z', null, true),
      ],
    },
  ],
});

/** A real image, small: what a rendering of a photograph is to the browser that draws it. */
const PHOTOGRAPH = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

/**
 * Answers every read this link makes, the survey file and every rendering of a photograph.
 *
 * Hands back the address of every rendering the page asked for, in order: a photograph is asked
 * for when it is shown and not before, and at the size it is shown at.
 */
export async function servePublishedExtras(
  page: Page,
  { expectedReturnAt = null }: { expectedReturnAt?: string | null } = {},
) {
  const base = `/api/v1/public/trips/${EXTRAS_TOKEN}`;
  const renderings: URL[] = [];
  await page.route('**/api/v1/public/trips/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === base) {
      await route.fulfill({ json: liveEnvelope(expectedReturnAt) });
    } else if (path === `${base}/past`) {
      await route.fulfill({
        json: {
          trips: [
            {
              tripLogId: EXTRAS_TRIP,
              expedition: null,
              title: EXTRAS_TITLE,
              tripDate: '2019-07-06',
              tripDateEnd: null,
              closedAt: '2019-07-06T18:00:00Z',
              participantCount: 2,
              playable: true,
            },
          ],
          more: false,
        },
      });
    } else if (path === `${base}/past/${EXTRAS_TRIP}`) {
      await route.fulfill({ json: pastTrack() });
    } else if (path === `${base}/live`) {
      await route.fulfill({ json: { trips: [], more: false } });
    } else {
      await route.fulfill({ status: 404, json: {} });
    }
  });
  await page.route('**/api/v1/files/**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/thumbnail')) {
      renderings.push(url);
      await route.fulfill({ body: PHOTOGRAPH, contentType: 'image/png' });
    } else {
      await route.fulfill({
        path: join(import.meta.dirname, 'fixtures', 'P8_Master.3d'),
        contentType: 'application/octet-stream',
      });
    }
  });
  return { renderings };
}

/** The address of the published page of this link, on a moment of the finished trip when one is named. */
export const extrasPage = (at?: string, frame = false) =>
  `/shared/trips/${EXTRAS_TOKEN}${frame ? '/embed' : ''}${
    at === undefined ? '' : `?past=${EXTRAS_TRIP}&at=${at}`
  }`;
