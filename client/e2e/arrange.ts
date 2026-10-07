// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { ownContext } from './consoleGuard.ts';
import { login } from './helpers.ts';
// The session's own token, read where the raster-map flows already read it: one copy of how.
import { bearerToken } from './rastermapApi.ts';

/**
 * What a flow arranges before the part of it that is driven: an account minted for one run, a
 * second browser for whoever is not the subject of the flow, and the calls that person makes to
 * set the scene.
 *
 * The flows that need these are the ones about what one person sees of something another person
 * did — a trip somebody else put them on, a list somebody else kept to themselves — and the
 * administrator cannot be both. The fixture's page is the subject's — the person whose screen the
 * flow is about — and the other browser only arranges what that screen is about; both are
 * watched, because the other browser is made through the console guard's own `ownContext`.
 */

export interface Account {
  /** The account's id, as the registration answered it — what a grant to this person names. */
  id: string;
  email: string;
  password: string;
  displayName: string;
}

/**
 * Registers an account of its own for this run.
 *
 * Self-registration is the only way an installation mints an account without an operator, so a run
 * that cannot do it says so as a missing prerequisite rather than skipping — a skip reads as a
 * non-failure. The stamp is in the display name as well as the address, because people are found
 * by name: the directory refuses to match an address, so that it cannot be used to ask whether an
 * address has an account here.
 */
export async function registerAccount(request: Page['request'], label: string): Promise<Account> {
  const config = (await (await request.get('/api/v1/auth/config')).json()) as {
    openRegistration?: boolean;
  };
  expect(
    config.openRegistration,
    'this flow needs an account that is not the administrator: start the API with SILEXGIS__Auth__OpenRegistration=true',
  ).toBe(true);
  const stamp = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const account = {
    email: `e2e-${label}-${stamp}@dev.local`,
    password: `e2e-${label}-pass-1`,
    displayName: `E2E ${label} ${stamp}`,
  };
  const registered = await request.post('/api/v1/auth/register', { data: account });
  expect(registered.ok(), `registering answered ${registered.status()}`).toBeTruthy();
  const { userId } = (await registered.json()) as { userId: string };
  return { id: userId, ...account };
}

/**
 * A browser of its own, signed in, reading English.
 *
 * English is seeded the way the fixture seeds it for the watched context, from the application's
 * own key: a context made here starts in the application's default language, and every sign-in
 * label the helpers look for is the English one — so without this the second person dies at the
 * login form, waiting for a password field that is labelled in Romanian.
 *
 * The caller closes the context when it is done with it.
 */
export async function signedInElsewhere(
  browser: Browser,
  // An account with or without its id. A flow that registered its accounts itself holds their
  // addresses, passwords and names without ever having been told their ids, and signing in reads
  // none of them but the first two — requiring the id here would turn that flow away for want of
  // a field this function never looks at.
  account?: Omit<Account, 'id'> & Partial<Pick<Account, 'id'>>,
): Promise<{ context: BrowserContext; page: Page }> {
  const context = await ownContext(browser);
  const page = await context.newPage();
  if (account) {
    await login(page, account.email, account.password);
  } else {
    await login(page);
  }
  return { context, page };
}

/**
 * One call to the API as the person signed in on `page`, failing at the call when it is refused
 * rather than later, at whatever the flow expected the call to have arranged.
 */
export async function asPerson<T = unknown>(
  page: Page,
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  data?: unknown,
  headers: Record<string, string> = {},
): Promise<T> {
  const token = await bearerToken(page);
  const response = await page.request.fetch(path, {
    method,
    headers: { Authorization: `Bearer ${token}`, ...headers },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}`).toBeTruthy();
  return (response.status() === 204 ? undefined : await response.json()) as T;
}

/**
 * Waits until an uploaded survey has been read into its stations.
 *
 * <b>Why a flow has to wait at all.</b> An upload answers as soon as the file is stored. The
 * survey's stations are written afterwards, by a job the upload starts, and the survey's own row
 * says how far that has got: waiting, being read, ready, or failed. Until it is ready the survey
 * has no stations on the server, so anything the server checks against them is answered as if the
 * file were empty — a report naming a station is refused as naming no station of the survey, and a
 * report by depth finds no station to stand at.
 *
 * <b>Why it shows only on a busy machine.</b> The steps a flow takes between an upload and its
 * first report — making a trip, arming its watch — are long enough for the job to finish on a
 * quiet machine and not on a loaded one, so a flow without this wait passes alone and fails in a
 * whole run, naming a refusal that has nothing to do with what it tests. A survey's name in the
 * page's list of models is not this state: the row is listed while it is still being read.
 *
 * <b>Waited on by the row's own word</b>, never on the clock and never by trying the report
 * again: the first would be a guess about the machine, the second would hide a refusal that was
 * real. A survey that could not be read ends the wait at once, saying so, instead of being
 * waited on for the whole allowance.
 *
 * Needed by a flow that reports at a station or by depth, or publishes a trip, straight after
 * uploading the survey it is tracked on. Not needed by a flow that only opens the survey in the
 * viewer: the viewer reads the file itself and asks the server for no station.
 */
export async function surveyRead(page: Page, modelId: string): Promise<void> {
  await expect
    .poll(
      async () => {
        const { status } = await asPerson<{ status: string }>(
          page,
          'GET',
          `/api/v1/survey-models/${modelId}`,
        );
        if (status === 'failed') {
          throw new Error('the uploaded survey could not be read: its row says it failed');
        }
        return status;
      },
      { timeout: 90_000, message: 'the uploaded survey was never read into its stations' },
    )
    .toBe('ready');
}

/**
 * The version of a record as last read, for a write the server checks against it.
 *
 * Moving a record from one state to the next is checked against the version the writer read, the
 * way the application's own pages are checked — a move made blind is refused as needing that
 * precondition. So the record is read first, and its version is what the move then names.
 */
export async function versionOf(page: Page, path: string): Promise<string> {
  const token = await bearerToken(page);
  const response = await page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });
  expect(response.ok(), `GET ${path} answered ${response.status()}`).toBeTruthy();
  const etag = response.headers()['etag'];
  expect(etag, `GET ${path} carried no version`).toBeTruthy();
  return etag;
}

/**
 * The same call, for clean-up: a row a flow made is removed on the way out, and a removal that
 * fails there must not hide the failure that sent the flow to its `finally`.
 */
export async function tryAsPerson(
  page: Page,
  method: 'DELETE',
  path: string,
): Promise<void> {
  try {
    const token = await bearerToken(page);
    await page.request.fetch(path, { method, headers: { Authorization: `Bearer ${token}` } });
  } catch {
    // Left behind, stamped with its run: nothing reads a row it did not make itself.
  }
}

/** A calendar day in the pickers' own format, built locally so it means the day the app reads. */
export function localDay(offsetDays: number, from: Date = new Date()): string {
  const date = new Date(from);
  date.setDate(date.getDate() + offsetDays);
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${`${date.getDate()}`.padStart(2, '0')}`;
}

/**
 * A trip as the trip form writes one, with everything a flow does not care about left empty — for
 * the flows where a trip is the scene rather than the subject.
 */
export function tripBody(title: string, day: string, extra: Record<string, unknown> = {}) {
  return {
    title,
    tripTypeId: null,
    tripDate: day,
    tripDateEnd: null,
    entryTime: null,
    exitTime: null,
    description: null,
    results: null,
    weatherConditions: null,
    locationText: null,
    organizingCavingGroupId: null,
    geom: null,
    caveIds: null,
    participants: [],
    proposers: null,
    cavingGroupId: null,
    // Readable by anybody signed in, so a person the flow is about can open the trip they are on.
    // Who may read a trip is decided on the server and has specs of its own; a trip somebody could
    // not read would rightly be listed nowhere they look.
    visibility: 'authenticated',
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
    ...extra,
  };
}

export interface PlannedPurpose {
  checklistId: string;
  checklistTitle: string;
  tripTypeId: number;
}

/**
 * A checklist and a trip purpose that names it, which is how a trip comes to carry a plan: the trip
 * works through whatever list its purpose names, read at the moment it is asked.
 *
 * The purpose is the installation's vocabulary, shared with every other flow, so its code and name
 * carry the run's stamp and the flow that made it removes it again — after the trips written under
 * it, which hold it in place until they are gone.
 */
export async function purposeWithChecklist(
  page: Page,
  stamp: number,
  options: { visibility?: string; lines?: string[] } = {},
): Promise<PlannedPurpose> {
  const checklistTitle = `E2E Plan ${stamp}`;
  const checklist = await asPerson<{ id: string }>(page, 'POST', '/api/v1/checklists', {
    title: checklistTitle,
    description: null,
    cavingGroupId: null,
    visibility: options.visibility ?? 'authenticated',
    items: (options.lines ?? ['Rope checked', 'Callout set']).map((text) => ({ id: null, text })),
  });
  const tripType = await asPerson<{ id: number }>(page, 'POST', '/api/v1/trip-types', {
    code: `e2e_plan_${stamp}`,
    name: `E2E Planned Purpose ${stamp}`,
    description: null,
    sortOrder: 0,
    fieldDataSchema: null,
    logisticsSchema: null,
    safetySchema: null,
    defaultChecklistId: checklist.id,
  });
  return { checklistId: checklist.id, checklistTitle, tripTypeId: tripType.id };
}

/** Removes what `purposeWithChecklist` made, once the trips written under it are gone. */
export async function removePurpose(page: Page, purpose: PlannedPurpose): Promise<void> {
  await tryAsPerson(page, 'DELETE', `/api/v1/trip-types/${purpose.tripTypeId}`);
  await tryAsPerson(page, 'DELETE', `/api/v1/checklists/${purpose.checklistId}`);
}
