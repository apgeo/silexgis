// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { surveyRead, tryAsPerson, versionOf } from './arrange.ts';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { uniquePng } from './png.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * What a visitor to a freshly installed instance is actually told by a published trip's page.
 *
 * <b>Nothing here is served by the test.</b> Every other browser walk over these surfaces that
 * shows a cave's named places or a replay's photographs serves them from a fixture, because no
 * cave the suite stands up had ever declared a place or hung a photograph in the public gallery.
 * A fixture can show that the page draws what it is handed; it cannot show that the server hands
 * it over, and the two settings that decide it default <em>on</em> — so what was unproven was
 * exactly the claim an installation rests on. This walk arranges the rows through the application,
 * leaves every setting at its default, and reads the page as somebody holding nothing but a link.
 *
 * <b>Three claims, each with its own control.</b>
 * <ul>
 *   <li>A depth the cave has <em>named</em> stands over the station a party was reported at, on the
 *     page of a party being followed and on a finished trip's replay. The declaration is written
 *     through the cave's own door and the answer is read back from the server as well as off the
 *     page, so a name on screen cannot have come from anywhere but the published envelope.</li>
 *   <li>A photograph <em>consented to the public gallery</em> and hung on a moment of the trip
 *     stands under the rail at that moment — decoded by the browser from the rendering the server
 *     signed, not merely present in the markup — and the moment before it carries none.</li>
 *   <li>The hour the party planned to be out by appears <em>nowhere</em>, although the trip
 *     records one. Its control is positive: the same hour is read back from the trip through an
 *     account that may see it, so the page's silence is the installation's decision and not a
 *     datum nobody wrote.</li>
 * </ul>
 *
 * <b>The switches are real, and that is shown by running this walk again with one of them off.</b>
 * The runner starts one API for a whole run and passes its own environment through to it, so a
 * setting can be given to that server
 * (`SILEXGIS__TripTracking__PublishDepthPlaces=false node scripts/e2e.mjs
 * public-trip-as-installed.spec.ts --project=desktop --workers=2`) but not varied per spec. This
 * walk therefore asserts the installed position only, and the off position is held down by the
 * integration tests, which stand three hosts over one database and ask them in the same instant.
 * Driving this walk red with that variable is what shows the assertions below have teeth.
 */

/** Two stations of the committed survey fixture: one the cave is given a name for, one not. */
const NAMED_STATION = 'p8.p8.98';
const PLAIN_STATION = 'p8.bens_dig.217';

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
) {
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

/** Points a trip's watch at a survey and moves it between states, honouring the read's etag. */
async function setWatch(page: Page, token: string, tripId: string, body: Record<string, unknown>) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok(), `the watch of ${tripId} answered ${read.status()}`).toBeTruthy();
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

test('as installed, a visitor holding only a link reads a cave’s own name for a place, sees a photograph at its moment, and is never told the hour the party planned to be out', async ({
  page,
  browser,
}) => {
  // A cave, a survey, two trips, a photograph through the gallery and a whole visit afterwards:
  // comfortably past the suite's two-minute default on a machine running other work.
  test.setTimeout(420_000);
  const stamp = Date.now();
  const caveName = `E2E Installed Cave ${stamp}`;
  // Invented, and visibly a test's. Neither name shares a word with a station, a title or a
  // person of the demo dataset, so finding one on the page can only mean it was published.
  const placeName = `E2E Sala Quorn ${stamp}`;
  const caption = `E2E at the pitch head ${stamp}`;
  const mirceaName = `E2E Mircea ${stamp}`;
  const ileanaName = `E2E Ileana ${stamp}`;
  const carmenName = `E2E Carmen ${stamp}`;
  const made: { trips: string[]; caves: string[] } = { trips: [], caves: [] };

  /** Whole seconds, because a moment travels through an address and a link writes it to the second. */
  const hoursAgo = (hours: number) =>
    new Date(Math.floor((stamp - hours * 3_600_000) / 1000) * 1000).toISOString();

  await login(page);

  // ---- The cave and the survey its parties are followed on ----
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId, 'the cave was saved without landing on its own page').toBeTruthy();
  made.caves.push(caveId!);

  const auth = await bearerToken(page);
  const upload = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${auth}` },
    multipart: {
      file: {
        name: `Installed ${stamp}.3d`,
        mimeType: 'application/octet-stream',
        buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
      },
    },
  });
  expect(upload.status(), await upload.text()).toBe(201);
  const modelId = ((await upload.json()) as { id: string }).id;
  // The declaration and every report below name a station: until the upload has been read into
  // its stations, a station is none of the survey's.
  await surveyRead(page, modelId);

  // ---- What the cave calls one of its depths, declared through the cave's own door ----
  // The answer says the survey marked as current holds that station, which is the condition the
  // published envelope applies too: a declaration whose station this survey does not hold would
  // be published to nobody, and a walk over it would pass with the setting either way.
  const declared = (await apiJson(page, auth, 'PUT', `/api/v1/caves/${caveId}/depth-places`, {
    depthM: 42,
    stationName: NAMED_STATION,
    placeLabel: placeName,
    note: null,
    landmark: false,
  })) as { stationName: string; placeLabel: string; stationInSurvey: boolean | null };
  expect(declared.placeLabel).toBe(placeName);
  expect(
    declared.stationInSurvey,
    'the declared station is not in the cave’s survey, so nothing would publish it',
  ).toBe(true);

  // ---- The earlier trip: tracked, reported, photographed, published, closed ----
  const pastTitle = `E2E the morning push ${stamp}`;
  const past = await makeTrip(page, auth, caveId!, pastTitle, [mirceaName, ileanaName]);
  made.trips.push(past.id);
  const mircea = past.participants.find((row) => row.name === mirceaName)!.caverId;
  const ileana = past.participants.find((row) => row.name === ileanaName)!.caverId;
  await setWatch(page, auth, past.id, {
    state: 'armed',
    surveyModelId: modelId,
    referenceStationName: null,
    depthFilter: [],
  });

  const reportOn = (
    tripId: string,
    caverIds: string[],
    kind: string,
    stationName: string | null = null,
    recordedAt: string | null = null,
  ) =>
    apiJson(page, auth, 'POST', `/api/v1/trip-logs/${tripId}/tracking/events`, {
      caverIds,
      kind,
      stationName,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt,
    });

  // A day with a shape, measured back from now because the watch is armed now: both go in, each
  // is placed at a station of their own, both come out. Both placements are before the moment the
  // photographs hang on, so at that moment the replay has each of them somewhere — one at a place
  // the cave has named and one at a station it has not.
  const photographed = hoursAgo(6);
  await reportOn(past.id, [mircea, ileana], 'entered', null, hoursAgo(8));
  await reportOn(past.id, [ileana], 'atStation', PLAIN_STATION, hoursAgo(7));
  await reportOn(past.id, [mircea], 'atStation', NAMED_STATION, photographed);
  await reportOn(past.id, [mircea, ileana], 'exited', null, hoursAgo(1));

  // ---- The photographs: uploaded, captioned, consented to the public gallery, hung on a moment ----
  // Two of them, on the one moment: one of the party and one of a person, which is the pair that
  // shows the strip saying whom each is of without the answer ever carrying anybody's identifier.
  const photographs: { file: string; document: string }[] = [];
  for (const [index, subject] of [null, mircea].entries()) {
    const sent = await page.request.post('/api/v1/files?allowDuplicate=false', {
      headers: { Authorization: `Bearer ${auth}` },
      multipart: {
        file: {
          name: `e2e-installed-photograph-${stamp}-${index}.png`,
          mimeType: 'image/png',
          buffer: uniquePng(64),
        },
      },
    });
    expect(sent.ok(), `the upload answered ${sent.status()}`).toBeTruthy();
    const photograph = (await sent.json()) as { id: string; documentId: string };
    photographs.push({ file: photograph.id, document: photograph.documentId });

    // The caption a published page shows is the gallery's own, so it is written where the gallery
    // keeps it — never the words typed beside the picture when it was hung on the trip, which are
    // for the trip's members and are published to nobody.
    if (subject !== null) {
      await apiJson(page, auth, 'PUT', `/api/v1/photos/${photograph.documentId}/credit`, {
        photographerCaverId: null,
        photographerName: null,
        caption,
        licenceCode: null,
        placeName: null,
      });
    }

    // The act of publication a published page takes as consent, by the only kind of account
    // allowed to take it.
    const consent = await page.request.fetch(
      `/api/v1/photos/${photograph.documentId}/public?published=true`,
      { method: 'PUT', headers: { Authorization: `Bearer ${auth}` } },
    );
    expect(consent.status(), await consent.text()).toBe(204);

    const hung = (await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${past.id}/tracking/pictures`, {
      items: [
        {
          documentId: photograph.documentId,
          at: photographed,
          caverId: subject,
          caption: null,
        },
      ],
    })) as { attached: { documentId: string }[]; refused: Record<string, string> };
    expect(hung.refused, 'the photograph was refused a moment of the trip').toEqual({});
    expect(hung.attached.map((row) => row.documentId)).toEqual([photograph.documentId]);
  }

  const pastShare = (await apiJson(
    page,
    auth,
    'POST',
    `/api/v1/trip-logs/${past.id}/tracking/shares`,
  )) as { token: string };
  expect(pastShare.token).toBeTruthy();
  await setWatch(page, auth, past.id, { state: 'closed' });

  // ---- Today's trip: the link the visitor holds, and the hour it plans to be out by ----
  const liveTitle = `E2E today ${stamp}`;
  const live = await makeTrip(page, auth, caveId!, liveTitle, [carmenName]);
  made.trips.push(live.id);
  const carmen = live.participants[0].caverId;
  const plannedFor = new Date(Math.floor((stamp + 2 * 3_600_000) / 1000) * 1000).toISOString();
  // Arranged through the trip's own door, which — unlike saying the party is out — is an edit of
  // the record and is checked against the version the writer read.
  await apiJson(
    page,
    auth,
    'POST',
    `/api/v1/trip-logs/${live.id}/callout`,
    { expectedReturnAt: plannedFor, calloutAlarmAt: null },
    { 'If-Match': await versionOf(page, `/api/v1/trip-logs/${live.id}`) },
  );
  await setWatch(page, auth, live.id, {
    state: 'armed',
    surveyModelId: modelId,
    referenceStationName: null,
    depthFilter: [],
  });
  await reportOn(live.id, [carmen], 'entered');
  await reportOn(live.id, [carmen], 'atStation', NAMED_STATION);
  const liveShare = (await apiJson(
    page,
    auth,
    'POST',
    `/api/v1/trip-logs/${live.id}/tracking/shares`,
  )) as { token: string };

  // The positive control for the negative below: the hour is on the trip and an account that may
  // read the trip is told it. Without this, a page that says nothing about the plan would read
  // the same whether the installation withholds the hour or nobody ever wrote one.
  const asOwner = (await apiJson(page, auth, 'GET', `/api/v1/trip-logs/${live.id}`)) as {
    expectedReturnAt: string | null;
  };
  expect(
    asOwner.expectedReturnAt === null ? null : new Date(asOwner.expectedReturnAt).toISOString(),
    'the trip does not record the hour, so the page below could not have withheld it',
  ).toBe(plannedFor);

  // ---- The visitor: a fresh context holding nothing but the link ----
  const anonymous = await ownContext(browser);
  try {
    const pub = await anonymous.newPage();

    // ---- The party being followed now ----
    await pub.goto(`/shared/trips/${liveShare.token}`);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(liveTitle, { timeout: 30_000 });
    await expect(pub.getByTestId('public-trip-party')).toContainText(carmenName);

    // The cave's own name for the place stands over the station, and the station stays under it.
    await expect(pub.getByTestId('public-trip-place-1')).toHaveText(placeName, { timeout: 30_000 });
    await expect(pub.getByTestId('public-trip-place-station-1')).toHaveText(NAMED_STATION);

    // And it came off the server: the envelope this very visitor is answered carries the
    // declaration, which is the half a fixture could never show.
    const envelope = await pub.request.get(`/api/v1/public/trips/${liveShare.token}`);
    expect(envelope.ok(), `the published envelope answered ${envelope.status()}`).toBeTruthy();
    const followed = (await envelope.json()) as {
      expectedReturnAt: string | null;
      model: {
        places:
          | {
              station: string;
              depthM: number | null;
              label: string;
              note: string | null;
              landmark: boolean;
            }[]
          | null;
      };
    };
    expect(followed.model.places).toEqual([
      { station: NAMED_STATION, depthM: 42, label: placeName, note: null, landmark: false },
    ]);

    // ---- The hour the party planned to be out by: nowhere ----
    // The envelope withholds it, and the page has only two wordings for it — neither is drawn.
    expect(
      followed.expectedReturnAt,
      'the published envelope carried the hour the party planned to be out by',
    ).toBeNull();
    await expect(pub.getByTestId('public-trip-expected')).toHaveCount(0);
    const onScreen = await pub.getByTestId('public-trip').innerText();
    expect(onScreen).not.toContain('Planned out by');
    expect(onScreen).not.toContain('The plan was to be out by');

    // ---- The finished trip of the same cave, picked as a visitor picks it ----
    await pub.getByText('Past trips in this cave').click();
    const row = pub.getByRole('button', { name: new RegExp(pastTitle) });
    await expect(
      row,
      'the archive did not open: start the API with SILEXGIS__TripTracking__ShareGraceAfterClose=00:00:00, '
        + 'or a trip closed a moment ago is still inside the installation’s live window',
    ).toBeVisible({ timeout: 30_000 });
    await row.click();
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 30_000,
    });
    await expect(pub.getByTestId('public-past-scrub')).toBeVisible();

    // A replay opens at its beginning, hours before anybody photographed anything: no row, no
    // heading. The same page, the same answer — only the clock is different.
    await expect(pub.getByTestId('public-past-pictures')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-expected')).toHaveCount(0);

    // ---- The photographed moment ----
    const strip = pub.getByTestId('public-past-pictures');
    await pub.goto(`/shared/trips/${liveShare.token}?past=${past.id}&at=${photographed}`);
    await expect(pub.getByTestId('public-past-banner-what')).toContainText(pastTitle, {
      timeout: 30_000,
    });
    await expect(strip).toBeVisible({ timeout: 30_000 });
    await expect(pub.getByTestId('public-past-pictures-when')).toContainText('Photographs from');
    const pictures = strip.getByTestId('public-past-picture');
    await expect(pictures).toHaveCount(2);

    // The party's first, then the person's — by the name this page already gives that place in
    // the party, with the gallery's caption beside it.
    await expect(pictures.nth(0).getByRole('img')).toHaveAccessibleName('Photograph of this moment');
    await expect(pictures.nth(0).getByTestId('public-past-picture-who')).toHaveCount(0);
    await expect(pictures.nth(1).getByTestId('public-past-picture-who')).toHaveText(mirceaName);
    await expect(pictures.nth(1).getByTestId('public-past-picture-caption')).toHaveText(caption);
    await expect(pictures.nth(1).getByRole('img')).toHaveAccessibleName(
      `Photograph of ${mirceaName}`,
    );

    // Drawn, and drawn from a rendering this server signed for this visitor: a thumbnail that did
    // not decode is a thumbnail that was not shown, whatever the markup says.
    const drawn = pictures.nth(1).getByRole('img');
    await strip.scrollIntoViewIfNeeded();
    await expect
      .poll(
        () => drawn.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0),
        { timeout: 30_000, message: 'the photograph never decoded in the browser' },
      )
      .toBe(true);
    const source = new URL((await drawn.getAttribute('src'))!, pub.url());
    expect(source.pathname).toBe(`/api/v1/files/${photographs[1].file}/thumbnail`);
    expect(source.searchParams.get('token')).toBeTruthy();
    // A rendering and never the upload: no published address opens the bytes that were sent.
    expect(await strip.innerHTML()).not.toContain('/content');

    // The same name over the same station on the replay, read off the survey that trip was
    // followed on — the person photographed is the one standing at the named place.
    await expect(pub.getByTestId('public-trip-place-1')).toHaveText(placeName);
    await expect(pub.getByTestId('public-trip-place-station-1')).toHaveText(NAMED_STATION);
    // The person at a station the cave gave no name to stands under the station alone, which is
    // what says the name above is a declaration and not a coat of paint over every station.
    await expect(pub.getByTestId('public-trip-caver-2')).toContainText(PLAIN_STATION);
    await expect(pub.getByTestId('public-trip-place-2')).toHaveCount(0);
    // And no replay tells anybody's plan.
    await expect(pub.getByTestId('public-trip-expected')).toHaveCount(0);
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
