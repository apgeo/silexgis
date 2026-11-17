// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ImportSession, PhotoSession } from '../hooks.ts';
import { queryKeys } from './queryKeys.ts';

// Two saves of one review overlap whenever the first is slower than the pause between two edits.
// The network is stubbed so the test decides the order the answers come back in.

vi.mock('../../auth/auth.tsx', () => ({
  userManager: { getUser: () => Promise.resolve(null) },
}));

const BrowserlessRequest = globalThis.Request;
vi.stubGlobal(
  'Request',
  class extends BrowserlessRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, 'http://localhost') : input, init);
    }
  },
);

/** Each PUT waits here until the test answers it, echoing back what it was sent. */
const waiting: { body: unknown; answer: () => void }[] = [];
vi.stubGlobal(
  'fetch',
  vi.fn(async (input: Request) => {
    const body = (await input.json()) as Record<string, unknown>;
    return new Promise<Response>((resolve) => {
      waiting.push({
        body,
        answer: () =>
          resolve(
            new Response(JSON.stringify({ ...base, ...body }), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            }),
          ),
      });
    });
  }),
);

let base: Record<string, unknown> = {};

// After the stubs: the transport takes hold of the global fetch when it is built.
const { useSaveImportSession, useSavePhotoImportSession } = await import('./imports.ts');

afterEach(() => {
  waiting.length = 0;
});

function wrapperFor(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

const vectorSession: ImportSession = {
  geofileId: 'geo-1',
  options: {
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

describe('saving a review that is still being saved', () => {
  it('keeps the newer vector review in the cache when the older save answers last', async () => {
    base = { ...vectorSession };
    const client = new QueryClient();
    client.setQueryData(queryKeys.importSession('geo-1'), vectorSession);
    const { result } = renderHook(() => useSaveImportSession(), { wrapper: wrapperFor(client) });

    const older = { options: vectorSession.options, decisions: { '1': { action: 'skip' as const } } };
    const newer = { options: vectorSession.options, decisions: { '2': { action: 'skip' as const } } };
    act(() => result.current.mutate({ geofileId: 'geo-1', body: older }));
    await waitFor(() => expect(waiting).toHaveLength(1));
    act(() => result.current.mutate({ geofileId: 'geo-1', body: newer }));
    await waitFor(() => expect(waiting).toHaveLength(2));

    // The newer save is answered first, the older one after it.
    await act(async () => waiting[1].answer());
    await act(async () => waiting[0].answer());
    await waitFor(() => expect(result.current.isPending).toBe(false));
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)));

    const cached = client.getQueryData<ImportSession>(queryKeys.importSession('geo-1'));
    expect(cached?.decisions).toEqual(newer.decisions);
  });

  it('keeps the newer photo drop in the cache when the older save answers last', async () => {
    const photoSession: PhotoSession = {
      fileIds: ['a'],
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
    base = { ...photoSession };
    const client = new QueryClient();
    client.setQueryData(queryKeys.photoImportSession, photoSession);
    const { result } = renderHook(() => useSavePhotoImportSession(), { wrapper: wrapperFor(client) });

    act(() => result.current.mutate({ ...photoSession, fileIds: ['a', 'b'] }));
    await waitFor(() => expect(waiting).toHaveLength(1));
    act(() => result.current.mutate({ ...photoSession, fileIds: ['a', 'b', 'c'] }));
    await waitFor(() => expect(waiting).toHaveLength(2));

    await act(async () => waiting[1].answer());
    await act(async () => waiting[0].answer());
    await waitFor(() => expect(result.current.isPending).toBe(false));
    await act(async () => new Promise((resolve) => setTimeout(resolve, 20)));

    expect(client.getQueryData<PhotoSession>(queryKeys.photoImportSession)?.fileIds).toEqual(['a', 'b', 'c']);
  });
});
