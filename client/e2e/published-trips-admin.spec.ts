// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { tryAsPerson } from './arrange.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * The administrators' page of everything published, driven against a real server and read back
 * from the other side: by somebody with no account, holding nothing but an address.
 *
 * <b>Why the second browser.</b> Everything this page does is a claim about what a stranger's
 * address opens — "the old one stops answering", "the fresh one works", "unpublished". A list
 * that redraws with the right word proves the list; only the address itself, opened where there
 * is no session to lean on, proves the act. So each act here is followed by the visitor trying
 * the address it was about.
 *
 * <b>The page is reached through the rail</b>, not by typing its address: a page is registered
 * with the router and with the rail separately, and one missing from the rail is a page nobody
 * finds while nothing fails.
 *
 * <b>Withdrawing everything is deliberately not pressed here.</b> Every spec of a run shares one
 * installation, and that act would take back the links other specs are in the middle of reading.
 * What it does is settled against the server on a database of its own, and that its button stays
 * dead until the word is typed is settled on the page alone.
 *
 * The run stands up its own cave, survey and trip, and takes them down on the way out whether or
 * not it passed.
 */

/** A station of the committed survey fixture, so the published page has somebody to place. */
const STATION = 'p8.p8.98';

const participant = (name: string) => ({
  caverId: null,
  newCaverName: name,
  roleId: null,
  entryTime: null,
  exitTime: null,
  note: null,
});

/** Points a trip's watch at a survey and starts it, honouring the version the read carried. */
async function startWatch(page: Page, token: string, tripId: string, surveyModelId: string) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok()).toBeTruthy();
  const etag = read.headers()['etag'];
  await apiJson(
    page,
    token,
    'PUT',
    `/api/v1/trip-logs/${tripId}/tracking`,
    { state: 'armed', surveyModelId, referenceStationName: null, depthFilter: [] },
    etag ? { 'If-Match': etag } : {},
  );
}

test('an administrator replaces a published link and then unpublishes the trip, and a visitor sees each take effect', async ({
  page,
  browser,
  consoleErrors: guard,
}) => {
  // The visitor opens an address that has been replaced and one that has been taken back; each is
  // answered 404 on purpose, and the browser logs the refusal.
  guard.allow(
    /the server responded with a status of 404/,
    'this flow opens a replaced address and then an unpublished one, and each read that finds them gone is answered 404',
  );

  const stamp = Date.now();
  const caveName = `E2E Published Cave ${stamp}`;
  const tripTitle = `E2E published party ${stamp}`;
  let caveId: string | undefined;
  let modelId: string | undefined;
  let tripId: string | undefined;

  await login(page);

  try {
    // ---- A cave of this run's own, with the committed survey fixture on it ----
    await page.goto('/caves/new');
    await page.getByLabel('Name', { exact: true }).fill(caveName);
    await page.getByLabel('Type', { exact: true }).click();
    await page.locator('.ant-select-item-option').first().click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
    caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
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
    modelId = models[0].id;

    // ---- A trip under way, published through the API as a coordinator's panel would ----
    const trip = (await apiJson(page, auth, 'POST', '/api/v1/trip-logs', {
      title: tripTitle,
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
      participants: [participant('E2E Sorina')],
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
    })) as { id: string; participants: { caverId: string }[] };
    tripId = trip.id;
    const sorina = trip.participants[0].caverId;

    await startWatch(page, auth, trip.id, modelId);
    const report = (kind: string, stationName: string | null = null) =>
      apiJson(page, auth, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
        caverIds: [sorina],
        kind,
        stationName,
        depthM: null,
        teamId: null,
        note: null,
        recordedAt: null,
      });
    await report('entered');
    await report('atStation', STATION);

    const first = (await apiJson(
      page,
      auth,
      'POST',
      `/api/v1/trip-logs/${trip.id}/tracking/shares`,
    )) as { id: string; token: string };
    expect(first.token).toBeTruthy();

    // ---- The page, found where an administrator would look for it ----
    // The rail is on screen already: the cave's page is inside the application's frame.
    await page.getByRole('menuitem', { name: /Administration$/ }).click();
    await page.getByRole('menuitem', { name: 'Published trips' }).click();
    await expect(page).toHaveURL(/\/admin\/published-trips$/);
    await expect(page.getByRole('heading', { name: 'Published trips' })).toBeVisible();

    // The link this run published, said to be what it is: a party being followed right now, on
    // the trip and the cave it belongs to.
    const firstStatus = page.getByTestId(`published-trips-status-${first.id}`);
    await expect(firstStatus).toHaveText('Followed now', { timeout: 20_000 });
    const firstRow = page.getByRole('row').filter({ has: firstStatus });
    await expect(firstRow).toContainText(tripTitle);
    await expect(firstRow).toContainText(caveName);

    // ---- Where the server saw this request come from ----
    // This run's browser reaches the API through the development server on this machine, with no
    // reverse proxy stated in between, so the address printed is this machine's own — and the
    // line says what it would mean were it a proxy's instead.
    await expect(page.getByTestId('published-trips-seen-from-address')).toHaveText(
      /^(127\.0\.0\.1|::1)$/,
    );
    await expect(page.getByTestId('published-trips-seen-from')).toContainText(
      'SILEXGIS__Proxy__Hops',
    );

    // ---- Tracking that is running, and for how long ----
    // Started a moment ago, so no whole day yet; the row leads to the trip's own tracking tab.
    const running = page.getByTestId(`published-trips-running-${first.id}`);
    await expect(running).toContainText('Days running: 0');
    const toTracking = page.getByTestId(`published-trips-watch-${first.id}`);
    await expect(toTracking).toHaveAttribute('href', `/trip-logs/${trip.id}?tab=tracking`);

    // Narrowed to tracking running longer than thirty days, this trip's row goes; asked for
    // everything that is running at all, it is back. The same row both times, so what removed it
    // was the number of days.
    const longRunning = page.getByRole('spinbutton', {
      name: 'Tracking running longer than (days)',
    });
    await longRunning.fill('30');
    await expect(firstStatus).toHaveCount(0, { timeout: 20_000 });
    await longRunning.fill('0');
    await expect(firstStatus).toHaveText('Followed now', { timeout: 20_000 });
    await longRunning.fill('');
    await expect(firstStatus).toHaveText('Followed now', { timeout: 20_000 });

    // The way in lands on the tab where the coordinator's own Close is — nothing is closed from
    // the administration page, and nothing closes by itself.
    await toTracking.click();
    await expect(page).toHaveURL(new RegExp(`/trip-logs/${trip.id}\\?tab=tracking$`));
    await expect(page.getByRole('button', { name: 'Close tracking' })).toBeVisible({
      timeout: 20_000,
    });
    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Published trips' })).toBeVisible();
    await expect(firstStatus).toHaveText('Followed now', { timeout: 20_000 });

    // ---- The visitor, holding the address as first handed out ----
    const anonymous = await ownContext(browser);
    try {
      const pub = await anonymous.newPage();
      await pub.goto(`/shared/trips/${first.token}`);
      await expect(pub.getByTestId('public-trip-title')).toHaveText(tripTitle, { timeout: 30_000 });

      // ---- Replaced: the page says what goes with it, then shows the fresh address once ----
      await page.getByTestId(`published-trips-replace-${first.id}`).click();
      const replaceDialog = page.getByRole('dialog', { name: 'Replace this link?' });
      await expect(replaceDialog).toContainText(tripTitle);
      await expect(replaceDialog).toContainText('The old address stops answering at once');
      await replaceDialog.getByRole('button', { name: 'Replace link' }).click();

      const freshField = page.getByTestId('published-trips-replaced-link');
      await expect(freshField).toBeVisible({ timeout: 20_000 });
      const freshAddress = new URL(await freshField.inputValue());
      // A different address, of the same kind, on this installation.
      expect(freshAddress.pathname).toMatch(/^\/shared\/trips\/[^/]+$/);
      expect(freshAddress.pathname).not.toBe(`/shared/trips/${first.token}`);
      await page.getByTestId('published-trips-replaced-done').click();
      await expect(freshField).toHaveCount(0);

      // The list redraws by itself: the old link is taken back, and the trip has exactly one
      // standing beside it — an exchange, not a second publication.
      await expect(firstStatus).toHaveText('Taken back', { timeout: 20_000 });
      const standingRows = page
        .getByRole('row')
        .filter({ hasText: tripTitle })
        .filter({ hasText: 'Followed now' });
      await expect(standingRows).toHaveCount(1);
      // A link that was taken back offers nothing more to do with it.
      await expect(page.getByTestId(`published-trips-replace-${first.id}`)).toHaveCount(0);

      // ---- What the visitor finds at each address ----
      await pub.goto(freshAddress.pathname);
      await expect(pub.getByTestId('public-trip-title')).toHaveText(tripTitle, { timeout: 30_000 });
      await expect(pub.getByTestId('public-trip-state-armed')).toBeVisible();
      await expect(pub.getByTestId('public-trip-party')).toContainText('E2E Sorina');

      // The old one answers exactly as an address that never existed does.
      await pub.goto(`/shared/trips/${first.token}`);
      await expect(pub.getByTestId('public-trip-not-found')).toBeVisible({ timeout: 20_000 });
      await expect(pub.getByTestId('public-trip-title')).toHaveCount(0);
      const neverIssued = await anonymous.newPage();
      await neverIssued.goto('/shared/trips/this-address-was-never-handed-out');
      await expect(neverIssued.getByTestId('public-trip-not-found')).toBeVisible({
        timeout: 20_000,
      });
      expect(await pub.getByTestId('public-trip-not-found').textContent()).toBe(
        await neverIssued.getByTestId('public-trip-not-found').textContent(),
      );
      await neverIssued.close();

      // ---- Unpublished: every link of the trip, in one act ----
      await standingRows.getByRole('button', { name: 'Unpublish this trip' }).click();
      const unpublishDialog = page.getByRole('dialog', { name: 'Unpublish this trip?' });
      await expect(unpublishDialog).toContainText(tripTitle);
      // The consequence nobody guesses is on the confirmation, with the fact that it is final.
      await expect(unpublishDialog).toContainText("leaves its cave's public list of past trips");
      await expect(unpublishDialog).toContainText('This cannot be undone.');
      await unpublishDialog.getByRole('button', { name: 'Unpublish this trip' }).click();
      // One link was still standing, and one is what the act reports having taken back.
      await expect(page.getByText('Links taken back: 1')).toBeVisible({ timeout: 20_000 });
      await expect(standingRows).toHaveCount(0);
      await expect(
        page.getByRole('row').filter({ hasText: tripTitle }).filter({ hasText: 'Taken back' }),
      ).toHaveCount(2);

      // And the fresh address, which answered a moment ago, answers nothing.
      await pub.goto(freshAddress.pathname);
      await expect(pub.getByTestId('public-trip-not-found')).toBeVisible({ timeout: 20_000 });
      await expect(pub.getByTestId('public-trip-title')).toHaveCount(0);
    } finally {
      await anonymous.close();
    }

    // Take the run's own rows down, and be told if one would not go: the trip, then the survey,
    // then the cave. Off the administration page first, so nothing on screen re-reads a row
    // while it is being removed.
    await gotoRoute(page, '/trip-logs');
    const token = await bearerToken(page);
    await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    tripId = undefined;
    await apiJson(page, token, 'DELETE', `/api/v1/survey-models/${modelId}`);
    modelId = undefined;
    await apiJson(page, token, 'DELETE', `/api/v1/caves/${caveId}`);
    caveId = undefined;
  } finally {
    // Only what a failure above left standing, and quietly: a removal that fails here must not
    // hide the failure that sent the flow this way.
    if (tripId || modelId || caveId) await gotoRoute(page, '/trip-logs').catch(() => undefined);
    if (tripId) await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    if (modelId) await tryAsPerson(page, 'DELETE', `/api/v1/survey-models/${modelId}`);
    if (caveId) await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});
