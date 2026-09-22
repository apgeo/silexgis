// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { CHOICE_KEY } from '../src/i18n/languageStorage.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * A cave's past trips, as somebody with no account reaches them.
 *
 * <b>Both trips are real.</b> A live one is stood up and published — that link is the page under
 * test, and the whole of what the visitor holds — and a second, earlier trip of the same cave is
 * tracked, reported, closed and published too. Nothing about the archive is faked: the list, the
 * track, the survey and the refusal all come off the server.
 *
 * <b>The reader is a fresh browser context with no storage at all</b>, because the claim under test
 * is that one link is the whole of an anonymous visitor's claim. That context sits outside the
 * suite's console sweep, so its errors are collected here and asserted empty — stricter than the
 * guard's report mode, and the only honest setting for a surface strangers read with nobody
 * watching its console.
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
}) => {
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
    surveyModelId: modelId,
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
  const anonymous = await browser.newContext();
  try {
    // English is a recorded choice; without it every assertion below reads Romanian.
    await anonymous.addInitScript(
      ({ key, language }: { key: string; language: string }) => {
        try {
          window.localStorage.setItem(key, language);
        } catch {
          // Blocked storage falls back to Romanian, and the assertions below would say so.
        }
      },
      { key: CHOICE_KEY, language: 'en' },
    );
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
      await pub.waitForTimeout(500);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/60-past-page-desktop.png` });
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
      await pub.waitForTimeout(400);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/61-past-page-360.png`, fullPage: true });
    }
    await pub.setViewportSize({ width: 1280, height: 900 });

    // ---- The way back, while there is a party to go back to ----
    await expect(pub.getByTestId('public-past-back')).toHaveText('Back to the party now');
    await expect(pub.getByTestId('public-past-no-live')).toHaveCount(0);
    await pub.getByTestId('public-past-back').click();
    await expect(pub.getByTestId('public-past-banner')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Carmen');

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
      await pub.waitForTimeout(400);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/64-past-embed-picker.png` });
    }
    await embedRow.click();
    await expect(pub.getByTestId('public-past-banner')).toContainText('past trip', {
      timeout: 20_000,
    });
    // The frame itself is marked, which costs no height in a box a club chose the size of.
    await expect(pub.getByTestId('public-trip-embed')).toHaveClass(/public-trip-embed-past/);
    if (process.env.PAST_SHOTS) {
      await pub.waitForTimeout(500);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/62-past-embed.png` });
    }

    // The frame at the width a club's article is actually read at. The drawing keeps the room the
    // snippet gave it and the strip keeps its own; nothing runs off the side.
    await pub.setViewportSize(NARROWEST);
    await expect(pub.getByTestId('public-past-banner')).toBeVisible();
    const framedOverflow = await pub.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(framedOverflow).toBeLessThanOrEqual(1);
    if (process.env.PAST_SHOTS) {
      await pub.waitForTimeout(400);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/65-past-embed-360.png` });
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
      await pub.waitForTimeout(400);
      await pub.screenshot({ path: `${process.env.PAST_SHOTS}/63-past-no-live-party.png` });
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
