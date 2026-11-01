// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { GraphNeighbourhood } from '../../api/hooks/types.ts';
import { RecordingRenderer } from '../../components/graph/recordingRenderer.ts';
import { count, wireAnswer, wireEdge, wireNode } from '../../graph/graphFixtures.ts';

const renderers: RecordingRenderer[] = [];
vi.mock('../../graph/rendererRegistry.ts', () => ({
  DEFAULT_RENDERER: 'g6',
  RENDERER_KINDS: ['g6'],
  hasRendererChoice: () => false,
  loadRenderer: () => {
    const r = new RecordingRenderer();
    renderers.push(r);
    return Promise.resolve(r);
  },
}));

const fetchNeighbourhood = vi.fn();
vi.mock('../../api/hooks/graph.ts', () => ({
  fetchNeighbourhood: (...args: unknown[]) => fetchNeighbourhood(...args),
}));

import { useExplorerStore } from '../../graph/explorerStore.ts';
import GraphExplorerPage from './GraphExplorerPage.tsx';

class NoopObserver {
  observe(): void {}
  disconnect(): void {}
  unobserve(): void {}
}

const S = wireNode('feature', 's', 'Cave S');
const doc = wireNode('document', 'd', 'Survey notes');
const trip = wireNode('tripLog', 't', 'Spring trip');

const seedAnswer = (): GraphNeighbourhood =>
  wireAnswer(S, [{ nodes: [doc], edges: [wireEdge('attachment', 'plan', S.key, doc.key, { directed: false })] }], [count('attachment', 1)]);
const docAnswer = (): GraphNeighbourhood =>
  wireAnswer(doc, [{ nodes: [trip], edges: [wireEdge('tripParticipant', 'leader', trip.key, doc.key)] }], [count('tripParticipant', 1)]);

function open(address: string) {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[address]}>
        <GraphExplorerPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  renderers.length = 0;
  fetchNeighbourhood.mockReset();
  vi.stubGlobal('ResizeObserver', NoopObserver);
  useExplorerStore.getState().resetFilters();
});

describe('GraphExplorerPage', () => {
  it('without a seed explains itself and loads nothing', () => {
    open('/graph');
    expect(screen.getByTestId('graph-no-seed')).toBeInTheDocument();
    expect(fetchNeighbourhood).not.toHaveBeenCalled();
    expect(screen.queryByTestId('graph-canvas')).toBeNull();
  });

  it('loads the seed hop, lists it, selects from the list, and expands the selection', async () => {
    fetchNeighbourhood.mockImplementation((_client, params: { id: string }) =>
      Promise.resolve(params.id === 's' ? seedAnswer() : docAnswer()),
    );
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    expect(fetchNeighbourhood).toHaveBeenCalledTimes(1);
    expect(fetchNeighbourhood.mock.calls[0][1]).toEqual({ type: 'feature', id: 's' });

    const row = screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:d')!;
    fireEvent.click(within(row).getAllByTestId('graph-list-select')[0]);
    await waitFor(() => expect(canvas).toHaveAttribute('data-selected', 'document:d'));
    await waitFor(() => expect(screen.getByTestId('graph-expand')).toBeEnabled());

    fireEvent.click(screen.getByTestId('graph-expand'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '3'));
    expect(fetchNeighbourhood.mock.calls[1][1]).toEqual({ type: 'document', id: 'd' });
    // Nodes that joined are placed around the node that was expanded.
    await waitFor(() => expect(renderers[0].anchors.at(-1)).toBe('document:d'));
    // Expanded once: nothing left to ask about it.
    await waitFor(() => expect(screen.getByTestId('graph-expand')).toBeDisabled());
  });

  it('collapses back to the first hop', async () => {
    fetchNeighbourhood.mockImplementation((_client, params: { id: string }) =>
      Promise.resolve(params.id === 's' ? seedAnswer() : docAnswer()),
    );
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    fireEvent.click(within(screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:d')!).getAllByTestId('graph-list-select')[0]);
    await waitFor(() => expect(screen.getByTestId('graph-expand')).toBeEnabled());
    fireEvent.click(screen.getByTestId('graph-expand'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '3'));
    fireEvent.click(screen.getByTestId('graph-collapse'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
  });

  it('refresh asks every request again, fresh, and keeps the selection', async () => {
    fetchNeighbourhood.mockResolvedValue(seedAnswer());
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    fireEvent.click(within(screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:d')!).getAllByTestId('graph-list-select')[0]);
    fireEvent.click(screen.getByTestId('graph-refresh'));
    await waitFor(() => expect(fetchNeighbourhood).toHaveBeenCalledTimes(2));
    expect(fetchNeighbourhood.mock.calls[1][2]).toBe(true);
    await waitFor(() => expect(screen.getByTestId('graph-refresh')).not.toHaveAttribute('class', expect.stringContaining('loading')));
    expect(canvas).toHaveAttribute('data-selected', 'document:d');
  });

  it('says so when the seed is missing or unreadable, with no graph', async () => {
    fetchNeighbourhood.mockRejectedValue(new ApiError(404, 'graph.not_found', 'no'));
    open('/graph?type=feature&id=nope');
    expect(await screen.findByTestId('graph-load-error')).toBeInTheDocument();
    expect(screen.queryByTestId('graph-canvas')).toBeNull();
  });

  it('keeps the graph and says so when an expansion fails', async () => {
    fetchNeighbourhood.mockImplementation((_client, params: { id: string }) =>
      params.id === 's' ? Promise.resolve(seedAnswer()) : Promise.reject(new ApiError(500, undefined, 'x')),
    );
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    fireEvent.click(within(screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:d')!).getAllByTestId('graph-list-select')[0]);
    await waitFor(() => expect(screen.getByTestId('graph-expand')).toBeEnabled());
    fireEvent.click(screen.getByTestId('graph-expand'));
    expect(await screen.findByTestId('graph-action-error')).toBeInTheDocument();
    expect(canvas).toHaveAttribute('data-node-count', '2');
  });
});
