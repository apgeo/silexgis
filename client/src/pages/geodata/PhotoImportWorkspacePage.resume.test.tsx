// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PhotoCandidate, PhotoPreview, PhotoSession } from '../../api/hooks.ts';

// The saved drop is read and written through the real hooks, a real query cache and the real
// transport; only the network answers are stubbed. The defect this file guards lives between the
// cache and the page — a stub of the session hook would take exactly that out of the test.

let serverSession: PhotoSession = {
  fileIds: ['file-1', 'file-2', 'file-3'],
  options: {
    defaultKind: 'caveEntrance',
    clusterRadiusMeters: 25,
    proximityRadiusMeters: 80,
    visibility: 'private',
    locationProtected: false,
    tagIds: [],
    elevation: 'discard',
    cameraClockOffsetSeconds: 0,
    trackMatchToleranceSeconds: 120,
  },
  decisions: {},
  updatedAt: null,
};

const puts: PhotoSession[] = [];

vi.stubGlobal(
  'fetch',
  vi.fn(async (input: Request) => {
    const url = new URL(input.url);
    if (url.pathname === '/api/v1/photo-import/session') {
      if (input.method === 'PUT') {
        const body = (await input.json()) as PhotoSession;
        puts.push(body);
        serverSession = { ...serverSession, ...body };
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

/** Three pictures of one entrance: one candidate with a gallery, not three rows. */
const entrance: PhotoCandidate = {
  key: 'file-1',
  geom: { type: 'Point', coordinates: [25.51, 45.61] },
  positionSource: 'exif',
  members: [
    {
      fileId: 'file-1',
      originalName: 'Pestera Ursilor.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      capturedAt: '2026-08-08T12:00:00Z',
      positionSource: 'exif',
      altitudeMeters: 812,
      directionDegrees: 137,
      directionIsMagnetic: false,
      dop: 1.4,
      confidence: 'excellent',
      hasOwnPosition: true,
      thumbnailUrl: '/api/v1/files/file-1/thumbnail?size=160&token=t',
    },
    {
      fileId: 'file-2',
      originalName: 'IMG_2044.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      capturedAt: '2026-08-08T12:01:00Z',
      positionSource: 'exif',
      altitudeMeters: null,
      directionDegrees: null,
      directionIsMagnetic: false,
      dop: null,
      confidence: 'unknown',
      hasOwnPosition: true,
      thumbnailUrl: '/api/v1/files/file-2/thumbnail?size=160&token=t',
    },
  ],
  proposedName: 'Pestera Ursilor',
  altitudeMeters: 812,
  directionDegrees: 137,
  directionIsMagnetic: false,
  dop: 1.4,
  confidence: 'excellent',
  trackMatchSecondsFromFix: null,
  trackMatchInterpolated: false,
  nearby: [
    {
      featureId: 'feat-9',
      name: null,
      kind: 'caveEntrance',
      distanceMeters: 18.2,
      caveFeatureId: 'cave-9',
      caveName: 'Izbucul Mare',
    },
  ],
  decision: null,
};

const preview: PhotoPreview = {
  items: [entrance],
  page: 1,
  pageSize: 25,
  totalItems: 1,
  allKeys: ['file-1'],
  selectableKeys: ['file-1'],
  placedCount: 1,
  unplacedCount: 0,
  photoCount: 2,
  trackFixCount: 0,
  trackFirstFixAt: null,
  trackLastFixAt: null,
  unreadableFileIds: [],
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePhotoImportPreview: () => ({ data: preview, isFetching: false, isError: false }),
    useCommitPhotoImport: () => ({ mutateAsync: vi.fn(), isPending: false }),
    usePhotoImportTracks: () => ({ data: [], isLoading: false }),
    useUploadFile: () => ({ mutateAsync: vi.fn() }),
    useCaveTypes: () => ({ data: [] }),
    useEntranceTypes: () => ({ data: [] }),
    useFeatureTypes: () => ({ data: [] }),
    useGenericFeatureTypes: () => ({ data: [] }),
    useCavingGroups: () => ({ data: [], isLoading: false }),
  };
});

vi.mock('../../components/import/PhotoCandidateMap.tsx', () => ({
  default: () => <div data-testid="photo-candidate-map" />,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => vi.fn(), useSearchParams: () => [new URLSearchParams(), vi.fn()] };
});

const { default: PhotoImportWorkspacePage } = await import('./PhotoImportWorkspacePage.tsx');

afterEach(cleanup);

describe('reopening a photo review within the same browser visit', () => {
  it('resumes from what was last saved and never saves the first-read copy back over it', async () => {
    // One cache for both visits, as the application has.
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const show = () =>
      render(
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <App>
              <PhotoImportWorkspacePage />
            </App>
          </MemoryRouter>
        </QueryClientProvider>,
      );

    const first = show();
    const name = await screen.findByDisplayValue('Pestera Ursilor');
    await waitFor(() => expect(puts.length).toBeGreaterThan(0), { timeout: 3000 });
    fireEvent.change(name, { target: { value: 'Izbucul de sus' } });
    fireEvent.blur(name);
    await waitFor(() => expect(puts.some((p) => p.decisions['file-1']?.name === 'Izbucul de sus')).toBe(true), {
      timeout: 3000,
    });
    first.unmount();

    const savesBefore = puts.length;
    show();
    // The reopened page shows the name saved last, not the proposal first read.
    expect(await screen.findByDisplayValue('Izbucul de sus')).toBeInTheDocument();
    // A reopened page applies the saved review afresh and saves once the autosave's pause is over,
    // so its own save is what is waited for: the save that would carry a first-read copy back.
    await waitFor(() => expect(puts.length).toBeGreaterThan(savesBefore), { timeout: 3000 });

    for (const sent of puts.slice(savesBefore)) {
      expect(sent.decisions['file-1']?.name).toBe('Izbucul de sus');
      expect(sent.fileIds).toEqual(['file-1', 'file-2', 'file-3']);
    }
    expect(serverSession.decisions['file-1']?.name).toBe('Izbucul de sus');
  }, 10000);
});
