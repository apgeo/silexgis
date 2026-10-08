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
/**
 * The same frame in an article that keeps a sixteen-pixel margin each side of that screen — the
 * narrowest a pasted frame is in practice, since few articles run their content edge to edge.
 */
const NARROWER_FRAME = { width: 328, height: 260 };
/**
 * What the drawing keeps of a frame while a past trip is on screen: at least this many pixels, and
 * at least this share of the box.
 *
 * <b>Set after measuring, and the measurement is why the frame's strip is one line.</b> The strip
 * used to be the page's whole strip, capped at 55% of the frame and scrolling inside itself. Under
 * a finger, in the 260px frame, that came to: drawing 106px (41%), strip 154px showing of the
 * 298 to 436px it held, and the viewer's own row of controls 54px — drawn over the drawing, with
 * the party's legend and the compass beside it, so that the 106px showed no cave at all. The play
 * button stood 142px down a strip that showed 154.
 *
 * <b>The floor first proposed for this was lower — 96px and two fifths of the frame — and the old
 * strip met it</b>, at 106px and 40.8%. It is not the floor kept, for what those 106px were: a box
 * wholly under the viewer's own controls, with play out of reach. A floor that calls that room
 * measures the box and not the drawing.
 *
 * So the floor is not the complement of any cap. Half the frame, because below that the frame is
 * mostly its own chrome and has stopped being a drawing in somebody's article; and 150px, because
 * the viewer's controls reach about 100px down the box under a finger and what is left has to be
 * a band a passage and a marker can be told apart in — with room for the one line the frame adds
 * above its strip when its link stops answering.
 */
const DRAWING_FLOOR = { px: 150, share: 0.5 };
/**
 * The smallest frame that is given a past trip's whole strip rather than the one line — the
 * figures the application chooses its layout on, repeated here so that a change to either is a
 * change somebody made twice on purpose.
 *
 * <b>Set after measuring the whole strip under a finger, in the state it is tallest in</b>: a team
 * followed, the record cut short, and the link's own trip over as well, which is one more line of
 * the statement. In Romanian, whose sentences are the longer, it came to 419px in a frame 600px
 * wide, 375px at 640 and 353px from 720 up; in English 375, 353 and 353. The drawing is owed half
 * the frame, so the least box holding strip and drawing is 720 by 706. With a name twice as long
 * for the trip and for the team, and a trip of three days — the clock then names the day and the
 * transport takes a second row — the strip was 423px at 720 wide and 375 at 800.
 *
 * The frame first given the whole strip was 600 by 540, where it needs 347 to 419px of the 270
 * that half the box is: capped there, the strip scrolled inside itself and its rail was behind
 * the scroll. At 800 by 760 the tallest state leaves the drawing 407px, and the longer names 385.
 */
const ROOMY_FRAME = { width: 800, height: 760 };

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
  places: [],
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
const pastTrack = (truncated = false) => ({
  tripLogId: PAST_TRIP,
  expedition: null,
  title: PAST_TITLE,
  tripDate: '2019-07-06',
  tripDateEnd: null,
  armedAt: '2019-07-06T08:00:00Z',
  closedAt: '2019-07-06T18:00:00Z',
  positionsWithheld: false,
  trackTruncated: truncated,
  pictures: [],
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
  /** The 2019 trip reported more than one answer carries, and its record is cut short. */
  truncated?: boolean;
  /** The link's own trip is over: nobody is underground for a replay to go back to. */
  over?: boolean;
  /**
   * An old link: the list of parties in the cave now is refused for good, in the one answer every
   * refusal of a published link is given in, while the cave's past trips answer as before.
   */
  liveRefused?: boolean;
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
      await route.fulfill({
        json: cave.over
          ? {
              ...liveEnvelope(),
              state: 'closed',
              armedAt: minutesAgo(300),
              closedAt: minutesAgo(20),
            }
          : liveEnvelope(),
      });
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
      await route.fulfill({ json: pastTrack(cave.truncated) });
    } else if (path === `${base}/live` && cave.liveRefused) {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        json: { title: 'Not Found', status: 404, code: 'tracking.share_not_found' },
      });
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
  return { reads, ownTrip: base, liveList: `${base}/live`, pastList: `${base}/past` };
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
 * Waits for the frame's sheet to finish sliding in.
 *
 * The sheet arrives by a transition of about a third of a second, and its controls count as on
 * screen for all of it. A control measured meanwhile is measured where it is passing through: its
 * box comes back a hair under the height it has at rest (39.99998px for a 40px button, once). So
 * what is said of the sheet's controls is said of the sheet standing still — waited for by what the
 * sheet says of itself, never by the clock.
 */
async function sheetAtRest(page: Page) {
  await expect
    .poll(
      () =>
        page.evaluate(() => {
          const sheet = document.querySelector('.public-past-drawer .ant-drawer-content-wrapper');
          if (sheet === null) return 'absent';
          const moving = /-motion-/.test(sheet.className) || sheet.getAnimations().length > 0;
          return moving ? 'moving' : 'at rest';
        }),
      { timeout: 20_000 },
    )
    .toBe('at rest');
}

/**
 * How a frame's height is shared out while a past trip is on screen, in CSS pixels.
 *
 * Read in the page in one go, so the figures describe one layout rather than four moments of one.
 * `stripWhole` is the height of everything the strip holds, which is more than `strip` whenever
 * the strip is scrolling inside itself — the state the one-line strip exists to end; `toolbar` is
 * the viewer's own row of controls, which is drawn over the drawing rather than beside it and is
 * absent until the viewer has put it there.
 */
const roomInTheFrame = (page: Page) =>
  page.evaluate(() => {
    const named = (id: string) => document.querySelector(`[data-testid="${id}"]`);
    const tall = (element: Element | null) =>
      element === null ? null : Math.round(element.getBoundingClientRect().height);
    const strip = named('public-past-bar');
    const drawing = named('caveview-container');
    return {
      frame: tall(named('public-trip-embed')),
      drawing: tall(drawing),
      strip: tall(strip),
      stripWhole: strip === null ? null : strip.scrollHeight,
      toolbar: tall(drawing?.querySelector('.cv-toolbar') ?? null),
    };
  });

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

    // While the survey team is followed the two arrows step through its own reports. The replay
    // opened on the first of them, so there is none to step back to — though the trip itself has
    // two earlier reports, which is what the same arrow offers once nobody is followed.
    const previous = page.getByTestId('public-past-report-previous');
    const next = page.getByTestId('public-past-report-next');
    await expect(previous).toHaveAccessibleName('Previous report about Survey');
    await expect(previous).toBeDisabled();
    await expect(next).toHaveAccessibleName('Next report about Survey');
    await expect(next).toBeEnabled();
    // And forwards it passes over the other person's report, to the team's own last one: after
    // that press there is nothing further of the team's, where the trip's own next would have
    // left one more to go.
    await next.tap();
    await expect(next).toBeDisabled();
    await expect(previous).toBeEnabled();

    await page.getByTestId('public-past-unfollow').tap();
    await expect(page.getByTestId('public-past-banner-following')).toHaveCount(0);
    await expect(previous).toHaveAccessibleName('Previous report');
    await expect(next).toHaveAccessibleName('Next report');

    // The cave's other trips, one tap under the strip rather than a party and a drawing below it:
    // a target for a finger, shut until tapped, and no wider than the screen when it is open.
    const another = page.getByTestId('public-past-switch');
    await another.scrollIntoViewIfNeeded();
    await fingerSized(page.locator('.ant-collapse-header', { has: another }), 'another trip');
    await expect(page.getByTestId('public-past-switch-list')).toHaveCount(0);
    await another.tap();
    const rows = page.getByTestId('public-past-switch-list');
    await expect(rows.getByTestId(`public-past-trip-${PAST_TRIP}`)).toContainText('Playing', {
      timeout: 20_000,
    });
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
    // It stands between the strip and the drawing, which is the whole of the point.
    const order = await page.evaluate(() => {
      const strip = document.querySelector('[data-testid="public-past-bar"]');
      const list = document.querySelector('[data-testid="public-past-switch-list"]');
      const drawing = document.querySelector('[data-testid="caveview-container"]');
      if (strip === null || list === null || drawing === null) {
        return null;
      }
      return {
        afterStrip: (strip.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0,
        beforeDrawing:
          (list.compareDocumentPosition(drawing) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0,
      };
    });
    expect(order).toEqual({ afterStrip: true, beforeDrawing: true });
    await another.tap();
    await expect(page.getByTestId('public-past-switch-list')).toHaveCount(0);
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
    const served = await serveCave(page);
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

    // One line: that it is the past and which trip, the clock, play, the way back and the way to
    // the rest — every one of them on screen without scrolling anything, and sized for a finger.
    await expect(page.getByTestId('public-past-banner')).toContainText('Past trip');
    await expect(page.getByTestId('public-past-clock')).toBeInViewport();
    for (const [id, name] of [
      ['public-past-play', 'play, in the frame'],
      ['public-past-back', 'the way back, in the frame'],
      ['public-past-controls-open', 'the way to the rest of the controls, in the frame'],
    ] as const) {
      await expect(page.getByTestId(id), name).toBeInViewport();
      await fingerSized(page.getByTestId(id), name);
    }
    await expect(page.getByTestId('public-past-back')).toHaveAccessibleName('Back to the party now');
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
    // Nothing of the rest is on the line, and the sheet that holds it is shut.
    await expect(page.getByTestId('public-past-report-next')).toBeHidden();

    // Play answers the finger from the line.
    const play = page.getByTestId('public-past-play');
    const opened = await page.getByTestId('public-past-clock').textContent();
    await play.tap();
    await expect(play).toHaveAccessibleName('Pause');
    await expect
      .poll(async () => page.getByTestId('public-past-clock').textContent(), { timeout: 15_000 })
      .not.toBe(opened);
    await play.tap();
    await expect(play).toHaveAccessibleName('Play');

    // The rest, a press away: the rail and the steps between reports first, where the sheet shows
    // them without being scrolled.
    const readsBeforeTheControls = served.reads.length;
    await page.getByTestId('public-past-controls-open').tap();
    const handle = page.getByTestId('public-past-scrub').locator('.ant-slider-handle');
    const moment = () => handle.getAttribute('aria-valuenow');
    await expect(handle).toBeInViewport({ timeout: 20_000 });
    await sheetAtRest(page);
    await fingerSized(page.getByTestId('public-past-report-next'), 'the next report, in the sheet');
    await expect(page.getByTestId('public-past-report-next')).toBeInViewport();
    const standing = await moment();
    await page.getByTestId('public-past-report-next').tap();
    await expect.poll(moment).not.toBe(standing);
    // The rail under a thumb, inside a sheet that scrolls: it moves the clock, not the sheet.
    const stepped = await moment();
    await dragRail(page, 60);
    await expect.poll(moment, { timeout: 5_000 }).not.toBe(stepped);
    // The statement in full and whom to follow, in the same sheet.
    await page.getByTestId('public-past-follow').scrollIntoViewIfNeeded();
    await fingerSized(page.getByTestId('public-past-follow'), 'whom to follow, in the sheet');
    await expect(page.getByTestId('public-past-statement-what')).toContainText(PAST_TITLE);
    // Moving through the replay asked the server for nothing: the cave's two lists are not what
    // this reader opened the sheet for, and neither is drawn under the controls unasked.
    await expect(row).toHaveCount(0);
    expect(
      served.reads
        .slice(readsBeforeTheControls)
        .filter((path) => path === served.liveList || path === served.pastList),
    ).toEqual([]);
    // They are a press further, under the controls — so a reader in the past can pick another
    // trip without leaving this one first — and are read on that press.
    const lists = page.getByTestId('public-past-lists-open');
    await lists.scrollIntoViewIfNeeded();
    await fingerSized(lists, 'the way to the cave’s lists, in the sheet');
    await lists.tap();
    await expect(row).toHaveCount(1, { timeout: 20_000 });
    expect(
      served.reads.slice(readsBeforeTheControls).filter((path) => path === served.liveList).length,
    ).toBeGreaterThan(0);
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    // Shut, the sheet leaves the line as it was, at the moment the rail was left on.
    await page.getByTestId('public-past-drawer').locator('.ant-drawer-close').tap();
    await expect(page.getByTestId('public-past-report-next')).toBeHidden();
    await expect(play).toBeInViewport();
    await expect(page.getByTestId('public-past-clock')).not.toHaveText(opened ?? '');
  });

  for (const { frame, truncated } of [
    { frame: SMALLEST_FRAME, truncated: false },
    { frame: NARROWER_FRAME, truncated: false },
    // A record cut short adds a row to the line saying so — the tallest the line gets.
    { frame: SMALLEST_FRAME, truncated: true },
    { frame: NARROWER_FRAME, truncated: true },
  ]) {
    test(`a past trip${truncated ? ' whose record was cut short' : ''} playing in a frame of ${frame.width} by ${frame.height} leaves the drawing its share of the box`, async ({
      page,
    }) => {
      await serveCave(page, { truncated });
      await page.setViewportSize(frame);
      // Following a team as well: once a line of the strip with a control of its own in it, and
      // the state this was first measured in.
      await page.goto(`/shared/trips/${TOKEN}/embed?past=${PAST_TRIP}&team=${TEAM_SURVEY}`);
      // The clock and play are on the strip, so the strip is as tall as it gets; and the viewer
      // has drawn, so its own controls are where they will stay.
      await expect(page.getByTestId('public-past-clock')).toBeVisible({ timeout: 30_000 });
      await expect(page.getByTestId('caveview-container')).toBeVisible({ timeout: 30_000 });
      await expect(page.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 45_000 });
      await expect(page.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);

      const room = await roomInTheFrame(page);
      const said = `frame ${room.frame}px: drawing ${room.drawing}px, strip ${room.strip}px showing of ${room.stripWhole}px, viewer toolbar ${room.toolbar}px`;
      test.info().annotations.push({ type: 'measured', description: said });

      // The frame is the size this test says it is, or nothing below is about that size.
      expect(room.frame, said).toBe(frame.height);
      expect(room.drawing, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.px);
      expect(room.drawing! / room.frame!, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.share);
      // The strip took its height from the drawing and not from outside the box, and holds
      // nothing it is not showing: no control of a replay is behind a scroll inside the strip.
      expect(room.drawing! + room.strip!, said).toBeLessThanOrEqual(frame.height);
      expect(room.stripWhole, said).toBeLessThanOrEqual(room.strip! + 1);
      for (const id of [
        'public-past-banner',
        'public-past-play',
        'public-past-back',
        'public-past-controls-open',
      ]) {
        await expect(page.getByTestId(id), id).toBeInViewport();
      }
      // That the record ran out is said on the line itself, where a reader who only presses play
      // will see it; and not at all about a record that arrived whole.
      if (truncated) {
        await expect(page.getByTestId('public-past-truncated')).toBeInViewport();
        await expect(page.getByTestId('public-past-truncated')).toContainText(
          'The record ends before the trip did',
        );
      } else {
        await expect(page.getByTestId('public-past-truncated')).toHaveCount(0);
      }
      expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
    });
  }

  for (const { language, statement, nobody } of [
    {
      language: 'en',
      statement: 'You are looking at a past trip',
      nobody: 'no party underground to go back to',
    },
    {
      language: 'ro',
      statement: 'Aceasta este o tură trecută',
      nobody: 'nu există nicio echipă în peșteră',
    },
  ]) {
    test(`the smallest frame given a past trip’s whole strip shows all of it under a finger at its tallest, read in ${language}, and leaves the drawing its share`, async ({
      page,
    }) => {
      // The state the strip is tallest in: a finger's sizes, a team followed, a record cut short
      // and the link's own trip over — each of the last three a line of the statement — in the
      // smallest frame the application calls roomy, where the lines wrap the most.
      await serveCave(page, { truncated: true, over: true });
      await page.setViewportSize(ROOMY_FRAME);
      await page.goto(
        `/shared/trips/${TOKEN}/embed?lang=${language}&past=${PAST_TRIP}&team=${TEAM_SURVEY}`,
      );
      await expect(page.getByTestId('public-past-clock')).toBeVisible({ timeout: 30_000 });
      await expect(page.getByTestId('caveview-container')).toBeVisible({ timeout: 30_000 });
      await expect(page.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 45_000 });
      await expect(page.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);

      // It is that state, in that language, or nothing below is about the tallest strip.
      await expect(page.getByTestId('public-past-banner')).toContainText(statement);
      await expect(page.getByTestId('public-past-banner-following')).toContainText('Survey');
      await expect(page.getByTestId('public-past-no-live')).toContainText(nobody);

      // The whole strip and no way into a sheet of controls: every line of the statement, every
      // control of the transport and the rail under them, on screen at once.
      await expect(page.getByTestId('public-past-controls-open')).toHaveCount(0);
      for (const id of [
        'public-past-banner',
        'public-past-banner-what',
        'public-past-banner-following',
        'public-past-unfollow',
        'public-past-truncated',
        'public-past-no-live',
        'public-past-back',
        'public-past-lists-open',
        'public-past-play',
        'public-past-clock',
        'public-past-speed',
        'public-past-report-previous',
        'public-past-report-next',
        'public-past-follow',
        'public-past-scrub',
      ]) {
        // All of each, not a sliver of it: a rail half behind the strip's own scroll is on
        // screen by the letter. Short of one only by what a fraction of a pixel rounds to.
        await expect(page.getByTestId(id), id).toBeInViewport({ ratio: 0.99 });
      }
      await fingerSized(page.getByTestId('public-past-play'), 'play, on the whole strip');
      await fingerSized(page.getByTestId('public-past-back'), 'the way back, on the whole strip');
      await fingerSized(
        page.getByTestId('public-past-unfollow'),
        'stop following, on the whole strip',
      );

      const room = await roomInTheFrame(page);
      const said = `frame ${room.frame}px: drawing ${room.drawing}px, strip ${room.strip}px showing of ${room.stripWhole}px, viewer toolbar ${room.toolbar}px`;
      test.info().annotations.push({ type: 'measured', description: said });
      expect(room.frame, said).toBe(ROOMY_FRAME.height);
      expect(room.drawing, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.px);
      expect(room.drawing! / room.frame!, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.share);
      // The strip took its height from the drawing and not from outside the box, and none of it
      // is behind a scroll inside the strip.
      expect(room.drawing! + room.strip!, said).toBeLessThanOrEqual(ROOMY_FRAME.height);
      expect(room.stripWhole, said).toBeLessThanOrEqual(room.strip! + 1);
      expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

      // One pixel short of that room, either way, the same frame is the one line — and with the
      // pixel back it is the whole strip again.
      const controls = page.getByTestId('public-past-controls-open');
      const rail = page.getByTestId('public-past-scrub');
      await page.setViewportSize({ width: ROOMY_FRAME.width, height: ROOMY_FRAME.height - 1 });
      await expect(controls).toBeInViewport();
      await expect(rail).toBeHidden();
      await page.setViewportSize(ROOMY_FRAME);
      await expect(rail).toBeInViewport({ ratio: 0.99 });
      await expect(controls).toHaveCount(0);
      await page.setViewportSize({ width: ROOMY_FRAME.width - 1, height: ROOMY_FRAME.height });
      await expect(controls).toBeInViewport();
      await expect(rail).toBeHidden();
      // And the line it becomes leaves the drawing its share as well.
      const lineRoom = await roomInTheFrame(page);
      const lineSaid = `frame ${lineRoom.frame}px: drawing ${lineRoom.drawing}px, strip ${lineRoom.strip}px showing of ${lineRoom.stripWhole}px`;
      expect(lineRoom.drawing! / lineRoom.frame!, lineSaid).toBeGreaterThanOrEqual(
        DRAWING_FLOOR.share,
      );
      expect(lineRoom.stripWhole, lineSaid).toBeLessThanOrEqual(lineRoom.strip! + 1);
    });
  }

  test('a frame with room keeps a past trip’s whole strip on screen under a finger, and the drawing its share', async ({
    page,
  }) => {
    // A large tablet's article, or a tall box somebody chose for the frame: the smallest frame
    // that is given the whole strip, under a finger, with a team followed and a record cut short
    // — and a party underground, so the way back is the way back to it.
    const served = await serveCave(page, { truncated: true });
    await page.setViewportSize(ROOMY_FRAME);
    await page.goto(`/shared/trips/${TOKEN}/embed?past=${PAST_TRIP}&team=${TEAM_SURVEY}`);
    await expect(page.getByTestId('public-past-clock')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('caveview-container')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 45_000 });
    await expect(page.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);

    // Everything a reader moves a replay with is on screen at once, with no sheet opened for it:
    // the rail is dragged while looking at the drawing it moves.
    await expect(page.getByTestId('public-past-controls-open')).toHaveCount(0);
    for (const id of [
      'public-past-banner',
      'public-past-play',
      'public-past-back',
      'public-past-speed',
      'public-past-report-previous',
      'public-past-report-next',
      'public-past-follow',
      'public-past-scrub',
      'public-past-truncated',
      'public-past-lists-open',
    ]) {
      await expect(page.getByTestId(id), id).toBeInViewport();
    }
    await fingerSized(page.getByTestId('public-past-play'), 'play, on the whole strip');
    await fingerSized(page.getByTestId('public-past-report-next'), 'the next report, on the whole strip');

    const room = await roomInTheFrame(page);
    const said = `frame ${room.frame}px: drawing ${room.drawing}px, strip ${room.strip}px showing of ${room.stripWhole}px, viewer toolbar ${room.toolbar}px`;
    test.info().annotations.push({ type: 'measured', description: said });
    expect(room.frame, said).toBe(ROOMY_FRAME.height);
    expect(room.drawing, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.px);
    expect(room.drawing! / room.frame!, said).toBeGreaterThanOrEqual(DRAWING_FLOOR.share);
    // And none of it is behind a scroll inside the strip.
    expect(room.stripWhole, said).toBeLessThanOrEqual(room.strip! + 1);
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);

    // The rail under a thumb with the drawing in view, and no list read for it.
    const handle = page.getByTestId('public-past-scrub').locator('.ant-slider-handle');
    const moment = () => handle.getAttribute('aria-valuenow');
    const standing = await moment();
    await dragRail(page, 80);
    await expect.poll(moment, { timeout: 5_000 }).not.toBe(standing);
    expect(
      served.reads.filter((path) => path === served.liveList || path === served.pastList),
    ).toEqual([]);

    // The cave's lists are one press away, in the frame's sheet — which holds no second rail.
    await page.getByTestId('public-past-lists-open').tap();
    await expect(page.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('public-past-scrub')).toHaveCount(1);

    // One pixel short of that room, the same frame is the one line.
    await page.getByTestId('public-past-drawer').locator('.ant-drawer-close').tap();
    await page.setViewportSize({ width: ROOMY_FRAME.width, height: ROOMY_FRAME.height - 1 });
    await expect(page.getByTestId('public-past-controls-open')).toBeInViewport();
    await expect(page.getByTestId('public-past-scrub')).toBeHidden();
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

  test('an old link says it no longer lists who is in the cave today only once its past trips are in hand, and asks once', async ({
    page,
    consoleErrors,
  }) => {
    // Declared rather than left looking like a defect: the refusal IS the subject. It is the one
    // answer a published link is ever refused with, so the page has no reason to read — only
    // which of its two lists answered.
    consoleErrors.allow(
      /Failed to load resource.*404/,
      'the refusal of the list of parties, which this test serves on purpose',
    );
    const served = await serveCave(page, { over: true, liveRefused: true });
    const asked = (path: string) => served.reads.filter((read) => read === path).length;
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(page.getByTestId('public-trip-state-closed')).toBeVisible({ timeout: 30_000 });

    // ---- The parties first: refused, with nothing yet known about the past trips ----
    // The page has not read the cave's past trips and reads nothing to find out, so it cannot
    // tell an old link from a list that failed, and keeps the wording it had.
    await page.getByTestId('public-trip-live-section').tap();
    await expect(page.getByTestId('public-live-failed')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('public-live-past-only')).toHaveCount(0);
    expect(asked(served.pastList)).toBe(0);
    expect(asked(served.liveList)).toBe(1);

    // ---- The past trips answer: now it is known, and said as a fact about the link ----
    await page.getByTestId('public-trip-past-section').tap();
    await expect(page.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible({ timeout: 20_000 });
    const quiet = page.getByTestId('public-live-past-only');
    await expect(quiet).toBeVisible();
    await expect(quiet).toContainText('This link no longer lists who is in the cave today');
    await expect(quiet).toContainText('past trips are still here');
    // Nothing that promises to recover, nothing to press that could not succeed, and nothing
    // saying the link itself is over while its past trips are on the screen below.
    await expect(page.getByTestId('public-live-failed')).toHaveCount(0);
    await expect(page.getByTestId('public-live-link-ended')).toHaveCount(0);
    await expect(page.getByTestId('public-trip-ended')).toHaveCount(0);
    await expect(page.getByTestId('public-trip-stale')).toHaveCount(0);
    await expect(page.getByTestId('public-trip-retry')).toHaveCount(0);
    await expect(page.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible();
    // Learning it cost the one read the reader asked for, and the refused list was not asked for
    // again on the way: a refusal given for good is not tried twice.
    expect(asked(served.pastList)).toBe(1);
    expect(asked(served.liveList)).toBe(1);
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
  });

  test('inside the smallest frame a club may paste, an old link’s sheet says the same and still lists the past trips', async ({
    page,
    consoleErrors,
  }) => {
    consoleErrors.allow(
      /Failed to load resource.*404/,
      'the refusal of the list of parties, which this test serves on purpose',
    );
    const served = await serveCave(page, { over: true, liveRefused: true });
    await page.setViewportSize(SMALLEST_FRAME);
    await page.goto(`/shared/trips/${TOKEN}/embed`);
    await expect(page.getByTestId('public-trip-embed')).toBeVisible({ timeout: 30_000 });

    // The sheet asks for both lists on the one press, so here both halves of the evidence arrive
    // together: the past trips answered, the parties refused for good.
    await page.getByTestId('public-past-open').tap();
    await expect(page.getByTestId(`public-past-trip-${PAST_TRIP}`)).toBeVisible({ timeout: 20_000 });
    const quiet = page.getByTestId('public-live-past-only');
    await expect(quiet).toBeVisible({ timeout: 20_000 });
    await expect(quiet).toContainText('This link no longer lists who is in the cave today');
    await expect(page.getByTestId('public-live-failed')).toHaveCount(0);
    await expect(page.getByTestId('public-live-link-ended')).toHaveCount(0);
    await expect(page.getByTestId('public-trip-ended')).toHaveCount(0);
    expect(served.reads.filter((read) => read === served.liveList).length).toBe(1);
    expect(await noSidewaysScroll(page)).toBeLessThanOrEqual(1);
  });
});
