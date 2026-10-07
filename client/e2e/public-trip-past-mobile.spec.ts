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
/** The link's own trip, and a second party of the same cave being followed at the same time. */
const LIVE_TRIP = 'bbbbbbbb-0000-4000-8000-000000000001';
const LIVE_TITLE = 'Peștera Demo Mare — explorare';
const OTHER_TRIP = 'cccccccc-0000-4000-8000-000000000001';
const OTHER_TITLE = 'Peștera Demo Mare — echipa de topografie';
/** A finished trip of the cave that was part of no camp. */
const LONE_TRIP = 'aaaaaaaa-0000-4000-8000-000000000002';
/** The camp the 2019 trip and the second party belong to, as the server names one: an id and a name. */
const CAMP = { id: 'dddddddd-0000-4000-8000-000000000001', name: 'Demo Mare 2019' };

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
  tripLogId: LIVE_TRIP,
  expedition: null,
  title: LIVE_TITLE,
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
 * The cave's other party, as a row of the list of parties being followed: the envelope's own
 * shape without a survey, because every place in it was already decided against the link's.
 * One person underground at a station and one out, so the row's three figures are not all alike
 * and the party list it becomes has somebody to place.
 */
const otherParty = () => ({
  tripLogId: OTHER_TRIP,
  expedition: CAMP,
  title: OTHER_TITLE,
  tripDate: new Date().toISOString().slice(0, 10),
  tripDateEnd: null,
  state: 'armed',
  armedAt: minutesAgo(120),
  closedAt: null,
  positionsWithheld: false,
  teams: [],
  participants: [
    {
      ordinal: 1,
      label: 'Sorin',
      teamId: null,
      stationName: STATION_B,
      depthM: null,
      lastRecordedAt: minutesAgo(20),
      positionRecordedAt: minutesAgo(20),
      positionOnOtherModel: false,
      in: true,
      out: false,
    },
    {
      ordinal: 2,
      label: 'Dana',
      teamId: null,
      stationName: null,
      depthM: null,
      lastRecordedAt: minutesAgo(5),
      positionRecordedAt: null,
      positionOnOtherModel: false,
      in: true,
      out: true,
    },
  ],
});

/** What a cave is served with beyond one party underground and one finished trip of no camp. */
interface ServedCave {
  /** A second party of the cave is being followed now, beside the link's own. */
  twoParties?: boolean;
  /** The 2019 trip was part of a camp, and a second finished trip of the cave was part of none. */
  camps?: boolean;
}

/**
 * Answers every public read this link makes — the trip, the cave's two lists and the one track —
 * and the survey file. Anything else asked of the public surface is a 404, which the console sweep
 * reports: a read this spec did not expect is a read worth knowing about.
 *
 * Hands back the path of every public read it answered, in order, for the tests whose claim is
 * about what was — and was not — asked for.
 */
async function serveCave(page: Page, cave: ServedCave = {}) {
  const base = `/api/v1/public/trips/${TOKEN}`;
  const reads: string[] = [];
  await page.route('**/api/v1/public/trips/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    reads.push(path);
    if (path === base) {
      await route.fulfill({ json: liveEnvelope() });
    } else if (path === `${base}/past`) {
      await route.fulfill({
        json: {
          trips: [
            {
              tripLogId: PAST_TRIP,
              expedition: cave.camps ? CAMP : null,
              title: PAST_TITLE,
              tripDate: '2019-07-06',
              tripDateEnd: null,
              closedAt: '2019-07-06T18:00:00Z',
              participantCount: 2,
              playable: true,
            },
            ...(cave.camps
              ? [
                  {
                    tripLogId: LONE_TRIP,
                    expedition: null,
                    title: 'Peștera Demo Mare — a day out in 2017',
                    tripDate: '2017-05-13',
                    tripDateEnd: null,
                    closedAt: '2017-05-13T16:00:00Z',
                    participantCount: 3,
                    playable: true,
                  },
                ]
              : []),
          ],
          more: false,
        },
      });
    } else if (path === `${base}/past/${PAST_TRIP}`) {
      await route.fulfill({ json: pastTrack() });
    } else if (path === `${base}/live`) {
      // The link's own trip is in this list too while it is being followed, as on the server —
      // without its survey, which a row never carries (a member left undefined is not sent).
      const own = { ...liveEnvelope(), model: undefined };
      await route.fulfill({
        json: { trips: cave.twoParties ? [own, otherParty()] : [], more: false },
      });
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
  return { reads, ownTrip: base, liveList: `${base}/live` };
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

  test('a link naming a moment and saying play starts by itself, and the strip copies such a link', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await serveCave(page);
    await page.setViewportSize(NARROWEST);

    // The twin first: the same moment without the word opens standing still, on the same clock.
    await page.goto(`/shared/trips/${TOKEN}?past=${PAST_TRIP}&at=2019-07-06T11:00:00Z`);
    await expect(page.getByTestId('public-past-banner-what')).toContainText(PAST_TITLE, {
      timeout: 30_000,
    });
    const play = page.getByTestId('public-past-play');
    await expect(play).toHaveAccessibleName('Play');
    const standing = await page.getByTestId('public-past-clock').textContent();
    expect(standing).not.toBe('');

    // With the word, nobody presses anything and the clock leaves the moment it opened at.
    await page.goto(`/shared/trips/${TOKEN}?past=${PAST_TRIP}&at=2019-07-06T11:00:00Z&play=1`);
    await expect(play).toHaveAccessibleName('Pause', { timeout: 30_000 });
    await expect
      .poll(async () => page.getByTestId('public-past-clock').textContent(), { timeout: 15_000 })
      .not.toBe(standing);

    // The two buttons that write such a link, sized for the finger and inside the page's width.
    await play.tap();
    await expect(play).toHaveAccessibleName('Play');
    await page.getByTestId('public-past-follow').scrollIntoViewIfNeeded();
    await fingerSized(page.getByTestId('public-past-copy-moment'), 'copy link to this moment');
    await fingerSized(page.getByTestId('public-past-copy-playing'), 'copy link that plays from here');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    const paused = await page.getByTestId('public-past-clock').textContent();
    await page.getByTestId('public-past-copy-playing').tap();
    await expect(page.getByTestId('public-past-copied')).toContainText('Link copied');
    await expect(page.getByTestId('public-past-copied')).toContainText(paused ?? 'no clock');
    const copied = new URL(await page.evaluate(() => navigator.clipboard.readText()));
    expect(copied.pathname).toBe(`/shared/trips/${TOKEN}`);
    expect(copied.searchParams.get('past')).toBe(PAST_TRIP);
    expect(copied.searchParams.get('play')).toBe('1');
    expect(copied.searchParams.get('at')).toMatch(/^2019-07-06T\d\d:\d\d:\d\dZ$/);
    // The address bar was given no moment by the press: only the copied link carries one.
    expect(new URL(page.url()).searchParams.get('at')).toBe('2019-07-06T11:00:00Z');

    // And the link that was copied does what its button said, in a tab that never saw the strip.
    await page.goto(copied.toString());
    await expect(play).toHaveAccessibleName('Pause', { timeout: 30_000 });
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

  test('another party of the cave is watched from a list that is shut until asked for, under a banner, and left again', async ({
    page,
  }) => {
    const served = await serveCave(page, { twoParties: true, camps: true });
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(page.getByTestId('public-trip-state-armed')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('public-trip-party')).toContainText('Carmen');

    // Shut, and unread: a reader who never asks who else is in the cave is told about nobody
    // else, and the page has not asked the server either.
    await expect(page.getByTestId('public-live')).toBeHidden();
    expect(served.reads.filter((path) => path === served.liveList)).toEqual([]);

    await page.getByTestId('public-trip-live-section').tap();
    const other = page.getByTestId(`public-live-trip-${OTHER_TRIP}`);
    await expect(other).toBeVisible({ timeout: 20_000 });
    expect(served.reads.filter((path) => path === served.liveList).length).toBeGreaterThan(0);
    await expect(other).toContainText(OTHER_TITLE);
    // The three standings, each with its number, and the camp the party is out from.
    const standings = page.getByTestId(`public-live-standings-${OTHER_TRIP}`);
    await expect(standings).toContainText('Underground: 1');
    await expect(standings).toContainText('Out: 1');
    await expect(standings).toContainText('Not reported yet: 0');
    await expect(page.getByTestId(`public-live-camp-${OTHER_TRIP}`)).toHaveText(`Camp: ${CAMP.name}`);
    // The link's own trip is named as that, and is neither something to watch nor — while it is
    // the one on screen — somewhere to go back to.
    await expect(page.getByTestId(`public-live-own-${LIVE_TRIP}`)).toHaveText("This link's trip");
    await expect(page.getByTestId(`public-live-watch-${LIVE_TRIP}`)).toHaveCount(0);
    await expect(page.getByTestId('public-live-back-own')).toHaveCount(0);
    await expect(page.getByTestId('public-watch-banner')).toHaveCount(0);

    const watch = page.getByTestId(`public-live-watch-${OTHER_TRIP}`);
    await fingerSized(watch, 'Watch, on the other party’s row');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
    const readsBeforeThePress = served.reads.length;
    await watch.tap();

    // Answered where the reader is looking: whose party this is, and the way back beside it.
    await expect(page.getByTestId('public-watch-banner')).toBeInViewport({ timeout: 20_000 });
    await expect(page.getByTestId('public-watch-banner-what')).toContainText(OTHER_TITLE);
    await expect(page.getByTestId('public-trip-title')).toHaveText(OTHER_TITLE);
    const party = page.getByTestId('public-trip-party');
    await expect(party).toContainText('Sorin');
    await expect(party).toContainText(STATION_B);
    await expect(party).toContainText('Dana');
    await expect(party).not.toContainText('Carmen');
    // The row says it is the one on screen in words, and the link's own row now carries the way back.
    await expect(page.getByTestId(`public-live-watching-${OTHER_TRIP}`)).toHaveText('Watching');
    await expect(page.getByTestId('public-live-back-own')).toHaveCount(1);
    // The tab and the address are still the link's: it was published for one trip, and a copy of
    // the address must never open somebody else's party.
    expect(await page.title()).toBe(LIVE_TITLE);
    expect(new URL(page.url()).search).toBe('');
    // And the press itself asked the server for nothing of its own: the party was already in the
    // list. Only the two reads the page keeps making by the minute may have landed meanwhile.
    expect(
      served.reads
        .slice(readsBeforeThePress)
        .filter((path) => path !== served.liveList && path !== served.ownTrip),
    ).toEqual([]);

    await fingerSized(page.getByTestId('public-watch-back'), 'the way back to this link’s trip');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
    await page.getByTestId('public-watch-back').tap();
    await expect(page.getByTestId('public-watch-banner')).toHaveCount(0);
    await expect(page.getByTestId('public-trip-title')).toHaveText(LIVE_TITLE);
    await expect(party).toContainText('Carmen');
    await expect(party).not.toContainText('Sorin');
    // Going back is not a watch that ended under the reader, and is not announced as one.
    await expect(page.getByTestId('public-watch-ended')).toHaveCount(0);

    // ---- The finished trips, gathered by the camp each was part of ----
    await page.getByTestId('public-trip-past-section').tap();
    const camp = page.getByTestId(`public-past-group-${CAMP.id}`);
    await expect(page.getByTestId(`public-past-group-${CAMP.id}-heading`)).toHaveText(
      `Camp: ${CAMP.name}`,
      { timeout: 20_000 },
    );
    await expect(camp.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible();
    await expect(camp.getByTestId(`public-past-trip-${LONE_TRIP}`)).toHaveCount(0);
    // The trips of no camp come last, under words of their own rather than under no heading.
    await expect(page.getByTestId('public-past-group-other-heading')).toHaveText(
      'Other trips of this cave',
    );
    await expect(
      page.getByTestId('public-past-group-other').getByTestId(`public-past-trip-${LONE_TRIP}`),
    ).toBeVisible();
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
  });

  test('a cave whose finished trips belong to no camp is one plain list, with no heading', async ({
    page,
  }) => {
    await serveCave(page);
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(page.getByTestId('public-trip-state-armed')).toBeVisible({ timeout: 30_000 });
    await page.getByTestId('public-trip-past-section').tap();
    await expect(page.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.public-past-group-heading')).toHaveCount(0);
  });

  test('inside the smallest frame a club may paste, another party is watched from the sheet and the strip says whose it is', async ({
    page,
  }) => {
    const served = await serveCave(page, { twoParties: true, camps: true });
    await page.setViewportSize(SMALLEST_FRAME);
    await page.goto(`/shared/trips/${TOKEN}/embed`);
    const frame = page.getByTestId('public-trip-embed');
    await expect(frame).toBeVisible({ timeout: 30_000 });
    expect(served.reads.filter((path) => path === served.liveList)).toEqual([]);

    await page.getByTestId('public-past-open').tap();
    const watch = page.getByTestId(`public-live-watch-${OTHER_TRIP}`);
    await expect(watch).toBeVisible({ timeout: 20_000 });
    await fingerSized(watch, 'Watch, in the frame’s sheet');
    // The same sheet carries the finished trips, under their camp.
    await expect(page.getByTestId(`public-past-group-${CAMP.id}-heading`)).toHaveText(
      `Camp: ${CAMP.name}`,
    );
    await watch.tap();

    // The sheet gets out of the way, and the frame's one line says whose party is on the drawing.
    await expect(watch).toBeHidden();
    await expect(page.getByTestId('public-watch-line')).toContainText(OTHER_TITLE, {
      timeout: 20_000,
    });
    await expect(frame).toHaveClass(/public-trip-embed-watch/);
    await expect(frame).not.toHaveClass(/public-trip-embed-past/);
    await fingerSized(page.getByTestId('public-watch-back'), 'the way back, in the frame');
    await fingerSized(page.getByTestId('public-past-open'), 'the way into the lists, while watching');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    await page.getByTestId('public-watch-back').tap();
    await expect(page.getByTestId('public-watch-line')).toHaveCount(0);
    await expect(frame).not.toHaveClass(/public-trip-embed-watch/);
    await expect(page.getByTestId('public-watch-ended')).toHaveCount(0);
  });
});
