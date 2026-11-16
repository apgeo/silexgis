// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import i18n from '../i18n';
import type { SharedFeatureEnvelope } from '../api/hooks.ts';

// The page is read through the real generated client and the refusal it builds from a Problem
// Details answer; only the request itself is answered here. (A test environment has no address to
// resolve the client's relative paths against, so the network stand-in sits one step higher.)
const network = { answer: (): Promise<Response> => Promise.reject(new Error('no answer set')) };

const signIn = vi.fn(async (_returnTo?: string) => {});
vi.mock('../auth/auth.tsx', () => ({
  // Nobody is signed in in this tab: the link was opened cold, from a message.
  userManager: { getUser: async () => null },
  useAuth: () => ({ signIn }),
}));

const { api } = await import('../api/client.ts');
const { default: SharedFeaturePage } = await import('./SharedFeaturePage.tsx');

// Mirrors what the generated client hands back: the parsed body as data or error, and the answer.
async function answerLikeTheClient() {
  const response = await network.answer();
  const body: unknown = await response.clone().json().catch(() => undefined);
  return response.ok ? { data: body, response } : { error: body, response };
}

const problem = (status: number, code: string) =>
  new Response(JSON.stringify({ status, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

function envelope(over: Partial<SharedFeatureEnvelope['feature']> = {}): SharedFeatureEnvelope {
  return {
    kind: 'generic',
    feature: {
      id: '00000000-0000-0000-0000-000000000001',
      kind: 'generic',
      featureTypeId: null,
      category: 'surface',
      name: 'Invented spring',
      geometry: null,
      approximateLocation: false,
      description: 'An invented record for this test.',
      properties: { flow: 'seasonal', depthM: 4, nested: { hidden: true } } as never,
      createdAt: '2026-10-01T10:00:00Z',
      updatedAt: '2026-10-01T10:00:00Z',
      ...over,
    },
    cave: null,
    entrance: null,
    centerline: null,
    children: [{ id: '00000000-0000-0000-0000-000000000002', kind: 'generic', name: 'Invented sink' }],
  };
}

function show(path = '/shared/features/tok-1?from=chat#top') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <App>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/shared/features/:token" element={<SharedFeaturePage />} />
            <Route path="/login" element={<div data-testid="password-page" />} />
          </Routes>
        </MemoryRouter>
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  vi.spyOn(api, 'GET').mockImplementation((() => answerLikeTheClient()) as never);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  signIn.mockClear();
  network.answer = () => Promise.reject(new Error('no answer set'));
});

/**
 * <b>A link handed out for a record opens that record, or says plainly why it cannot.</b> The page
 * is reached by a token from a message, by people with no session in that tab. A link that needs an
 * account has to bring the reader back to the same link once they are signed in; a link that is
 * wrong or withdrawn says so; and a server that did not answer is not a link that does not exist.
 */
describe('the page a shared record link opens', () => {
  it('a link that needs an account signs in and comes back to the same link', async () => {
    network.answer = async () => problem(401, 'share.login_required');
    show();

    const button = await screen.findByTestId('shared-feature-sign-in');
    fireEvent.click(button);

    expect(signIn).toHaveBeenCalledWith('/shared/features/tok-1?from=chat#top');
    // Not the password page, whose sign-in ends on the home page and loses the link.
    expect(screen.queryByTestId('password-page')).toBeNull();
    expect(button.closest('a')).toBeNull();
  });

  it('a wrong or withdrawn link says it does not exist, with nothing to retry', async () => {
    network.answer = async () => problem(404, 'share.not_found');
    show();

    const alert = await screen.findByTestId('shared-feature-not-found');
    expect(alert.textContent).toContain(i18n.t('shares.sharedNotFound'));
    expect(screen.queryByRole('button', { name: i18n.t('common.retry') })).toBeNull();
  });

  it.each([
    ['a server error', () => Promise.resolve(problem(500, 'server.error'))],
    ['no connection', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])('%s is a failed load with a retry, never "this link does not exist"', async (_name, answer) => {
    network.answer = answer;
    show();

    const alert = await screen.findByTestId('shared-feature-load-failed');
    expect(alert.textContent).not.toContain(i18n.t('shares.sharedNotFound'));

    network.answer = async () =>
      new Response(JSON.stringify(envelope()), { status: 200, headers: { 'Content-Type': 'application/json' } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));

    expect(await screen.findByText('Invented spring')).toBeTruthy();
  });

  it('a record without a shape shows its details and no map', async () => {
    network.answer = async () =>
      new Response(JSON.stringify(envelope()), { status: 200, headers: { 'Content-Type': 'application/json' } });
    const { container } = show();

    expect(await screen.findByText('Invented spring')).toBeTruthy();
    expect(screen.getByText('An invented record for this test.')).toBeTruthy();
    expect(screen.getByText('Invented sink')).toBeTruthy();
    // Plain typed values are listed; a nested one has no meaning without its type and is left out.
    expect(screen.getByText('seasonal')).toBeTruthy();
    expect(screen.queryByText('hidden')).toBeNull();
    expect(container.querySelector('[data-testid="shared-feature-map"]')).toBeNull();
  });

  it('a record shown only roughly says its location is approximate', async () => {
    network.answer = async () =>
      new Response(JSON.stringify(envelope({ approximateLocation: true })), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    show();

    expect(await screen.findByText(i18n.t('map.approximate'))).toBeTruthy();
  });
});

/** An invented cave, entrance and survey line, beside the record of the envelope above. */
function withCards(
  over: Partial<Pick<SharedFeatureEnvelope, 'cave' | 'entrance' | 'centerline'>> = {},
): SharedFeatureEnvelope {
  const cave = {
    otherToponyms: null,
    identificationCode: 'INV-001',
    caveTypeId: 1,
    website: null,
    region: 'Invented region',
    hydrographicBasin: null,
    valley: null,
    tributaryRiver: null,
    closestAddress: null,
    landRegistryNumber: null,
    locationNotes: null,
    rockTypeId: null,
    rockAge: null,
    surveyedLength: 123,
    estimatedLength: null,
    realExtension: null,
    projectedExtension: null,
    depth: 45,
    positiveDepth: null,
    negativeDepth: null,
    potentialDepth: null,
    altitude: null,
    volume: null,
    area: null,
    ramificationIndex: null,
    caveAge: null,
    explorationStatus: 'ongoing',
    protectionClass: null,
    isShowCave: false,
    showCaveLength: null,
    discoveryDate: null,
    discoverer: null,
    entranceCount: 2,
  } as SharedFeatureEnvelope['cave'];
  return {
    ...envelope(),
    cave,
    entrance: { entranceTypeId: 1, isMain: true, altitude: 640, positionQuality: 'gps', surveyedAt: null },
    centerline: { isDefault: true, lengthM: 1234, pathCount: 3, source: 'extracted' },
    ...over,
  };
}

function answerWith(body: SharedFeatureEnvelope) {
  network.answer = async () =>
    new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

describe('the cards a shared record carries', () => {
  it('shows the cave, the entrance and the survey line in words, never as lookup keys', async () => {
    answerWith(withCards());
    const { container } = show();

    expect(await screen.findByText('INV-001')).toBeTruthy();
    expect(screen.getByText('Invented region')).toBeTruthy();
    expect(screen.getByText(i18n.t('caves.explorationValues.ongoing'))).toBeTruthy();
    expect(screen.getByText(i18n.t('entrances.qualityValues.gps'))).toBeTruthy();
    expect(screen.getByText(i18n.t('centerlines.sourceValues.extracted'))).toBeTruthy();
    expect(screen.getByText(`${Number(1234).toLocaleString()} m`)).toBeTruthy();
    expect(container.textContent).not.toMatch(/sourceValues\.|qualityValues\.|explorationValues\./);
  });

  it('leaves out a value it has no words for rather than printing its lookup key', async () => {
    // The contract says these are always one of a few named values; an answer from a server that
    // has grown a new one, or that sends none, must still read as a page and not as a key.
    answerWith(
      withCards({
        entrance: { entranceTypeId: 1, isMain: false, altitude: null, positionQuality: 'sonar' as never, surveyedAt: null },
        centerline: { isDefault: false, lengthM: null, pathCount: 1, source: null as never },
      }),
    );
    const { container } = show();

    expect(await screen.findByText('INV-001')).toBeTruthy();
    expect(container.textContent).not.toMatch(/sourceValues|qualityValues|undefined|null/);
    expect(screen.queryByText(i18n.t('centerlines.source'))).toBeNull();
    expect(screen.queryByText(i18n.t('entrances.positionQuality'))).toBeNull();
  });
});

describe('the sign-in a shared record link begins', () => {
  it('says so when the sign-in server cannot be reached, rather than doing nothing', async () => {
    network.answer = async () => problem(401, 'share.login_required');
    signIn.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    show();

    fireEvent.click(await screen.findByTestId('shared-feature-sign-in'));

    await waitFor(() => expect(screen.getByText(i18n.t('auth.serverUnreachable'))).toBeTruthy());
  });
});
