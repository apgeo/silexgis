// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// The transport is stood in for, not the network: what is under test is what the client reads
// again once a write on a caving group has answered, not what the server said.
const answer = () =>
  Promise.resolve({ data: { id: 'g-1' }, response: new Response(null, { status: 200 }) });

vi.mock('./client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client.ts')>()),
  api: { GET: answer, POST: answer, DELETE: answer },
}));

const { queryKeys, useCreateCavingGroup, useRemoveCavingGroupMember, useUpsertCavingGroupMember } =
  await import('./hooks.ts');

function harness() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidated = vi.spyOn(client, 'invalidateQueries');
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { invalidated, wrapper };
}

beforeEach(() => vi.restoreAllMocks());

/**
 * A caving group's rules reach people through its roster, so a write that changes who is in a
 * group can change what the person making it may do — and what they may create is drawn from the
 * capabilities answer, which is otherwise kept for minutes. Somebody who has just founded a club,
 * or joined one, must find the doors that club opens without reloading the page; somebody who has
 * just left one must not be offered a door that now leads to a refusal.
 */
describe('a write on a caving group', () => {
  it('reads the founder’s capabilities again when a group is created', async () => {
    const { invalidated, wrapper } = harness();
    const { result } = renderHook(() => useCreateCavingGroup(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        name: 'Silex',
        type: 'cavingClub',
        description: null,
        website: null,
      });
    });

    expect(invalidated).toHaveBeenCalledWith({ queryKey: queryKeys.capabilities });
    // The directory still refreshes, as it always did.
    expect(invalidated).toHaveBeenCalledWith({ queryKey: ['cavingGroups'] });
  });

  it('reads them again when somebody is put on a roster', async () => {
    const { invalidated, wrapper } = harness();
    const { result } = renderHook(() => useUpsertCavingGroupMember('g-1'), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ caverId: 'c-1', role: 'member' });
    });

    expect(invalidated).toHaveBeenCalledWith({ queryKey: queryKeys.capabilities });
    expect(invalidated).toHaveBeenCalledWith({ queryKey: queryKeys.cavingGroupMembers('g-1') });
  });

  it('reads them again when somebody is taken off one', async () => {
    const { invalidated, wrapper } = harness();
    const { result } = renderHook(() => useRemoveCavingGroupMember('g-1'), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('c-1');
    });

    expect(invalidated).toHaveBeenCalledWith({ queryKey: queryKeys.capabilities });
  });
});
