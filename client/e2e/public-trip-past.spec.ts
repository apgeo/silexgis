// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { CHOICE_KEY } from '../src/i18n/languageStorage.ts';
import { surveyRead, tryAsPerson } from './arrange.ts';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';
import {
  AT_FIRST_MOMENT,
  BEFORE_ANY,
  CAPTION,
  EXTRAS_TITLE,
  FIRST_MOMENT,
  NAMED_STATION,
  PLACE_NAME,
  extrasPage,
  servePublishedExtras,
} from './publishedReplayExtras.ts';
import { settledScreenshot } from './settled.ts';

/**
 * A cave's past trips, as somebody with no account reaches them.
 *
 * <b>Both trips are real.</b> A live one is stood up and published — that link is the page under
 * test, and the whole of what the visitor holds — and a second, earlier trip of the same cave is
 * tracked, reported, closed and published too. Nothing about the archive is faked: the list, the
 * track, the survey and the refusal all come off the server.
 *
 * <b>The reader is a fresh browser context with no storage at all</b>, because the claim under test
 * is that one link is the whole of an anonymous visitor's claim. That context is made through the
 * console guard and swept like any other; its errors are collected here as well and asserted
 * empty at the points where the page should be silent, which the guard's one verdict at the end
 * of the test cannot say — and a surface strangers read has nobody watching its console.
 *
 * <b>One thing is arranged rather than waited for: a closed trip stops being live only after the
 * installation's grace, which is two days by default.</b> The run is driven against an API started
 * with `SILEXGIS__TripTracking__ShareGraceAfterClose=00:00:00`, which is an ordinary operator
 * setting and not a test hook — with the default the spec would have to sleep for two days or reach
 * into the database and move a timestamp. The spec says so out loud and fails, rather than passing
 * vacuously, if the archive does not open.
 */

/** Two stations of the committed survey fixture: where the past party was reported. */
const STATION_A = 'p8.p8.98';
const STATION_B = 'p8.bens_dig.217';
/** The narrowest screen these surfaces are designed for. */
const NARROWEST = { width: 360, height: 740 };

const participant = (name: string) => ({
  caverId: null,
  newCaverName: name,
  roleId: null,
  entryTime: null,
  exitTime: null,
  note: null,
});

/** A trip of this cave with a roster, through the API this suite already speaks. */
async function makeTrip(
  page: Page,
  token: string,
  caveId: string,
  title: string,
  names: string[],
  tripDate: string,
) {
  return (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title,
    tripTypeId: null,
    tripDate,
    tripDateEnd: null,
    entryTime: null,
    exitTime: null,
    description: null,
    results: null,
    weatherConditions: null,
    locationText: null,
    organizingCavingGroupId: null,
    geom: null,
    caveIds: [caveId],
    participants: names.map(participant),
    proposers: null,
    cavingGroupId: null,
    visibility: null,
    depthReachedM: null,
    lengthSurveyedM: null,
    surveyStations: null,
    ropeMetres: null,
    hadIncident: false,
    fieldData: null,
    logistics: null,
    safety: null,
    maxParticipants: null,
    meetingGeom: null,
  })) as { id: string; participants: { caverId: string; name: string }[] };
}

/** Points a trip's watch at a survey and moves it between states, honouring the read's etag. */
async function setWatch(
  page: Page,
  token: string,
  tripId: string,
  body: Record<string, unknown>,
) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok()).toBeTruthy();
  const etag = read.headers()['etag'];
  return apiJson(
    page,
    token,
    'PUT',
    `/api/v1/trip-logs/${tripId}/tracking`,
    body,
    etag ? { 'If-Match': etag } : {},
  );
}

test('a visitor picks a past trip of this cave, plays it, and finds the way back', async ({
  page,
  browser,
  consoleErrors: guard,
}) => {
  // One visit from the first press to the link's end: the page, the replay, a second link, the
  // frame at four sizes, two refusals and a revocation — some thirteen hundred steps and close to
  // two minutes of them on a quiet machine. The suite's default bound is two minutes, so on a
  // busy one this flow was failed for its length, at whichever step the bound happened to fall.
  test.setTimeout(300_000);
  // The visitor's browser is watched like the signed-in one, and this flow refuses a track on
  // purpose and then revokes the link: each is answered 404, which the browser logs.
  guard.allow(
    /the server responded with a status of 404/,
    'this flow refuses one past track and then revokes the link, and each read that finds them gone is answered 404',
  );
  const stamp = Date.now();
  const caveName = `E2E Past Cave ${stamp}`;
  await login(page);

  // ---- The cave and its survey ----
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId).toBeTruthy();

  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadModal = page.getByRole('dialog');
  await expect(uploadModal.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const chooser = page.waitForEvent('filechooser');
  await uploadModal.locator('.ant-upload-drag').click();
  await (await chooser).setFiles('e2e/fixtures/P8_Master.3d');
  await uploadModal.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  const auth = await bearerToken(page);
  const models = (await apiJson(page, auth, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as {
    id: string;
  }[];
  const modelId = models[0].id;

  // ---- A later survey of the same cave, which the party underground now is followed on ----
  // The earlier trip keeps the survey it was made on, so every pick of the past and every way back
  // hands the viewer another file — what a visitor meets wherever a cave has been surveyed again
  // since. The burst further down depends on it: one file under a freshly signed address is not a
  // reason to build a viewer, and a burst between two addresses of one file would build none.
  const laterUpload = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${auth}` },
    multipart: {
      file: {
        name: 'P8_Master later.3d',
        mimeType: 'application/octet-stream',
        buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
      },
    },
  });
  expect(laterUpload.status(), await laterUpload.text()).toBe(201);
  const laterModelId = ((await laterUpload.json()) as { id: string }).id;
  await surveyRead(page, modelId);
  await surveyRead(page, laterModelId);

  // ---- The earlier trip: tracked, reported, closed, published ----
  const pastTitle = `E2E the morning push ${stamp}`;
  const today = new Date().toISOString().slice(0, 10);
  const past = await makeTrip(page, auth, caveId!, pastTitle, ['E2E Mircea', 'E2E Ileana'], today);
  const mircea = past.participants.find((row) => row.name === 'E2E Mircea')!.caverId;
  const ileana = past.participants.find((row) => row.name === 'E2E Ileana')!.caverId;

  await setWatch(page, auth, past.id, {
    state: 'armed',
    surveyModelId: modelId,
    referenceStationName: null,
    depthFilter: [],
  });
  const survey = (await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${past.id}/tracking/teams`, {
    title: 'Survey',
  })) as { id: string };
  const advance = (await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${past.id}/tracking/teams`, {
    title: 'Advance',
  })) as { id: string };

  const reportOn = (
    tripId: string,
    caverIds: string[],
    kind: string,
    stationName: string | null = null,
    teamId: string | null = null,
    recordedAt: string | null = null,
  ) =>
    apiJson(page, auth, 'POST', `/api/v1/trip-logs/${tripId}/tracking/events`, {
      caverIds,
      kind,
      stationName,
      depthM: null,
      teamId,
      note: null,
      recordedAt,
    });

  // A track with a shape: both go in, then each team is reported at its own station, hours
  // apart, so the replay has something to move through rather than one instant. Measured back
  // from now, because the watch is armed now — a log stamped weeks before the watch it belongs to
  // makes a replay whose whole trip sits on the left-hand pixel of the rail.
  const hoursAgo = (hours: number) =>
    new Date(Date.now() - hours * 3_600_000).toISOString();
  await reportOn(past.id, [mircea, ileana], 'entered', null, null, hoursAgo(8));
  await reportOn(past.id, [mircea], 'atStation', STATION_A, advance.id, hoursAgo(6));
  await reportOn(past.id, [ileana], 'atStation', STATION_B, survey.id, hoursAgo(4));
  await reportOn(past.id, [mircea, ileana], 'exited', null, null, hoursAgo(1));

  const pastShare = (await apiJson(
    page,
    auth,
    'POST',
    `/api/v1/trip-logs/${past.id}/tracking/shares`,
  )) as { id: string; token: string };
  expect(pastShare.token).toBeTruthy();
  await setWatch(page, auth, past.id, { state: 'closed' });

  // ---- Today's trip: the link the visitor actually holds ----
  const liveTitle = `E2E today ${stamp}`;
  const live = await makeTrip(
    page,
    auth,
    caveId!,
    liveTitle,
    ['E2E Carmen'],
    new Date().toISOString().slice(0, 10),
  );
  const carmen = live.participants[0].caverId;
  await setWatch(page, auth, live.id, {
    state: 'armed',
    surveyModelId: laterModelId,
    referenceStationName: null,
    depthFilter: [],
  });
  await reportOn(live.id, [carmen], 'entered');
  await reportOn(live.id, [carmen], 'atStation', STATION_A);
  const liveShare = (await apiJson(
    page,
    auth,
    'POST',
    `/api/v1/trip-logs/${live.id}/tracking/shares`,
  )) as { id: string; token: string };

  // ---- The visitor: a fresh context holding nothing but the link ----
  // Made the way the fixture's context is — reading English, and watched — though it holds no
  // session at all.
  const anonymous = await ownContext(browser);
  try {
    const pub = await anonymous.newPage();
    const consoleErrors: string[] = [];
    pub.on('pageerror', (error) => consoleErrors.push(String(error)));
    pub.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    // Every archive read this browser makes, counted: the claim is that a visitor who never asks
    // for the past pays nothing for it.
    const archiveReads: string[] = [];
    pub.on('request', (request) => {
      if (/\/api\/v1\/public\/trips\/[^/]+\/past/.test(request.url())) {
        archiveReads.push(request.url());
      }
    });

    await pub.goto(`/shared/trips/${liveShare.token}`);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(liveTitle, { timeout: 30_000 });
    await expect(pub.getByTestId('public-trip-state-armed')).toBeVisible();
    expect(archiveReads).toEqual([]);

    // ---- The picker ----
    await pub.getByText('Past trips in this cave').click();
    const row = pub.getByRole('button', { name: new RegExp(pastTitle) });
    await expect(
      row,
      'the archive did not open: start the API with SILEXGIS__TripTracking__ShareGraceAfterClose=00:00:00, '
        + 'or a trip closed a moment ago is still inside the installation’s live window',
    ).toBeVisible({ timeout: 20_000 });
    expect(archiveReads.length).toBeGreaterThan(0);
    // Only the list so far: a track is the heavy answer and is not read until a trip is chosen.
    expect(archiveReads.filter((url) => /\/past\/[0-9a-f-]+/.test(url))).toEqual([]);

    // ---- Playing it ----
    await row.click();
    await expect(pub.getByTestId('public-past-banner')).toContainText(
      'You are looking at a past trip',
      { timeout: 20_000 },
    );
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle);
    // The party on screen is the past one, drawn through the same page as the live one.
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Mircea');
    await expect(pub.getByTestId('public-trip-party')).not.toContainText('E2E Carmen');
    await expect(pub.getByTestId('public-trip-title')).toHaveText(pastTitle);
    // And it is the very replay machinery the coordinator's surface uses: a clock, a rail, speeds.
    await expect(pub.getByTestId('public-past-scrub')).toBeVisible();
    await expect(pub.getByTestId('public-past-clock')).toBeVisible();

    // Playing moves the clock without anybody touching the rail.
    const opened = await pub.getByTestId('public-past-clock').textContent();
    await pub.getByTestId('public-past-play').click();
    await expect
      .poll(async () => pub.getByTestId('public-past-clock').textContent(), { timeout: 15_000 })
      .not.toBe(opened);
    await pub.getByTestId('public-past-play').click();

    // Stepping between the reports themselves, which is what a reader scrubbing a trip is hunting
    // for: the rail stops at a thousand places and only four of them are moments anybody spoke.
    await pub.getByTestId('public-past-report-previous').click();
    await pub.getByTestId('public-past-report-next').click();
    await pub.getByTestId('public-past-report-next').click();
    await pub.getByTestId('public-past-report-next').click();
    // By the third report the advance team has been placed, and the page says where — which is the
    // whole claim: a replay draws the party through the same honesty rules the live page uses.
    await expect(pub.getByTestId('public-trip-party')).toContainText(STATION_A);

    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/60-past-page-desktop.png`);
    }

    // ---- At 360px, where this page is actually read ----
    await pub.setViewportSize(NARROWEST);
    await expect(pub.getByTestId('public-past-banner')).toBeVisible();
    await expect(pub.getByTestId('public-past-back')).toBeVisible();
    const backBox = (await pub.getByTestId('public-past-back').boundingBox())!;
    expect(backBox.height).toBeGreaterThanOrEqual(24);
    // Nothing is pushed off the side of a 360px screen.
    const overflow = await pub.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(1);
    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/61-past-page-360.png`, { fullPage: true });
    }
    // A row pressed at the bottom of a phone page is answered at the top of it. The picker sits
    // under the whole party; without the page bringing the strip into view, the only visible
    // change after the press was the row's own tag, and the statement that this is the past, the
    // way back and the transport all mounted out of sight above.
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await row.click();
    await expect(pub.getByTestId('public-past-banner')).toBeInViewport({ timeout: 20_000 });
    await expect(pub.getByTestId('public-past-back')).toBeInViewport();
    await pub.setViewportSize({ width: 1280, height: 900 });

    // ---- The way back, while there is a party to go back to ----
    await expect(pub.getByTestId('public-past-back')).toHaveText('Back to the party now');
    await expect(pub.getByTestId('public-past-no-live')).toHaveCount(0);
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');

    // ---- The browser's own Back button ----
    // What somebody presses on finding themselves in a trip of another day. Picking a trip is a
    // step in the page's history, so Back undoes it and stays on the page — it used to replace
    // the entry the reader was on, and Back then left the published trip altogether.
    const here = `/shared/trips/${liveShare.token}`;
    await row.click();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 20_000,
    });
    expect(new URL(pub.url()).searchParams.get('past')).toBe(past.id);
    // Pressed as soon as the replay says which trip it is, while the browser is still parsing
    // that trip's survey — the moment a reader who mis-tapped presses it, and the one in which
    // the page once had not yet heard of its own pick and so saw nothing to go back from.
    await pub.goBack();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0, { timeout: 20_000 });
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');
    await expect(pub.getByTestId('public-trip-title')).toHaveText(liveTitle);
    expect(new URL(pub.url()).pathname).toBe(here);
    expect(new URL(pub.url()).searchParams.has('past')).toBe(false);
    // And Forward is the pick again, read from the address like any link into the past.
    await pub.goForward();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 20_000,
    });
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);

    // ---- A second link of the same cave, in the same tab ----
    // Two links of one cave answer for the same past trips, so nothing but the page keeps one
    // link's replay from standing under the other. The move between them is made inside the
    // document — which is how the browser's Back and Forward move between two links once both
    // are in a tab's history, and how a link on a club's own page moves — because a fresh load
    // starts with nothing held and would pass whether or not the page forgot anything.
    const secondShare = (await apiJson(
      page,
      auth,
      'POST',
      `/api/v1/trip-logs/${live.id}/tracking/shares`,
    )) as { id: string; token: string };
    expect(secondShare.token).not.toBe(liveShare.token);
    const there = `/shared/trips/${secondShare.token}`;
    // What the second link is asked for about the past, counted: it names no past trip, so it is
    // asked for none.
    const askedOfSecond: string[] = [];
    pub.on('request', (request) => {
      if (request.url().includes(`/public/trips/${secondShare.token}/past/`)) {
        askedOfSecond.push(request.url());
      }
    });

    await row.click();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 20_000,
    });
    await pub.evaluate((to) => {
      window.history.pushState(null, '', to);
      window.dispatchEvent(new PopStateEvent('popstate'));
    }, there);
    // The second link's own party, with no replay over it and nothing of one in its address.
    await expect(pub.getByTestId('public-trip-title')).toHaveText(liveTitle, { timeout: 30_000 });
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');
    await expect(pub.getByTestId('public-trip-party')).not.toContainText('E2E Mircea');
    expect(new URL(pub.url()).pathname).toBe(there);
    expect(new URL(pub.url()).searchParams.has('past')).toBe(false);
    expect(askedOfSecond).toEqual([]);

    // Back is the first link again, in the replay its own address names —
    await pub.goBack();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 20_000,
    });
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Mircea');
    expect(new URL(pub.url()).pathname).toBe(here);
    expect(new URL(pub.url()).searchParams.get('past')).toBe(past.id);
    // — and Forward is the second link, still on its party now: the replay did not follow the
    // reader across.
    await pub.goForward();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0, { timeout: 20_000 });
    await expect(pub.getByTestId('public-trip-title')).toHaveText(liveTitle);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');
    expect(new URL(pub.url()).pathname).toBe(there);
    expect(askedOfSecond).toEqual([]);

    // The rest of this visit is the first link's.
    await pub.goBack();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 20_000,
    });
    // ---- Another trip of the cave, from under the strip ----
    // Shut until pressed; opened, it holds the cave's past trips beside the replay, with the one
    // on screen marked as playing.
    await expect(pub.getByTestId('public-past-switch-list')).toHaveCount(0);
    await pub.getByTestId('public-past-switch').click();
    await expect(
      pub.getByTestId('public-past-switch-list').getByTestId(`public-past-trip-${past.id}`),
    ).toContainText('Playing');
    await pub.getByTestId('public-past-switch').click();
    await expect(pub.getByTestId('public-past-switch-list')).toHaveCount(0);
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    expect(new URL(pub.url()).pathname).toBe(here);

    // ---- The page's language, at the desk ----
    // The button at the foot of the wide layout. It changes what the page says and what the
    // document tells the browser, writes the language into the address in place of the entry it
    // is on, and leaves the signed-in application's own record of a language — English, in this
    // suite's browsers — exactly as it was.
    const appLanguage = () =>
      pub.evaluate((key) => window.localStorage.getItem(key), CHOICE_KEY);
    const languageButton = pub.getByTestId('public-trip-language');
    await expect(languageButton).toHaveText('Română');
    await languageButton.click();
    await expect(pub.locator('html')).toHaveAttribute('lang', 'ro');
    await expect(pub.getByTestId('public-trip-state-armed')).toHaveText('În peșteră acum');
    expect(new URL(pub.url()).searchParams.get('lang')).toBe('ro');
    expect(await appLanguage()).toBe('en');
    await expect(languageButton).toHaveText('English');
    await languageButton.click();
    await expect(pub.locator('html')).toHaveAttribute('lang', 'en');
    await expect(pub.getByTestId('public-trip-state-armed')).toHaveText('Underground now');

    // ---- A link that names a moment, and the buttons that write one ----
    await anonymous.grantPermissions(['clipboard-read', 'clipboard-write']);
    // Inside the trip's own stretch, between two of its reports, and to the second — which is
    // how the page itself writes one.
    const moment = new Date(Math.floor((Date.now() - 5 * 3_600_000) / 1000) * 1000)
      .toISOString()
      .replace('.000Z', 'Z');
    const playButton = pub.getByTestId('public-past-play');
    // The twin first: the same moment without the word opens standing still.
    await pub.goto(`${here}?past=${past.id}&at=${moment}`);
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 30_000,
    });
    await expect(playButton).toHaveAccessibleName('Play');
    const standing = await pub.getByTestId('public-past-clock').textContent();
    expect(standing).not.toBe('');
    // With the word, nobody presses anything and the clock leaves the moment it opened at.
    await pub.goto(`${here}?past=${past.id}&at=${moment}&play=1`);
    await expect(playButton).toHaveAccessibleName('Pause', { timeout: 30_000 });
    await expect
      .poll(async () => pub.getByTestId('public-past-clock').textContent(), { timeout: 15_000 })
      .not.toBe(standing);
    await playButton.click();
    await expect(playButton).toHaveAccessibleName('Play');
    // The copy row at the wide layout: this moment, standing still.
    const paused = await pub.getByTestId('public-past-clock').textContent();
    await pub.getByTestId('public-past-copy-moment').click();
    await expect(pub.getByTestId('public-past-copied')).toContainText('Link copied');
    await expect(pub.getByTestId('public-past-copied')).toContainText(paused ?? 'no clock');
    const copied = new URL(await pub.evaluate(() => navigator.clipboard.readText()));
    expect(copied.pathname).toBe(here);
    expect(copied.searchParams.get('past')).toBe(past.id);
    expect(copied.searchParams.get('at')).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/);
    expect(copied.searchParams.has('play')).toBe(false);
    // The address bar was given no moment by the press: only the copied link carries one.
    expect(new URL(pub.url()).searchParams.get('at')).toBe(moment);
    // Left as the next part expects to find it: the party now, and the cave's list open.
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');
    await expect(row).toBeVisible({ timeout: 20_000 });

    // ---- Back and forth faster than a drawing loads ----
    // Every pick and every way back points the viewer at another survey file, and the one being
    // taken off the screen may still be parsing. At reading speed, as above, nothing showed;
    // pressed 150 ms apart, the disposed viewer's own progress dial went on asking it to draw
    // against a renderer that was gone, and this page threw an uncaught TypeError on each press —
    // on the one surface strangers read with nobody watching its console. Swept by the error
    // lists this test ends on.
    //
    // Each press waits only until a viewer for the drawing it asked for exists, and the next press
    // takes that viewer down again — while it parses, or before the ask its dial makes half a
    // second after. Sooner than that, the panel is re-pointed before it has built a viewer at all
    // and the burst exercises nothing. The page's clock is in the test's hands for the burst, so
    // that last ask can be made on demand rather than waited for.
    const container = pub.getByTestId('caveview-container');
    const pressForANewViewer = async (press: () => Promise<void>) => {
      await container.locator('canvas').evaluateAll((canvases) => {
        for (const canvas of canvases) canvas.dataset.seenBefore = '';
      });
      await press();
      await expect(container.locator('canvas:not([data-seen-before])').first()).toBeAttached({
        timeout: 20_000,
      });
    };
    await pub.clock.install();
    for (let round = 0; round < 3; round += 1) {
      if (!(await row.isVisible())) {
        await pub.getByText('Past trips in this cave').click();
      }
      await pressForANewViewer(() => row.click());
      await pressForANewViewer(() => pub.getByTestId('public-past-back').click());
    }
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');
    // The drawing asked for last has loaded, and the ones before it were handed over earlier, so
    // every load of the burst has ended. Each has one ask left, half a second after its end; this
    // makes them now.
    await expect(pub.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
    await pub.clock.runFor(1_000);

    // ---- A hyperlink into the past, opened cold ----
    await pub.goto(`/shared/trips/${liveShare.token}?past=${past.id}&team=${survey.id}`);
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 30_000,
    });
    await expect(pub.getByTestId('public-past-banner-following')).toContainText('Survey');
    // The followed team's own station is what the page is showing, so the person in it is placed.
    await expect(pub.getByTestId('public-trip-party')).toContainText(STATION_B);

    // ---- The same archive inside the frame a club pastes ----
    await pub.goto(`/shared/trips/${liveShare.token}/embed`);
    await expect(pub.getByTestId('public-trip-embed')).toBeVisible({ timeout: 30_000 });
    await pub.getByTestId('public-past-open').click();
    const embedRow = pub.getByRole('button', { name: new RegExp(pastTitle) });
    await expect(embedRow).toBeVisible({ timeout: 20_000 });
    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/64-past-embed-picker.png`);
    }
    await embedRow.click();
    // A frame this size has room for the whole strip: the statement that it is the past, and the
    // trip's name under it.
    await expect(pub.getByTestId('public-past-banner')).toContainText(
      'You are looking at a past trip',
      { timeout: 20_000 },
    );
    await expect(pub.getByTestId('public-past-banner')).toContainText(pastTitle);
    // The frame itself is marked, which costs no height in a box a club chose the size of.
    await expect(pub.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);
    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/62-past-embed.png`);
    }

    // The frame as a wide desktop article holds it — the snippet's own 4:3 box in a column
    // 1024px wide. The rail, the speed, the steps and whom to follow are on screen beside the
    // drawing, with no sheet opened for them, and the drawing keeps well over half the box.
    await pub.setViewportSize({ width: 1024, height: 768 });
    await expect(pub.getByTestId('public-past-controls-open')).toHaveCount(0);
    for (const id of [
      'public-past-play',
      'public-past-back',
      'public-past-speed',
      'public-past-report-next',
      'public-past-follow',
      'public-past-scrub',
    ]) {
      await expect(pub.getByTestId(id), id).toBeInViewport();
    }
    const deskRoom = await pub.evaluate(() => {
      const tall = (id: string) =>
        Math.round(
          document.querySelector(`[data-testid="${id}"]`)?.getBoundingClientRect().height ?? -1,
        );
      const strip = document.querySelector('[data-testid="public-past-bar"]');
      return {
        frame: tall('public-trip-embed'),
        drawing: tall('caveview-container'),
        strip: tall('public-past-bar'),
        stripWhole: strip?.scrollHeight ?? -1,
      };
    });
    const deskSaid = `frame ${deskRoom.frame}px: drawing ${deskRoom.drawing}px, strip ${deskRoom.strip}px showing of ${deskRoom.stripWhole}px`;
    test.info().annotations.push({ type: 'measured', description: deskSaid });
    expect(deskRoom.frame, deskSaid).toBe(768);
    expect(deskRoom.drawing / deskRoom.frame, deskSaid).toBeGreaterThanOrEqual(0.5);
    expect(deskRoom.stripWhole, deskSaid).toBeLessThanOrEqual(deskRoom.strip + 1);

    // The same box in an ordinary article's column, 760px wide, is not that room: the whole strip
    // with all it can have to say would be more than half of it. So it is the one line, with the
    // rest of the transport a press away, and the drawing keeps most of the box.
    await pub.setViewportSize({ width: 760, height: 570 });
    await expect(pub.getByTestId('public-past-controls-open')).toBeInViewport();
    await expect(pub.getByTestId('public-past-play')).toBeInViewport();
    await expect(pub.getByTestId('public-past-banner')).toContainText('Past trip');
    await expect(pub.getByTestId('public-past-scrub')).toBeHidden();
    const columnRoom = await pub.evaluate(() => {
      const tall = (id: string) =>
        Math.round(
          document.querySelector(`[data-testid="${id}"]`)?.getBoundingClientRect().height ?? -1,
        );
      return { frame: tall('public-trip-embed'), drawing: tall('caveview-container') };
    });
    expect(columnRoom.frame).toBe(570);
    expect(columnRoom.drawing / columnRoom.frame).toBeGreaterThanOrEqual(0.5);

    // The frame at the width a club's article is actually read at on a phone: one line, a tag
    // that says it is the past and the trip's name beside it. The drawing keeps the room the
    // snippet gave it and the strip keeps its own; nothing runs off the side.
    await pub.setViewportSize(NARROWEST);
    await expect(pub.getByTestId('public-past-banner')).toBeVisible();
    await expect(pub.getByTestId('public-past-banner')).toContainText('Past trip');
    await expect(pub.getByTestId('public-past-controls-open')).toBeVisible();
    const framedOverflow = await pub.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(framedOverflow).toBeLessThanOrEqual(1);
    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/65-past-embed-360.png`);
    }
    await pub.setViewportSize({ width: 1280, height: 900 });

    // ---- And when there is no party to go back to ----
    //
    // The live envelope alone is answered as a finished trip; everything about the archive stays
    // real. Arranged rather than driven because the server cannot be in both states at once: a
    // trip whose watch is closed, on an installation configured with no grace, answers nothing at
    // all — and the page would then be the refusal rather than the branch under test.
    await pub.route(
      (url) => /\/api\/v1\/public\/trips\/[^/]+$/.test(url.pathname),
      async (route) => {
        const answer = await route.fetch();
        if (!answer.ok()) {
          // A refusal is passed through untouched: dressing one up as a closed trip would hide
          // the very answer the revocation below is about to assert.
          await route.fulfill({ response: answer });
          return;
        }
        const body = (await answer.json()) as Record<string, unknown>;
        await route.fulfill({
          json: { ...body, state: 'closed', closedAt: new Date().toISOString() },
        });
      },
    );
    await pub.goto(`/shared/trips/${liveShare.token}?past=${past.id}`);
    await expect(pub.getByTestId('public-past-banner')).toBeVisible({ timeout: 30_000 });
    await expect(pub.getByTestId('public-past-no-live')).toContainText(
      'no party underground to go back to',
    );
    await expect(pub.getByTestId('public-past-back')).toHaveText("Back to this link's trip");
    if (process.env.PAST_SHOTS) {
      await settledScreenshot(pub, `${process.env.PAST_SHOTS}/63-past-no-live-party.png`);
    }
    // By predicate identity `unroute` would not match the arrow above, so the whole table goes.
    await pub.unrouteAll();

    // The anonymous surface ran clean to here — asserted before the refusals below, because each
    // of them is a page built around a 404 the browser legitimately logs.
    expect(consoleErrors).toEqual([]);

    // ---- A trip the archive offers and the reader cannot be given ----
    //
    // Past this installation's retention, withdrawn, or simply gone. Driven by refusing the track
    // and nothing else: the list, the live envelope and the survey all stay real, so what is under
    // test is the state the page is left in rather than a page that failed to load.
    const refuseTrack = (url: URL) =>
      /\/api\/v1\/public\/trips\/[^/]+\/past\/[0-9a-f-]+$/.test(url.pathname);
    await pub.route(refuseTrack, (route) =>
      route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }),
    );

    await pub.goto(`/shared/trips/${liveShare.token}?past=${past.id}`);
    await expect(pub.getByTestId('public-past-track-failed')).toBeVisible({ timeout: 30_000 });
    // The header must not go on naming the live trip under a banner saying this is the past, and
    // least of all with its blue "underground now" beside it: one page saying a party is
    // underground and that this is the past, in a single glance, and permanently.
    await expect(pub.getByTestId('public-trip-title')).not.toHaveText(liveTitle);
    await expect(pub.getByTestId('public-trip-state-armed')).toHaveCount(0);

    // And inside somebody's article the same refusal must not turn into a claim about a drawing.
    await pub.goto(`/shared/trips/${liveShare.token}/embed`);
    await expect(pub.getByTestId('public-trip-embed')).toBeVisible({ timeout: 30_000 });
    await pub.getByTestId('public-past-open').click();
    const refusedRow = pub.getByRole('button', { name: new RegExp(pastTitle) });
    await expect(refusedRow).toBeVisible({ timeout: 20_000 });
    await refusedRow.click();
    await expect(pub.getByTestId('public-past-track-failed')).toBeVisible({ timeout: 20_000 });
    await expect(pub.getByText('There is no survey drawing to show for this trip.')).toHaveCount(0);
    // The frame is still marked as the past, which is the signal costing no height at all.
    await expect(pub.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);
    await pub.unrouteAll();

    // ---- Revoked, the link closes, archive and all ----
    await apiJson(
      page,
      auth,
      'DELETE',
      `/api/v1/trip-logs/${live.id}/tracking/shares/${liveShare.id}`,
    );
    await pub.goto(`/shared/trips/${liveShare.token}?past=${past.id}`);
    await expect(pub.getByTestId('public-trip-not-found')).toBeVisible({ timeout: 20_000 });
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);

    expect(
      consoleErrors.filter((line) => !/the server responded with a status of 404/.test(line)),
    ).toEqual([]);
  } finally {
    await anonymous.close();
  }
});

test('a visitor watches another party of the cave on the survey of the link they hold, and is told when that party leaves the list', async ({
  page,
  browser,
}) => {
  test.setTimeout(300_000);
  const stamp = Date.now();
  const caveName = `E2E Two Parties Cave ${stamp}`;
  await login(page);
  const made: { trips: string[]; caves: string[] } = { trips: [], caves: [] };

  const anonymous = await ownContext(browser);
  try {
    // ---- One cave, one survey, and two parties in it at once, each followed and published ----
    await page.goto('/caves/new');
    await page.getByLabel('Name', { exact: true }).fill(caveName);
    await page.getByLabel('Type', { exact: true }).click();
    await page.locator('.ant-select-item-option').first().click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
    const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
    expect(caveId, 'the cave page names the cave in its address').toBeTruthy();
    made.caves.push(caveId!);

    const auth = await bearerToken(page);
    const uploaded = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
      headers: { Authorization: `Bearer ${auth}` },
      multipart: {
        file: {
          name: 'P8_Master.3d',
          mimeType: 'application/octet-stream',
          buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
        },
      },
    });
    expect(uploaded.status(), await uploaded.text()).toBe(201);
    const modelId = ((await uploaded.json()) as { id: string }).id;
    await surveyRead(page, modelId);

    const today = new Date().toISOString().slice(0, 10);
    const report = (
      tripId: string,
      caverIds: string[],
      kind: string,
      stationName: string | null = null,
    ) =>
      apiJson(page, auth, 'POST', `/api/v1/trip-logs/${tripId}/tracking/events`, {
        caverIds,
        kind,
        stationName,
        depthM: null,
        teamId: null,
        note: null,
        recordedAt: null,
      });
    const followed = async (title: string, names: string[]) => {
      const trip = await makeTrip(page, auth, caveId!, title, names, today);
      made.trips.push(trip.id);
      await setWatch(page, auth, trip.id, {
        state: 'armed',
        surveyModelId: modelId,
        referenceStationName: null,
        depthFilter: [],
      });
      const share = (await apiJson(
        page,
        auth,
        'POST',
        `/api/v1/trip-logs/${trip.id}/tracking/shares`,
      )) as { id: string; token: string };
      expect(share.token).toBeTruthy();
      const caver = (name: string) => trip.participants.find((row) => row.name === name)!.caverId;
      return { id: trip.id, token: share.token, caver };
    };

    // The party of the link the visitor holds: one person, at one station.
    const ownTitle = `E2E the family's party ${stamp}`;
    const own = await followed(ownTitle, ['E2E Carmen']);
    await report(own.id, [own.caver('E2E Carmen')], 'entered');
    await report(own.id, [own.caver('E2E Carmen')], 'atStation', STATION_A);

    // The cave's other party: one person placed elsewhere on the same survey, one already out.
    const otherTitle = `E2E the survey party ${stamp}`;
    const other = await followed(otherTitle, ['E2E Sorin', 'E2E Dana']);
    await report(other.id, [other.caver('E2E Sorin'), other.caver('E2E Dana')], 'entered');
    await report(other.id, [other.caver('E2E Sorin')], 'atStation', STATION_B);
    await report(other.id, [other.caver('E2E Dana')], 'exited');

    // ---- The visitor: a browser holding one link, and a clock that can be moved on ----
    const pub = await anonymous.newPage();
    // Before the page exists: a timer the page set on the real clock could not be moved on later.
    await pub.clock.install();
    /** Every read of the published surface this browser makes, by path. */
    const publicReads: string[] = [];
    pub.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/v1/public/trips/')) publicReads.push(path);
    });
    const ownBase = `/api/v1/public/trips/${own.token}`;

    await pub.goto(`/shared/trips/${own.token}`);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(ownTitle, { timeout: 30_000 });
    const party = pub.getByTestId('public-trip-party');
    await expect(party).toContainText('E2E Carmen');
    // The drawing has been built before anything is pressed, so that "no new drawing" below is a
    // statement about the press and not about a viewer that had not started yet.
    const container = pub.getByTestId('caveview-container');
    await expect(container.locator('canvas').first()).toBeAttached({ timeout: 30_000 });
    await expect(pub.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });

    // Nobody else is named, and the server has not been asked who else there is, until the
    // reader opens the list.
    await expect(pub.getByTestId('public-live')).toBeHidden();
    await expect(pub.getByText(otherTitle)).toHaveCount(0);
    expect(publicReads.filter((path) => path === `${ownBase}/live`)).toEqual([]);

    // ---- The list, opened ----
    await pub.getByTestId('public-trip-live-section').click();
    const otherRow = pub.getByTestId(`public-live-trip-${other.id}`);
    await expect(otherRow).toBeVisible({ timeout: 20_000 });
    await expect(otherRow).toContainText(otherTitle);
    const standings = pub.getByTestId(`public-live-standings-${other.id}`);
    await expect(standings).toContainText('Underground: 1');
    await expect(standings).toContainText('Out: 1');
    await expect(pub.getByTestId(`public-live-own-${own.id}`)).toHaveText("This link's trip");
    await expect(pub.getByTestId(`public-live-watch-${own.id}`)).toHaveCount(0);

    // ---- Watching the other party ----
    await container.locator('canvas').evaluateAll((canvases) => {
      for (const canvas of canvases) (canvas as HTMLCanvasElement).dataset.seenBefore = '';
    });
    await pub.getByTestId(`public-live-watch-${other.id}`).click();
    await expect(pub.getByTestId('public-watch-banner')).toContainText(
      'You are watching another party of this cave',
      { timeout: 20_000 },
    );
    await expect(pub.getByTestId('public-watch-banner')).toBeInViewport();
    await expect(pub.getByTestId('public-watch-banner-what')).toContainText(otherTitle);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(otherTitle);
    await expect(party).toContainText('E2E Sorin');
    await expect(party).toContainText(STATION_B);
    await expect(party).toContainText('E2E Dana');
    await expect(party).not.toContainText('E2E Carmen');
    await expect(pub.getByTestId(`public-live-watching-${other.id}`)).toHaveText('Watching');
    // The tab and the address stay the link's own: a copy of this address opens the trip the
    // link was published for, never the party the reader happened to be looking at.
    expect(await pub.title()).toBe(ownTitle);
    expect(new URL(pub.url()).search).toBe('');
    // The other party is drawn on the drawing already on screen: the one built for this link is
    // still there and no other was built beside it.
    await expect(container.locator('canvas[data-seen-before]').first()).toBeAttached();
    await expect(container.locator('canvas:not([data-seen-before])')).toHaveCount(0);
    // And everything this browser has read of the published surface is this link's own: the
    // other party's link was never needed, and nothing of the cave's past was asked for.
    expect(publicReads.length).toBeGreaterThan(0);
    expect(publicReads.filter((path) => !path.startsWith(ownBase))).toEqual([]);
    expect(publicReads.filter((path) => path.startsWith(`${ownBase}/past`))).toEqual([]);

    // ---- And back ----
    await pub.getByTestId('public-watch-back').click();
    await expect(pub.getByTestId('public-watch-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(ownTitle);
    await expect(party).toContainText('E2E Carmen');
    await expect(party).not.toContainText('E2E Sorin');
    await expect(pub.getByTestId('public-watch-ended')).toHaveCount(0);

    // ---- The same inside the frame a club pastes, through its sheet ----
    await pub.goto(`/shared/trips/${own.token}/embed`);
    const frame = pub.getByTestId('public-trip-embed');
    await expect(frame).toBeVisible({ timeout: 30_000 });
    await pub.getByTestId('public-past-open').click();
    const framedWatch = pub.getByTestId(`public-live-watch-${other.id}`);
    await expect(framedWatch).toBeVisible({ timeout: 20_000 });
    await framedWatch.click();
    await expect(pub.getByTestId('public-watch-line')).toContainText(otherTitle, {
      timeout: 20_000,
    });
    await expect(frame).toHaveClass(/public-trip-embed-watch/);
    // The sheet got out of the way of the drawing the reader asked to see.
    await expect(framedWatch).toBeHidden();
    await pub.getByTestId('public-watch-back').click();
    await expect(pub.getByTestId('public-watch-line')).toHaveCount(0);
    await expect(frame).not.toHaveClass(/public-trip-embed-watch/);

    // ---- A watched party that leaves the list ----
    //
    // The other party's watch is closed while the visitor is looking at it. This run's server
    // keeps a closed watch readable for no time at all, so the party leaves the list at once;
    // the page learns of it at its next read of the list, which the clock is moved on to.
    await pub.goto(`/shared/trips/${own.token}`);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(ownTitle, { timeout: 30_000 });
    await pub.getByTestId('public-trip-live-section').click();
    await pub.getByTestId(`public-live-watch-${other.id}`).click();
    await expect(pub.getByTestId('public-trip-title')).toHaveText(otherTitle, { timeout: 20_000 });

    await setWatch(page, auth, other.id, { state: 'closed' });
    const ended = pub.getByTestId('public-watch-ended');
    await expect(async () => {
      if (!(await ended.isVisible())) {
        // Shorter than the minute between two reads of the list, so no step carries the page
        // past a read and the next one unobserved.
        await pub.clock.fastForward(30_000);
      }
      expect(
        await ended.isVisible(),
        'the page did not say that the watched party had left the list',
      ).toBe(true);
    }).toPass({ timeout: 120_000 });
    // Said by name, and the page is the link's own trip again under its own title — never a
    // silent swap of whom the reader is looking at.
    await expect(ended).toContainText(otherTitle);
    await expect(pub.getByTestId('public-watch-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(ownTitle);
    await expect(party).toContainText('E2E Carmen');
    await expect(party).not.toContainText('E2E Sorin');
    // The list itself no longer offers that party, and still carries the link's own.
    await expect(pub.getByTestId(`public-live-own-${own.id}`)).toHaveCount(1);
    await expect(pub.getByTestId(`public-live-trip-${other.id}`)).toHaveCount(0);
  } finally {
    await anonymous.close();
    for (const tripId of made.trips) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    for (const caveId of made.caves) {
      await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
  }
});

/**
 * What an installation may choose to publish beside a trip, under a mouse: the hour the party
 * planned to be out by, the names the cave gives its places, the photographs on a replay's
 * moments.
 *
 * <b>Served by the test, unlike everything above.</b> The installation the suite stands up
 * publishes none of the three — they are off as installed — so the tests above are the proof that
 * the page prints nothing for them, against a real server. This one is the other side, which no
 * real answer here can show: the three drawn in a browser at a desk's width.
 */
test('where an installation publishes them, a visitor is told the planned hour, reads a place by its name, and sees a replay’s photographs at their moment', async ({
  page,
}) => {
  const inTwoHours = new Date(Date.now() + 2 * 3_600_000).toISOString();
  const served = await servePublishedExtras(page, { expectedReturnAt: inTwoHours });

  // The party being followed: the plan, said as a plan, and a name over a station.
  await page.goto(extrasPage());
  const plan = page.getByTestId('public-trip-expected');
  await expect(plan).toBeVisible({ timeout: 30_000 });
  await expect(plan).toContainText('Planned out by');
  await expect(page.getByTestId('public-trip-place-1')).toHaveText(PLACE_NAME);
  await expect(page.getByTestId('public-trip-place-station-1')).toHaveText(NAMED_STATION);

  // A replay tells nobody's plan, and before the first photographed moment shows no photograph.
  await page.goto(extrasPage(BEFORE_ANY));
  await expect(page.getByTestId('public-past-banner-what')).toContainText(EXTRAS_TITLE, {
    timeout: 30_000,
  });
  await expect(page.getByTestId('public-past-scrub')).toBeVisible();
  await expect(plan).toHaveCount(0);
  await expect(page.getByTestId('public-past-pictures')).toHaveCount(0);
  expect(served.renderings).toEqual([]);

  // At the moment: the row under the rail, each picture drawn and said to be of whom it is of.
  await page.goto(extrasPage(FIRST_MOMENT));
  const strip = page.getByTestId('public-past-pictures');
  await expect(strip).toBeVisible({ timeout: 30_000 });
  const pictures = strip.getByTestId('public-past-picture');
  await expect(pictures).toHaveCount(AT_FIRST_MOMENT);
  await expect(pictures.nth(1).getByTestId('public-past-picture-who')).toHaveText('Mircea');
  await expect(pictures.nth(1).getByTestId('public-past-picture-caption')).toHaveText(CAPTION);
  await expect(pictures.nth(2).getByTestId('public-past-picture-who')).toHaveText('Caver 2');
  const drawn = pictures.nth(1).getByRole('img');
  await expect(drawn).toHaveAccessibleName('Photograph of Mircea');
  await expect
    .poll(() => drawn.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0))
    .toBe(true);
  const box = (await drawn.boundingBox())!;
  expect(Math.round(box.width)).toBe(112);
  expect(Math.round(box.height)).toBe(84);
  // Under the rail it belongs to, and inside the page's width.
  const rail = (await page.getByTestId('public-past-scrub').boundingBox())!;
  expect(box.y).toBeGreaterThan(rail.y);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    ),
  ).toBeLessThanOrEqual(1);

  // A press opens it larger, from the large rendering; Escape puts it away.
  await drawn.click();
  const opened = page.locator('.ant-image-preview-img');
  await expect(opened).toBeVisible();
  await expect(opened).toHaveAttribute('src', /size=1200/);
  await page.keyboard.press('Escape');
  await expect(opened).toHaveCount(0);
  await expect(page.getByTestId('public-trip-place-1')).toHaveText(PLACE_NAME);
});
