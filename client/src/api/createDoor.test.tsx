// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stood in for, not the network: what is under test is how the capabilities
// answer is read into a create door, not what the server decides.
type Answer = { data?: unknown; error?: unknown; response: Response };
const asked: string[] = [];
let answer: () => Promise<Answer>;

vi.mock('./client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client.ts')>()),
  api: {
    GET: (path: string) => {
      asked.push(path);
      return answer();
    },
  },
}));

const { useCreateDoor } = await import('./hooks.ts');

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

/** The capabilities answer, as the server sends it: the map, and the groups beside it. */
function capabilities(
  domains: Record<string, string>,
  createInCavingGroups: Record<string, { id: string; name: string }[]>,
) {
  return () =>
    Promise.resolve({
      data: { domains, isFullAdmin: false, createInCavingGroups },
      response: new Response(null, { status: 200 }),
    });
}

const silex = { id: 'g-1', name: 'Silex' };
const avenul = { id: 'g-2', name: 'Avenul' };

beforeEach(() => {
  asked.length = 0;
});

/**
 * One question for every create door: may this caller create here somewhere, and where. The map
 * alone answers a narrower one — is the right held over the domain as such — and a right held
 * only at a caving group's scope is never that, so a door gated on the map alone stays shut for
 * a member the server would let in.
 */
describe('the create door', () => {
  it('is open, and needs no group, for somebody who holds the right over the domain', async () => {
    answer = capabilities({ tripLogs: 'read, create' }, { tripLogs: [] });
    const { wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    await waitFor(() => expect(result.current.canCreate).toBe(true));
    expect(result.current.unbound).toBe(true);
    expect(result.current.cavingGroups).toEqual([]);
  });

  it('is open for somebody whose right reaches only through a caving group, and names the group', async () => {
    // The gap this exists to close: the map carries no create, the server accepts a row bound
    // to the club, and the door must be drawn.
    answer = capabilities({ tripLogs: 'none' }, { tripLogs: [silex] });
    const { wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    await waitFor(() => expect(result.current.canCreate).toBe(true));
    // What tells the form behind the door that the row has to belong to a group.
    expect(result.current.unbound).toBe(false);
    expect(result.current.cavingGroups).toEqual([silex]);
  });

  it('gives every group the server named, in the order it named them', async () => {
    answer = capabilities({ tripLogs: 'none' }, { tripLogs: [avenul, silex] });
    const { wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    await waitFor(() => expect(result.current.cavingGroups).toHaveLength(2));
    expect(result.current.cavingGroups.map((group) => group.name)).toEqual(['Avenul', 'Silex']);
  });

  it('keeps both halves for somebody who holds the right and is in a club besides', async () => {
    answer = capabilities({ tripLogs: 'read, write, create' }, { tripLogs: [silex] });
    const { wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    await waitFor(() => expect(result.current.canCreate).toBe(true));
    // Unbound is what the form reads: this caller is not made to file their work under a club.
    expect(result.current.unbound).toBe(true);
    expect(result.current.cavingGroups).toEqual([silex]);
  });

  it('is shut for somebody who may create nowhere', async () => {
    answer = capabilities({ tripLogs: 'read' }, { tripLogs: [] });
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    await waitFor(() => expect(client.isFetching()).toBe(0));
    expect(result.current).toEqual({ canCreate: false, unbound: false, cavingGroups: [] });
  });

  it('is not opened by a right in another domain', async () => {
    // A club that lets its members add caves says nothing about trips, and the other way round.
    answer = capabilities(
      { tripLogs: 'none', features: 'none' },
      { tripLogs: [], features: [silex] },
    );
    const { client, wrapper } = harness();
    const { result } = renderHook(
      () => ({ trips: useCreateDoor('tripLogs'), features: useCreateDoor('features') }),
      { wrapper },
    );

    await waitFor(() => expect(client.isFetching()).toBe(0));
    expect(result.current.trips.canCreate).toBe(false);
    expect(result.current.features.canCreate).toBe(true);
    // Two doors, one question to the server: they read the same answer.
    expect(asked).toEqual(['/api/v1/me/capabilities']);
  });

  it('is shut for a domain that has no group binding, which the answer does not list at all', async () => {
    answer = capabilities({ taxonomies: 'read' }, { tripLogs: [silex] });
    const { client, wrapper } = harness();
    const { result } = renderHook(() => useCreateDoor('taxonomies'), { wrapper });

    await waitFor(() => expect(client.isFetching()).toBe(0));
    expect(result.current).toEqual({ canCreate: false, unbound: false, cavingGroups: [] });
  });

  it('is shut while the answer is on its way, and hands back one list rather than a new one each time', () => {
    // A door that appears is fine; one that flashes away is not. And a component that depends
    // on the list must not see a fresh empty array on every render while it waits.
    answer = () => new Promise(() => {});
    const { wrapper } = harness();
    const { result, rerender } = renderHook(() => useCreateDoor('tripLogs'), { wrapper });

    expect(result.current.canCreate).toBe(false);
    const first = result.current.cavingGroups;
    rerender();
    expect(result.current.cavingGroups).toBe(first);
  });
});
