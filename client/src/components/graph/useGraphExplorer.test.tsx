// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { GraphNeighbourhood } from '../../api/hooks/types.ts';
import { count, wireAnswer, wireEdge, wireNode } from '../../graph/graphFixtures.ts';

const fetchNeighbourhood = vi.fn();
vi.mock('../../api/hooks/graph.ts', () => ({
  fetchNeighbourhood: (...args: unknown[]) => fetchNeighbourhood(...args),
}));

import { getExplorerModel, useExplorerStore } from '../../graph/explorerStore.ts';
import { useGraphExplorer } from './useGraphExplorer.ts';

const S = wireNode('feature', 's', 'Cave S');
const doc = wireNode('document', 'd', 'Survey notes');
const trip = wireNode('tripLog', 't', 'Spring trip');
const seedAnswer = (): GraphNeighbourhood =>
  wireAnswer(S, [{ nodes: [doc], edges: [wireEdge('attachment', 'plan', S.key, doc.key, { directed: false })] }], [count('attachment', 1)]);
const docAnswer = (): GraphNeighbourhood =>
  wireAnswer(doc, [{ nodes: [trip], edges: [wireEdge('tripParticipant', 'leader', trip.key, doc.key)] }], [count('tripParticipant', 1)]);

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

async function setup() {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>
  );
  const hook = renderHook(() => useGraphExplorer({ type: 'feature', id: 's' }), { wrapper });
  await waitFor(() => expect(hook.result.current.status).toBe('ready'));
  return hook;
}

const nodeKeys = () => [...getExplorerModel().nodes.keys()].sort();

describe('useGraphExplorer racing writers', () => {
  it('keeps an expansion that is still loading when a refresh starts', async () => {
    const slow = deferred<GraphNeighbourhood>();
    fetchNeighbourhood.mockReset();
    fetchNeighbourhood.mockImplementation((_c, params: { id: string }) => (params.id === 's' ? Promise.resolve(seedAnswer()) : slow.promise));
    const { result } = await setup();

    let expanding!: Promise<void>;
    let refreshing!: Promise<void>;
    act(() => {
      expanding = result.current.expand(doc.key);
    });
    act(() => {
      refreshing = result.current.refresh();
    });
    await act(async () => {
      slow.resolve(docAnswer());
      await expanding;
      await refreshing;
    });
    expect(nodeKeys()).toContain(trip.key);
    expect(useExplorerStore.getState().seed?.id).toBe('s');
  });

  it('drops an expansion answered after the graph was collapsed', async () => {
    const slow = deferred<GraphNeighbourhood>();
    fetchNeighbourhood.mockReset();
    fetchNeighbourhood.mockImplementation((_c, params: { id: string }) => (params.id === 's' ? Promise.resolve(seedAnswer()) : slow.promise));
    const { result } = await setup();

    let expanding!: Promise<void>;
    act(() => {
      expanding = result.current.expand(doc.key);
    });
    act(() => {
      useExplorerStore.getState().collapseToSeed();
    });
    await act(async () => {
      slow.resolve(docAnswer());
      await expanding;
    });
    expect(nodeKeys()).not.toContain(trip.key);
  });
});
