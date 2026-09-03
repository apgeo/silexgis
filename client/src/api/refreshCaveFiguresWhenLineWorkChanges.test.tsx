// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is which caches a
// successful write empties, and standing a server up to prove that would test the server.
vi.mock('./client.ts', () => ({
  api: {
    POST: () => Promise.resolve({ data: { id: 'c1' }, response: new Response(null, { status: 200 }) }),
    DELETE: () => Promise.resolve({ data: undefined, response: new Response(null, { status: 204 }) }),
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { queryKeys, useUploadCenterline, useDeleteSurveyModel } = await import('./hooks.ts');

/**
 * A cave's survey figures are worked out from its line work every time they are asked for, but
 * they are cached under the cave while the line work is cached under the centerlines and the
 * survey models. So nothing about uploading a file reaches them unless it is made to: the two
 * panels on a cave's page would keep answering for the cave as it was before the upload, for as
 * long as their answer stayed fresh, with nothing on screen to say so. These tests hold the wiring
 * that closes that gap, because the browser run cannot — it reaches the page by navigating to it,
 * which empties the cache and hides exactly this defect.
 */

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper, invalidate };
}

/** The keys an invalidation spy was asked for, flattened so membership can be asserted. */
function keysAskedFor(invalidate: ReturnType<typeof vi.spyOn>) {
  return invalidate.mock.calls.map(([arg]) => JSON.stringify((arg as { queryKey: unknown }).queryKey));
}

describe("the figures computed from a cave's line work", () => {
  it('are asked for again when a centerline is uploaded', async () => {
    const { wrapper, invalidate } = harness();
    const { result } = renderHook(() => useUploadCenterline(), { wrapper });

    await result.current.mutateAsync({
      caveId: 'cave-1',
      file: new File(['{}'], 'plot.geojson', { type: 'application/geo+json' }),
    });

    await waitFor(() => expect(invalidate).toHaveBeenCalled());
    const asked = keysAskedFor(invalidate);
    expect(asked).toContain(JSON.stringify(queryKeys.centerlines('cave-1')));
    expect(asked).toContain(JSON.stringify(queryKeys.caveSurveyStatistics('cave-1')));
    expect(asked).toContain(JSON.stringify(queryKeys.caveOrientation('cave-1')));
  });

  it('are asked for again when a survey model is removed', async () => {
    const { wrapper, invalidate } = harness();
    const { result } = renderHook(() => useDeleteSurveyModel(), { wrapper });

    await result.current.mutateAsync({ caveId: 'cave-2', id: 'model-1' });

    await waitFor(() => expect(invalidate).toHaveBeenCalled());
    const asked = keysAskedFor(invalidate);
    expect(asked).toContain(JSON.stringify(queryKeys.surveyModels('cave-2')));
    expect(asked).toContain(JSON.stringify(queryKeys.caveSurveyStatistics('cave-2')));
    expect(asked).toContain(JSON.stringify(queryKeys.caveOrientation('cave-2')));
  });
});
