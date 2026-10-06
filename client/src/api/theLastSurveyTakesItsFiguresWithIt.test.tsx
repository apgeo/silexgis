// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stubbed rather than the network: what is under test is which questions
// deleting a survey makes the cave's page ask, not what the server answers them with.
const get = vi.fn();
const del = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    GET: (path: string, init?: unknown) => get(path, init),
    DELETE: (path: string, init?: unknown) => del(path, init),
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const hooks = await import('./hooks.ts');

/**
 * A cave's passage network is a figure the server refuses outright when the cave has no read line
 * plot, and the panel that shows it asks only of a cave that has one — a rule it works out from
 * the list of uploads. Deleting the cave's only survey used to invalidate the figure in the same
 * breath as the list, so the figure was fetched again before the panel had rendered a list with
 * nothing on it, and the refusal went to the console of a page that had done nothing wrong.
 */
const LIST = '/api/v1/caves/{caveId}/survey-models';
const NETWORK = '/api/v1/caves/{id}/topology';

const survey = (id: string) => ({ id, caveId: 'cave-1', format: 'survex3d', status: 'ready' });

function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper };
}

/** The panel's own rule: the list decides whether the network is asked about at all. */
function useCavePage() {
  const models = hooks.useSurveyModels('cave-1');
  const network = hooks.useCaveTopology('cave-1', hooks.caveHasMeasurableSurvey(models.data));
  const remove = hooks.useDeleteSurveyModel();
  return { models, network, remove };
}

const readsOf = (path: string) => get.mock.calls.filter(([asked]) => asked === path).length;

let uploads: ReturnType<typeof survey>[];

beforeEach(() => {
  get.mockReset();
  del.mockReset();
  get.mockImplementation((path: string) =>
    path === LIST ? answers(uploads) : answers({ surveyModelId: uploads[0]?.id ?? null }),
  );
  del.mockImplementation((_path: string, init: { params: { path: { id: string } } }) => {
    uploads = uploads.filter((upload) => upload.id !== init.params.path.id);
    return answers(undefined);
  });
});

describe('deleting a survey', () => {
  it('does not ask about the network of a cave whose last survey has just gone', async () => {
    uploads = [survey('only')];
    const { wrapper } = harness();
    const page = renderHook(() => useCavePage(), { wrapper });
    await waitFor(() => expect(readsOf(NETWORK)).toBe(1));

    await act(async () => {
      await page.result.current.remove.mutateAsync({ id: 'only', caveId: 'cave-1' });
    });

    // The list is asked again and comes back empty; the network is not asked about at all.
    await waitFor(() => expect(page.result.current.models.data).toEqual([]));
    expect(readsOf(NETWORK)).toBe(1);
    expect(page.result.current.network.data).toBeUndefined();
  });

  it('asks again when another survey is left to answer', async () => {
    uploads = [survey('first'), survey('second')];
    const { wrapper } = harness();
    const page = renderHook(() => useCavePage(), { wrapper });
    await waitFor(() => expect(readsOf(NETWORK)).toBe(1));

    await act(async () => {
      await page.result.current.remove.mutateAsync({ id: 'first', caveId: 'cave-1' });
    });

    // The cave is now measured from the survey that is left, so the figure is read again.
    await waitFor(() => expect(readsOf(NETWORK)).toBe(2));
    await waitFor(() =>
      expect(page.result.current.network.data).toEqual({ surveyModelId: 'second' }),
    );
  });
});
