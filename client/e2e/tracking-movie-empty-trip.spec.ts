// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { apiJson } from './rastermapApi.ts';

/**
 * The movie dialog opens from a trip whose watch was armed and closed with nothing reported on it.
 *
 * <b>A trip like that is ordinary.</b> A club sets up the watch for a trip it then writes up some
 * other way, or arms it and nobody goes in: no party list on the watch, no report, one team made
 * ready for the day. Its model carries other trips that were reported, so the dialog has something
 * to offer even when the trip it was opened from has nothing of its own.
 */

const participant = (name: string) => ({
  caverId: null,
  newCaverName: name,
  roleId: null,
  entryTime: null,
  exitTime: null,
  note: null,
});

async function makeTrip(page: Page, token: string, caveId: string, title: string, names: string[]) {
  return (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title,
    tripTypeId: null,
    tripDate: new Date().toISOString().slice(0, 10),
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

async function setWatch(page: Page, token: string, tripId: string, body: Record<string, unknown>) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok()).toBeTruthy();
  const etag = read.headers()['etag'];
  return apiJson(page, token, 'PUT', `/api/v1/trip-logs/${tripId}/tracking`, body, etag ? { 'If-Match': etag } : {});
}

test('the movie dialog opens from a trip whose watch holds nobody and no report', async ({ page }) => {
  test.setTimeout(300_000);
  const stamp = Date.now();
  // Taken from the sign-in's own answer rather than out of the page's modules, which only the
  // development server serves under their source names: this spec runs against the built bundle too.
  const signedIn = page.waitForResponse((response) => new URL(response.url()).pathname === '/connect/token');
  await login(page);
  const auth = ((await (await signedIn).json()) as { access_token: string }).access_token;

  // ---- A cave and its survey ----
  const types = (await apiJson(page, auth, 'GET', '/api/v1/cave-types')) as { id: string }[];
  const cave = (await apiJson(page, auth, 'POST', '/api/v1/caves', {
    name: `E2E Empty Movie Cave ${stamp}`,
    caveTypeId: types[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  })) as { id: string };
  const upload = await page.request.post(`/api/v1/caves/${cave.id}/survey-models`, {
    headers: { Authorization: `Bearer ${auth}` },
    multipart: {
      file: {
        name: 'P8_Master.3d',
        mimeType: 'application/octet-stream',
        buffer: await import('node:fs/promises').then((fs) => fs.readFile('e2e/fixtures/P8_Master.3d')),
      },
    },
  });
  expect(upload.ok()).toBeTruthy();
  const modelId = ((await upload.json()) as { id: string }).id;
  // Read until its stations are stored: a report naming one before then is refused.
  await expect
    .poll(async () => ((await apiJson(page, auth, 'GET', `/api/v1/survey-models/${modelId}`)) as { status: string }).status, {
      timeout: 90_000,
    })
    .toBe('ready');
  const armOn = (tripId: string) =>
    setWatch(page, auth, tripId, { state: 'armed', surveyModelId: modelId, referenceStationName: null, depthFilter: [] });

  // ---- Another trip on the same survey, reported in the ordinary way ----
  const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();
  const other = await makeTrip(page, auth, cave.id, `E2E a reported trip ${stamp}`, ['E2E Ana']);
  const ana = other.participants[0].caverId;
  await armOn(other.id);
  for (const [kind, station, at] of [
    ['entered', null, 6],
    ['atStation', 'p8.p8.98', 5],
    ['exited', null, 2],
  ] as const) {
    await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${other.id}/tracking/events`, {
      caverIds: [ana],
      kind,
      stationName: station,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt: hoursAgo(at),
    });
  }
  await setWatch(page, auth, other.id, { state: 'closed' });

  // ---- The trip itself: armed, one team made ready, closed — and nobody, and nothing reported ----
  const empty = await makeTrip(page, auth, cave.id, `E2E an empty watch ${stamp}`, []);
  await armOn(empty.id);
  await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${empty.id}/tracking/teams`, { title: 'Ready' });
  await setWatch(page, auth, empty.id, { state: 'closed' });

  // ---- Its tracking tab, and the movie button — in Romanian, the language the club reads it in ----
  await page.addInitScript((key) => {
    try {
      window.localStorage.setItem(key, 'ro');
    } catch {
      // A private window keeps the suite's English, and the language check below says so.
    }
  }, 'silexgis.languageChosen');
  await gotoRoute(page, `/trip-logs/${empty.id}?tab=tracking`);
  await expect(page.locator('html')).toHaveAttribute('lang', 'ro', { timeout: 30_000 });
  const button = page.getByTestId('trip-tracking-movie');
  await expect(button).toBeVisible({ timeout: 60_000 });
  await button.click();
  const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('movie-preview') });
  await expect(dialog).toBeVisible({ timeout: 30_000 });
  await expect(dialog.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready', { timeout: 90_000 });
  await expect(dialog.getByTestId(`movie-trip-${empty.id}`).getByRole('checkbox')).toBeChecked({ timeout: 30_000 });
  // The reported trip is offered beside it, and ticking it gives the movie something to show.
  const otherRow = dialog.getByTestId(`movie-trip-${other.id}`);
  await otherRow.getByRole('checkbox').check();
  await expect(otherRow.getByRole('checkbox')).toBeChecked();
});
