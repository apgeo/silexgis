// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Locator, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson, versionOf } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A camp's head count on a phone: the three counts side by side and every person's standing on
 * the screen, with nothing reachable only by dragging the page sideways.
 *
 * The person answering for a camp's parties is as likely to be standing at a cave entrance with a
 * phone as sitting at the camp's laptop, and the section's layout is written for that: three
 * counts across even on the narrowest phone, so the number of people nobody has heard from is not
 * pushed below the other two, and a long name that breaks inside itself instead of pushing the
 * word beside it off the edge. Those are statements about measured boxes, which no test of the
 * component's markup can make. What the section lists and counts is proved on the desk project
 * and on the server; this proves only that it fits.
 *
 * Everything is stood up by this run and taken down after it. The survey is the committed
 * fixture; the people are invented.
 */

/** The narrowest phone the layout is written for; the project's own is a little wider. */
const NARROWEST = { width: 360, height: 740 };

/**
 * A name with one long unbroken stretch: the case that widens a row past the screen unless the
 * name is allowed to break inside itself.
 */
const longName = (stamp: number) => `E2E Speologulcunumelefoartelungfaraspatii-${stamp} Dan`;

/**
 * How far anything from this element up to the page itself holds more than its own width — what
 * could be dragged sideways, or is cut off where it cannot be.
 *
 * Asked of every box on the way up and not of the page alone: the application scrolls inside its
 * own frame, so a row wider than the screen widens that frame, or is clipped by a card, and the
 * page's own width never moves.
 */
const widestOverflowAround = (target: Locator) =>
  target.evaluate((element) => {
    let worst = 0;
    for (let box: Element | null = element; box; box = box.parentElement) {
      worst = Math.max(worst, box.scrollWidth - box.clientWidth);
    }
    return worst;
  });

async function boxOf(target: Locator) {
  const box = await target.boundingBox();
  expect(box, 'the element has a box on the page').not.toBeNull();
  return box!;
}

/** The layout's promises, asked of one card at whatever width the page currently has. */
async function expectItFits(page: Page, card: Locator, standings: Locator[]) {
  const width = page.viewportSize()!.width;

  // Three across: the counts share a line, in the order they are read, each wholly on screen.
  const counts = await Promise.all(
    ['underground', 'out', 'unheard'].map((which) =>
      boxOf(card.getByTestId(`expedition-watch-count-${which}`)),
    ),
  );
  expect(Math.abs(counts[0].y - counts[1].y), 'the first two counts share a line').toBeLessThan(2);
  expect(Math.abs(counts[1].y - counts[2].y), 'the last two counts share a line').toBeLessThan(2);
  expect(counts[0].x).toBeLessThan(counts[1].x);
  expect(counts[1].x).toBeLessThan(counts[2].x);
  for (const box of counts) {
    expect(box.x, 'a count starts on the screen').toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, 'a count ends on the screen').toBeLessThanOrEqual(width);
  }

  // Each count's caption is one line and on the screen — "Not heard from" is the longest, and
  // a caption that wrapped would put the three figures at three heights.
  const captions = card.locator('.expedition-watch-count-label');
  await expect(captions).toHaveCount(3);
  const captionBoxes = await Promise.all([0, 1, 2].map((index) => boxOf(captions.nth(index))));
  for (const box of captionBoxes) {
    expect(box.height, 'a caption is as tall as the shortest one').toBeLessThanOrEqual(
      Math.min(...captionBoxes.map((other) => other.height)) + 1,
    );
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, 'a caption ends on the screen').toBeLessThanOrEqual(width);
  }

  // Every person's standing is wholly on the screen, whatever the length of the name beside it.
  for (const standing of standings) {
    await expect(standing).toBeVisible();
    const box = await boxOf(standing);
    expect(box.x, 'a standing starts on the screen').toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, 'a standing ends on the screen').toBeLessThanOrEqual(width);
  }

  // And every name, which is what gives way: broken inside itself, never running off the edge.
  const names = card.locator('.expedition-watch-person-name');
  await expect(names).toHaveCount(standings.length);
  for (let index = 0; index < standings.length; index += 1) {
    const box = await boxOf(names.nth(index));
    expect(box.x, 'a name starts on the screen').toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, 'a name ends on the screen').toBeLessThanOrEqual(width);
  }

  expect(
    await widestOverflowAround(card),
    'what the card, or anything around it, holds beyond its own width',
  ).toBeLessThanOrEqual(1);
}

test('on a phone, a camp’s head count shows its three counts and every standing without sideways scrolling', async ({
  page,
}) => {
  // A survey the server has to read before a watch can be started on it.
  test.slow();
  const stamp = Date.now();
  const ana = `E2E Camp Phone Ana ${stamp}`;
  const bujor = `E2E Camp Phone Bujor ${stamp}`;
  const dan = longName(stamp);

  await login(page);
  let caveId: string | undefined;
  let modelId: string | undefined;
  let campId: string | undefined;
  let tripId: string | undefined;

  try {
    const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
    const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E Camp Phone Cave ${stamp}`,
      caveTypeId: caveTypes[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    });
    caveId = cave.id;

    const upload = await page.request.post(`/api/v1/caves/${cave.id}/survey-models`, {
      headers: { Authorization: `Bearer ${await bearerToken(page)}` },
      multipart: {
        file: {
          name: 'P8_Master.3d',
          mimeType: 'application/octet-stream',
          buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
        },
      },
    });
    expect(upload.ok(), `the survey upload answered ${upload.status()}`).toBeTruthy();
    const model = ((await upload.json()) as { id: string }).id;
    modelId = model;
    await expect
      .poll(
        async () =>
          (await asPerson<{ status: string }>(page, 'GET', `/api/v1/survey-models/${model}`)).status,
        { timeout: 90_000, message: 'the uploaded survey was never read' },
      )
      .toBe('ready');

    const camp = await asPerson<{ id: string }>(page, 'POST', '/api/v1/expeditions', {
      name: `E2E Camp Phone ${stamp}`,
      startDate: localDay(-1),
      endDate: localDay(5),
      visibility: 'authenticated',
    });
    campId = camp.id;

    const person = (name: string) => ({
      caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
    });
    const trip = await asPerson<{ id: string; participants: { caverId: string; name: string }[] }>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(`E2E Camp Phone Tracked ${stamp}`, localDay(0), {
        caveIds: [cave.id],
        participants: [person(ana), person(bujor), person(dan)],
      }),
    );
    tripId = trip.id;
    await asPerson(page, 'POST', `/api/v1/expeditions/${camp.id}/trips`, { tripLogId: trip.id });

    const watchPath = `/api/v1/trip-logs/${trip.id}/tracking`;
    const idOf = (name: string) => trip.participants.find((p) => p.name === name)!.caverId;
    await asPerson(
      page,
      'PUT',
      watchPath,
      { state: 'armed', surveyModelId: model, referenceStationName: null, depthFilter: [] },
      { 'If-Match': await versionOf(page, watchPath) },
    );
    const report = (name: string, kind: string) =>
      asPerson(page, 'POST', `${watchPath}/events`, {
        caverIds: [idOf(name)], kind, stationName: null, depthM: null, teamId: null, note: null,
        recordedAt: null,
      });
    // One of each standing; the person with the long name is the one nobody has heard from,
    // so the longest name sits beside the longest word.
    await report(ana, 'entered');
    await report(bujor, 'entered');
    await report(bujor, 'exited');

    await page.goto(`/expeditions/${camp.id}?tab=watch`);
    await page.waitForURL((url) => url.pathname === `/expeditions/${camp.id}`, { timeout: 60_000 });
    const card = page
      .getByTestId('expedition-watch-tab')
      .getByTestId(`expedition-watch-trip-${trip.id}`);
    await expect(card).toBeVisible({ timeout: 60_000 });
    await card.scrollIntoViewIfNeeded();

    // What is measured below is the count this party really has, not three empty boxes.
    await expect(card.getByTestId('expedition-watch-count-underground')).toHaveText('1');
    await expect(card.getByTestId('expedition-watch-count-out')).toHaveText('1');
    await expect(card.getByTestId('expedition-watch-count-unheard')).toHaveText('1');
    const standingOf = (name: string, standing: string) =>
      card
        .getByTestId(`expedition-watch-person-${idOf(name)}`)
        .getByTestId(`expedition-watch-standing-${standing}`);
    const standings = [
      standingOf(ana, 'underground'),
      standingOf(bujor, 'out'),
      standingOf(dan, 'unheard'),
    ];
    await expect(standings[0]).toHaveText('Underground');
    await expect(standings[1]).toHaveText('Out');
    await expect(standings[2]).toHaveText('Not heard from');
    await expect(card.getByTestId(`expedition-watch-person-${idOf(dan)}`)).toContainText(dan);

    // At the width of the project's phone, and again at the narrowest the layout names.
    await expectItFits(page, card, standings);
    await page.setViewportSize(NARROWEST);
    await expect(card).toBeVisible();
    await expectItFits(page, card, standings);
  } finally {
    // Off the camp before its rows go, so no page of this run reads a deleted record and files
    // the refusal with the console guard.
    try {
      await gotoRoute(page, '/expeditions');
    } catch {
      // The failure that brought the flow here is the one to report.
    }
    if (campId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/expeditions/${campId}`);
    }
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (modelId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/survey-models/${modelId}`);
    }
    if (caveId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
  }
});
