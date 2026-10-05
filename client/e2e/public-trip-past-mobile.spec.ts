// SPDX-License-Identifier: AGPL-3.0-or-later
import { join } from 'node:path';
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';

/**
 * A cave's past trips under a finger.
 *
 * <b>Why this is its own file, claimed by the phone project.</b> Every control on the strip that
 * plays a past trip is sized on the pointer: large where a finger drives it, small under a mouse.
 * The spec that stands a real archive up runs under the desktop project, where the pointer is a
 * mouse whatever the viewport is set to — so at 360px it proved the mouse sizes and nothing about
 * the finger ones, and the rail's `touch-action` and the frame's sheet under a tap were exercised
 * by nobody. Here the device is the phone profile, touch and a coarse pointer included, and the
 * checks are the ones only that device can answer.
 *
 * <b>The answers are served by this test, not by a trip.</b> Standing a closed, published trip up
 * takes an installation configured with no grace after close, a survey upload and a dozen reports;
 * the desktop spec already does that and proves the server's half. What this proves is the browser:
 * layout, target sizes and gestures. The survey file is the real committed fixture, so the drawing
 * the strip sits under is a real one.
 */

const TOKEN = 'e2e-past-follow-token';
const PAST_TRIP = 'aaaaaaaa-0000-4000-8000-000000000001';
const TEAM_SURVEY = '22222222-2222-4222-8222-222222222222';
/** Two stations of the committed survey fixture. */
const STATION_A = 'p8.p8.98';
const STATION_B = 'p8.bens_dig.217';
const PAST_TITLE = 'Peștera Demo Mare — the 2019 push';

/** The narrowest screen these surfaces are designed for; the project's own device is wider. */
const NARROWEST = { width: 360, height: 740 };
/** The smallest frame a club is told it can paste the viewer into. */
const SMALLEST_FRAME = { width: 360, height: 260 };
/** What antd's large control measures, which is what a finger is given here. */
const FINGER = 40;

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
};

const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

/** The link's own trip: one person underground now, so "back to now" has somebody to go back to. */
const liveEnvelope = () => ({
  tripLogId: 'bbbbbbbb-0000-4000-8000-000000000001',
  expedition: null,
  title: 'Peștera Demo Mare — explorare',
  tripDate: new Date().toISOString().slice(0, 10),
  tripDateEnd: null,
  state: 'armed',
  armedAt: minutesAgo(180),
  closedAt: null,
  positionsWithheld: false,
  model,
  teams: [],
  participants: [
    {
      ordinal: 1,
      label: 'Carmen',
      teamId: null,
      stationName: STATION_A,
      depthM: null,
      lastRecordedAt: minutesAgo(10),
      positionRecordedAt: minutesAgo(10),
      positionOnOtherModel: false,
      in: true,
      out: false,
    },
  ],
});

const fix = (recordedAt: string, stationName: string | null, teamId: string | null, out = false) => ({
  recordedAt,
  teamId,
  stationName,
  depthM: null,
  positionOnOtherModel: false,
  in: !out,
  out,
});

/** A finished day in the same cave: two people, one team, reports hours apart. */
const pastTrack = () => ({
  tripLogId: PAST_TRIP,
  expedition: null,
  title: PAST_TITLE,
  tripDate: '2019-07-06',
  tripDateEnd: null,
  armedAt: '2019-07-06T08:00:00Z',
  closedAt: '2019-07-06T18:00:00Z',
  positionsWithheld: false,
  trackTruncated: false,
  model,
  teams: [{ id: TEAM_SURVEY, title: 'Survey' }],
  participants: [
    {
      ordinal: 1,
      label: 'Mircea',
      track: [
        fix('2019-07-06T09:00:00Z', null, null),
        fix('2019-07-06T11:00:00Z', STATION_A, null),
        fix('2019-07-06T17:00:00Z', null, null, true),
      ],
    },
    {
      ordinal: 2,
      label: 'Ileana',
      track: [
        fix('2019-07-06T09:00:00Z', null, null),
        fix('2019-07-06T13:00:00Z', STATION_B, TEAM_SURVEY),
        fix('2019-07-06T17:30:00Z', null, TEAM_SURVEY, true),
      ],
    },
  ],
});

/**
 * Answers every public read this link makes — the trip, the cave's two lists and the one track —
 * and the survey file. Anything else asked of the public surface is a 404, which the console sweep
 * reports: a read this spec did not expect is a read worth knowing about.
 */
async function serveCave(page: Page) {
  const base = `/api/v1/public/trips/${TOKEN}`;
  await page.route('**/api/v1/public/trips/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === base) {
      await route.fulfill({ json: liveEnvelope() });
    } else if (path === `${base}/past`) {
      await route.fulfill({
        json: {
          trips: [
            {
              tripLogId: PAST_TRIP,
              expedition: null,
              title: PAST_TITLE,
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
    } else if (path === `${base}/past/${PAST_TRIP}`) {
      await route.fulfill({ json: pastTrack() });
    } else if (path === `${base}/live`) {
      await route.fulfill({ json: { trips: [], more: false } });
    } else {
      await route.fulfill({ status: 404, json: {} });
    }
  });
  await page.route('**/api/v1/files/**', (route) =>
    route.fulfill({
      path: join(import.meta.dirname, 'fixtures', 'P8_Master.3d'),
      contentType: 'application/octet-stream',
    }),
  );
}

/** Asserts a control is at least a finger's height, naming it when it is not. */
async function fingerSized(control: Locator, name: string) {
  await expect(control, `${name} is on screen`).toBeVisible();
  const box = (await control.boundingBox())!;
  expect(box.height, `${name} is ${box.height}px tall`).toBeGreaterThanOrEqual(FINGER);
}

const noSidewaysScroll = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

/**
 * Drags one finger along the rail from its handle, stepped, the way a thumb moves.
 *
 * Through the browser's own touch input rather than synthesised pointer events, because what is
 * under test is how the browser hands the gesture out: the rail keeps the horizontal axis for
 * itself and gives the page the vertical one, and only a real touch sequence meets that rule.
 */
async function dragRail(page: Page, by: number) {
  const handle = page.getByTestId('public-past-scrub').locator('.ant-slider-handle');
  const box = (await handle.boundingBox())!;
  const start = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  const finger = (x: number, y: number) => [{ x, y, radiusX: 4, radiusY: 4, force: 1, id: 1 }];
  const steps = 12;
  // The times the events carry: a move every 16 ms, as a touch screen reports them, and a 60 ms
  // rest before the thumb lifts. Written onto the events rather than waited out, because the
  // browser reads the gesture's speed off these times. Laid out in the past and ending now, since a
  // time ahead of the clock would put whatever the test does next before the end of this gesture.
  let at = Date.now() - steps * 16 - 60;
  const stamp = (after: number) => {
    at += after;
    return at / 1000;
  };
  const cdp = await page.context().newCDPSession(page);
  try {
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: finger(start.x, start.y),
      timestamp: stamp(0),
    });
    for (let step = 1; step <= steps; step += 1) {
      await cdp.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: finger(start.x + (by * step) / steps, start.y),
        timestamp: stamp(16),
      });
    }
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchEnd',
      touchPoints: [],
      timestamp: stamp(60),
    });
  } finally {
    await cdp.detach();
  }
}

test.describe('a cave’s past trips on a phone', () => {
  test('a row pressed at the bottom of the page is answered at the top, with controls a finger can hit', async ({
    page,
  }) => {
    await serveCave(page);
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(page.getByTestId('public-trip-state-armed')).toBeVisible({ timeout: 30_000 });

    await page.getByText('Past trips in this cave').tap();
    const row = page.getByTestId(`public-past-trip-${PAST_TRIP}`);
    await expect(row).toBeVisible({ timeout: 20_000 });
    await row.tap();

    // The statement that this is the past, and the way back, where the reader is looking.
    await expect(page.getByTestId('public-past-banner')).toBeInViewport({ timeout: 20_000 });
    await expect(page.getByTestId('public-past-banner-what')).toContainText(PAST_TITLE);

    // Sized for the finger that is driving them, not for the mouse the desktop run has.
    await fingerSized(page.getByTestId('public-past-back'), 'the way back');
    await fingerSized(page.getByTestId('public-past-play'), 'play');
    await fingerSized(page.getByTestId('public-past-report-previous'), 'the previous report');
    await fingerSized(page.getByTestId('public-past-report-next'), 'the next report');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    // The rail under a thumb: it moves the clock, and it does not move the page.
    const scrub = page.getByTestId('public-past-scrub');
    await scrub.scrollIntoViewIfNeeded();
    const before = {
      clock: await page.getByTestId('public-past-clock').textContent(),
      scrollY: await page.evaluate(() => window.scrollY),
    };
    await dragRail(page, 150);
    await expect
      .poll(async () => page.getByTestId('public-past-clock').textContent(), { timeout: 5_000 })
      .not.toBe(before.clock);
    expect(await page.evaluate(() => window.scrollY)).toBe(before.scrollY);

    // Following somebody puts one more control on the strip, inside the banner's own prose — and
    // it is still a target for a finger.
    await page.goto(`/shared/trips/${TOKEN}?past=${PAST_TRIP}&team=${TEAM_SURVEY}`);
    await expect(page.getByTestId('public-past-banner-following')).toContainText('Survey', {
      timeout: 30_000,
    });
    await fingerSized(page.getByTestId('public-past-unfollow'), 'stop following');
    await page.getByTestId('public-past-unfollow').tap();
    await expect(page.getByTestId('public-past-banner-following')).toHaveCount(0);
  });

  test('inside the smallest frame a club may paste, the archive opens under a tap and plays', async ({
    page,
  }) => {
    await serveCave(page);
    await page.setViewportSize(SMALLEST_FRAME);
    await page.goto(`/shared/trips/${TOKEN}/embed`);
    await expect(page.getByTestId('public-trip-embed')).toBeVisible({ timeout: 30_000 });

    // The sheet over the frame, opened by the one line of chrome the frame carries — which is a
    // target for the finger that opens it, like every control on the strip that replaces it.
    await fingerSized(page.getByTestId('public-past-open'), 'the way into the archive, in the frame');
    await page.getByTestId('public-past-open').tap();
    const row = page.getByTestId(`public-past-trip-${PAST_TRIP}`);
    await expect(row).toBeVisible({ timeout: 20_000 });
    await expect(row).toBeInViewport();
    await row.tap();

    // The sheet gets out of the way, and the frame says it is the past.
    await expect(page.getByTestId('public-past-banner')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);
    await expect(row).toBeHidden();

    await fingerSized(page.getByTestId('public-past-play'), 'play, in the frame');
    await fingerSized(page.getByTestId('public-past-back'), 'the way back, in the frame');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    // And the transport answers the finger.
    const opened = await page.getByTestId('public-past-clock').textContent();
    await page.getByTestId('public-past-report-next').tap();
    await expect(page.getByTestId('public-past-clock')).not.toHaveText(opened ?? '');
  });
});
