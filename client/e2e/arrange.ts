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

/**
 * Trips nobody but the flow that asked for them wrote, for a flow that follows a count of trips.
 *
 * Every flow writes and deletes trips while the others read, so a count of the installation's
 * trips read twice a few seconds apart is two numbers on any run busier than one file — off by
 * the trip somebody else just made. A count narrowed to a word only this run's titles hold is
 * not: nothing else writes a trip that word finds, so the number stands still for as long as the
 * flow looks at it, and comparing it with itself means what it says.
 *
 * One trip for each day named, written as a draft; a day marked done is moved on to that state,
 * so the trips differ in state and a narrowing by one leaves some of them and not all. Each trip
 * is pushed onto `made` the moment it exists, so the flow's clean-up reaches whatever was
 * written before a failure half-way.
 */
export async function tripsOfItsOwn(
  page: Page,
  word: string,
  made: string[],
  days: { day: string; done?: boolean }[],
): Promise<void> {
  for (const [index, { day, done }] of days.entries()) {
    const trip = await asPerson<{ id: string }>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(`E2E Counted ${word} ${index + 1}`, day),
    );
    made.push(trip.id);
    if (done) {
      await asPerson(
        page,
        'POST',
        `/api/v1/trip-logs/${trip.id}/state`,
        { state: 'done' },
        { 'If-Match': await versionOf(page, `/api/v1/trip-logs/${trip.id}`) },
      );
    }
  }
}
