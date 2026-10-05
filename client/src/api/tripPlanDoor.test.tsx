// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stood in for, not the network: what is under test is which door a plan goes
// through and what the client does once it is through, not what the server answers.
type Answer = { data?: unknown; error?: unknown; response: Response };
const asked: { method: 'GET' | 'POST'; path: string; body?: unknown }[] = [];
let answer: Answer = { data: {}, response: new Response(null, { status: 200 }) };

vi.mock('./client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client.ts')>()),
  api: {
    GET: (path: string) => {
      asked.push({ method: 'GET', path });
      return Promise.resolve(answer);
    },
    POST: (path: string, init?: { body?: unknown }) => {
      asked.push({ method: 'POST', path, body: init?.body });
      return Promise.resolve(answer);
    },
  },
}));

const { useCreateTripPlan, useTripPlanDefault, queryKeys } = await import('./hooks.ts');

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

/** A write body with only what tells the two doors apart filled in. */
const plan = {
  title: 'Spring recce',
  tripDate: '2027-04-10',
  visibility: null,
} as unknown as Parameters<ReturnType<typeof useCreateTripPlan>['mutateAsync']>[0];

beforeEach(() => {
  asked.length = 0;
  answer = { data: { id: 'trip-9' }, response: new Response(null, { status: 201 }) };
});
afterEach(() => vi.restoreAllMocks());

/**
 * The plan door on the client side. Two routes shipped with no caller: this pins that the hook
 * for each one calls the route it is named for, and that opening a plan leaves no trip listing
 * showing the world as it was a moment before.
 */
describe('the plan door', () => {
  it('posts the plan through its own door, with the audience left unstated as given', async () => {
    const { wrapper } = harness();
    const { result } = renderHook(() => useCreateTripPlan(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync(plan);
    });

    expect(asked).toEqual([{ method: 'POST', path: '/api/v1/trip-logs/plans', body: plan }]);
    expect((asked[0].body as { visibility: unknown }).visibility).toBeNull();
  });

  it('makes every trip listing read again once a plan exists', async () => {
    const { client, wrapper } = harness();
    const invalidated = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useCreateTripPlan(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync(plan);
    });

    // The whole trip world, by its prefix: the list, the reader's own trips and the dashboard's
    // "coming up" are all keyed under it, and a plan belongs on every one of them at once.
    expect(invalidated).toHaveBeenCalledWith({ queryKey: ['trip-logs'] });
  });

  it('asks who would read a plan from the route that answers before the trip exists', async () => {
    answer = {
      data: { visibility: 'cavingGroup', cavingGroupId: 'g-1', cavingGroupName: 'Silex' },
      response: new Response(null, { status: 200 }),
    };
    const { wrapper } = harness();
    const { result } = renderHook(() => useTripPlanDefault(true), { wrapper });

    await waitFor(() => expect(result.current.data).toBeDefined());

    expect(asked).toEqual([{ method: 'GET', path: '/api/v1/trip-logs/plan-default' }]);
    expect(result.current.data?.cavingGroupName).toBe('Silex');
  });

  it('does not ask at all while no plan is being written', () => {
    const { wrapper } = harness();
    renderHook(() => useTripPlanDefault(false), { wrapper });

    expect(asked).toEqual([]);
  });

  it('is keyed under the trip world, so a write on a trip refreshes it too', () => {
    expect(queryKeys.tripPlanDefault[0]).toBe('trip-logs');
  });
});
