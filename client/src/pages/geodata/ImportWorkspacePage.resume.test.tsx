// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ImportCandidate, ImportPreview, ImportSession } from '../../api/hooks.ts';

// The saved review is read and written through the real hooks, a real query cache and the real
// transport; only the network answers are stubbed. The defect this file guards lives between the
// cache and the page — a stub of the session hook would take exactly that out of the test.

let serverSession: ImportSession = {
  geofileId: 'geo-1',
  options: {
    termRuleSetId: 'set-1',
    languages: [],
    mapping: {},
    elevation: 'keep',
    tracks: 'ignore',
    visibility: 'private',
    locationProtected: false,
    tagIds: [],
    duplicateRadiusMeters: 50,
  },
  decisions: {},
  allowCreateWithoutReview: false,
  updatedAt: null,
};

const puts: { decisions: Record<string, unknown> }[] = [];

vi.stubGlobal(
  'fetch',
  vi.fn(async (input: Request) => {
    const url = new URL(input.url);
    if (url.pathname === '/api/v1/geofiles/geo-1/import/session') {
      if (input.method === 'PUT') {
        const body = (await input.json()) as ImportSession;
        puts.push(body);
        serverSession = { ...serverSession, options: body.options, decisions: body.decisions };
      }
      return new Response(JSON.stringify(serverSession), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } });
  }),
);

// The application asks by path and the browser supplies the origin; outside a browser a request
// cannot be built from a path alone, so the origin is supplied here.
const BrowserlessRequest = globalThis.Request;
vi.stubGlobal(
  'Request',
  class extends BrowserlessRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, 'http://localhost') : input, init);
    }
  },
);

vi.mock('../../auth/auth.tsx', () => ({
  userManager: { getUser: () => Promise.resolve(null) },
}));

const twin: ImportCandidate = {
  sourceId: 3,
  geometry: 'point',
  sourceName: 'Peștera de la Izbuc',
  sourceDescription: null,
  sourceCode: null,
  sourceElevation: null,
  ruleId: 'cave-abbrev',
  ruleName: 'Cave abbreviation',
  conflictingRuleNames: [],
  proposedKind: 'cave',
  proposedCaveTypeCode: 'cave',
  proposedEntranceTypeCode: null,
  proposedFeatureTypeCode: null,
  proposedName: 'de la Izbuc',
  geom: { type: 'Point', coordinates: [25.52, 45.62] },
  duplicate: {
    featureId: 'feat-9',
    name: 'Izbucul Mare',
    kind: 'generic',
    distanceMeters: 12.4,
    nameSimilarity: 0.4,
    caveFeatureId: null,
    caveName: null,
  },
  decision: null,
};

const preview: ImportPreview = {
  items: [twin],
  page: 1,
  pageSize: 25,
  totalItems: 1,
  filteredSourceIds: [3],
  selectableSourceIds: [3],
  candidateCount: 1,
  matchedCount: 1,
  unmatchedCount: 0,
  trackCount: 0,
  areaCount: 0,
  truncated: false,
  fileRowCount: 1,
  scannedRowCount: 1,
  ruleHits: [],
  ruleSet: null,
  lithologyValues: [],
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useEffectiveTermRuleSet: () => ({ data: null }),
    useImportPreview: () => ({ data: preview, isFetching: false, isError: false }),
    useCommitImport: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useGeofile: () => ({ data: { id: 'geo-1', name: 'trip.gpx', format: 'gpx' } }),
    useTermRuleSets: () => ({ data: [], isLoading: false }),
    useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }] }),
    useEntranceTypes: () => ({ data: [] }),
    useFeatureTypes: () => ({ data: [] }),
    useGenericFeatureTypes: () => ({ data: [] }),
    useCavingGroups: () => ({ data: [], isLoading: false }),
    useGeofileColumns: () => ({ data: undefined }),
    useProcessingJob: () => ({ data: undefined }),
    useImportBatch: () => ({ data: undefined }),
  };
});

vi.mock('../../components/import/CandidateMap.tsx', () => ({
  default: () => <div data-testid="import-candidate-map" />,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ geofileId: 'geo-1' }), useNavigate: () => vi.fn() };
});

const { default: ImportWorkspacePage } = await import('./ImportWorkspacePage.tsx');

afterEach(cleanup);

describe('reopening a review within the same browser visit', () => {
  it('resumes from what was last saved and never saves the first-read copy back over it', async () => {
    // One cache for both visits, as the application has: leaving the page and coming back within
    // a few minutes finds the session still cached.
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const show = () =>
      render(
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <App>
              <ImportWorkspacePage />
            </App>
          </MemoryRouter>
        </QueryClientProvider>,
      );

    const first = show();
    await screen.findByText('de la Izbuc');
    // The page saves once it has applied the saved review; a choice made before that would be
    // replaced by what was saved, so the first save is the sign the review is in hand.
    await waitFor(() => expect(puts.length).toBeGreaterThan(0), { timeout: 3000 });
    fireEvent.click(screen.getByText('It is this one'));
    await waitFor(() => expect(puts.some((p) => '3' in p.decisions)).toBe(true), { timeout: 3000 });
    first.unmount();

    const savesBefore = puts.length;
    show();
    await screen.findByText('de la Izbuc');
    // Past the autosave's quiet period, so a save the reopened page makes has been sent.
    await new Promise((resolve) => setTimeout(resolve, 1200));

    for (const sent of puts.slice(savesBefore)) {
      expect(sent.decisions).toHaveProperty('3');
    }
    expect(serverSession.decisions).toHaveProperty('3');
  }, 10000);
});
