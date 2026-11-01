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
const graphSearch = vi.fn();
vi.mock('../../api/hooks/graph.ts', () => ({
  fetchNeighbourhood: (...args: unknown[]) => fetchNeighbourhood(...args),
  useGraphSearch: (q: string) => ({ data: graphSearch(q) }),
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
  graphSearch.mockReset();
  graphSearch.mockReturnValue(undefined);
  useExplorerStore.getState().resetFilters();
  useExplorerStore.getState().setSearchText('');
  useExplorerStore.getState().select(null);
});

describe('GraphExplorerPage', () => {
  it('without a seed explains itself and loads nothing', () => {
    open('/graph');
    expect(screen.getByTestId('graph-no-seed')).toBeInTheDocument();
    expect(screen.getByTestId('graph-seed-search')).toBeInTheDocument();
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

  it('hides a category from the list and the canvas, and shows it again, without asking the server', async () => {
    fetchNeighbourhood.mockResolvedValue(seedAnswer());
    open('/graph?type=feature&id=s');
    await waitFor(() => expect(screen.getAllByTestId('graph-list-item')).toHaveLength(2));
    const calls = fetchNeighbourhood.mock.calls.length;
    const row = document.querySelector('[data-category="attachment"]') as HTMLElement;
    fireEvent.click(within(row).getByRole('checkbox'));
    await waitFor(() => expect(screen.getAllByTestId('graph-list-item')).toHaveLength(1));
    expect(screen.getByTestId('graph-canvas')).toHaveAttribute('data-node-count', '1');
    fireEvent.click(screen.getByTestId('graph-filter-all'));
    await waitFor(() => expect(screen.getAllByTestId('graph-list-item')).toHaveLength(2));
    expect(fetchNeighbourhood.mock.calls.length).toBe(calls);
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
  it('finds loaded items by title without accents, lights the matches and walks them with Enter', async () => {
    fetchNeighbourhood.mockResolvedValue(seedAnswer());
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    const box = screen.getByRole('searchbox', { name: /search the graph/i });
    fireEvent.change(box, { target: { value: 'SURVEY' } });
    await waitFor(() => expect(screen.getByTestId('graph-search-count')).toHaveTextContent('1'));
    // The match is lifted and the rest fade on the canvas.
    await waitFor(() => {
      const states = Object.fromEntries(renderers[0].views.at(-1)!.nodes.map((n) => [n.key, n.state]));
      expect(states).toEqual({ 'feature:s': 'faded', 'document:d': 'selected' });
    });
    fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', charCode: 13 });
    await waitFor(() => expect(canvas).toHaveAttribute('data-selected', 'document:d'));
    expect(renderers[0].focused.at(-1)).toBe('document:d');
    fireEvent.change(box, { target: { value: 'zzz' } });
    await waitFor(() => expect(screen.getByTestId('graph-search-count')).toHaveTextContent(/no match/i));
  });

  it('shows a legend of only what is on screen', async () => {
    fetchNeighbourhood.mockResolvedValue(seedAnswer());
    open('/graph?type=feature&id=s');
    await waitFor(() => expect(screen.getAllByTestId('graph-legend-type')).toHaveLength(2));
    expect(screen.getAllByTestId('graph-legend-category')).toHaveLength(1);
    expect(screen.queryByTestId('graph-legend-more')).toBeNull();
  });

  it('pages a category through its "+N more" stub, from the list and from the canvas, and merges the next page', async () => {
    const more = (n: number) => wireNode('document', `p${n}`, `Paged ${n}`);
    const first = wireAnswer(
      S,
      [{ nodes: [more(1)], edges: [wireEdge('attachment', 'plan', S.key, more(1).key)] }],
      [count('attachment', 1, 3)],
    );
    const second = wireAnswer(
      S,
      [{ nodes: [more(2), more(3)], edges: [wireEdge('attachment', 'plan', S.key, more(2).key), wireEdge('attachment', 'plan', S.key, more(3).key)] }],
      [count('attachment', 2, 3)],
    );
    fetchNeighbourhood.mockImplementation((_client, params: { category?: string }) =>
      Promise.resolve(params.category === undefined ? first : second),
    );
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    // The stub is in the drawing but does not count as an item.
    await waitFor(() => expect(renderers[0].views.at(-1)!.nodes.some((n) => n.kind === 'more')).toBe(true));
    expect(screen.getByTestId('graph-list-more')).toHaveTextContent('+2');

    fireEvent.click(screen.getByTestId('graph-list-more'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '4'));
    expect(fetchNeighbourhood.mock.calls[1][1]).toEqual({ type: 'feature', id: 's', category: 'attachment', offset: 1 });
    expect(screen.queryByTestId('graph-list-more')).toBeNull();
    expect(renderers[0].views.at(-1)!.nodes.some((n) => n.kind === 'more')).toBe(false);
  });

  it('opens the details of the selection, and removing an item takes it out of the graph', async () => {
    fetchNeighbourhood.mockResolvedValue(seedAnswer());
    open('/graph?type=feature&id=s');
    const canvas = await screen.findByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '2'));
    expect(screen.getByTestId('graph-details-open-button')).toBeDisabled();
    fireEvent.click(within(screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:d')!).getAllByTestId('graph-list-select')[0]);
    fireEvent.click(await screen.findByTestId('graph-details-open-button'));
    expect(await screen.findByTestId('graph-details-node')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('graph-details-remove'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-node-count', '1'));
  });
});
