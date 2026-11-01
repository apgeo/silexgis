// SPDX-License-Identifier: AGPL-3.0-or-later
import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { GraphModel } from '../../graph/graphModel.ts';
import { count, wireAnswer, wireEdge, wireNode } from '../../graph/graphFixtures.ts';
import { toView } from '../../graph/view.ts';
import { buildGraphTheme } from '../../graph/visuals.ts';
import { RecordingRenderer } from './recordingRenderer.ts';

const created: RecordingRenderer[] = [];
vi.mock('../../graph/rendererRegistry.ts', () => ({
  DEFAULT_RENDERER: 'g6',
  loadRenderer: () => {
    const r = new RecordingRenderer();
    created.push(r);
    return Promise.resolve(r);
  },
}));

import GraphCanvas from './GraphCanvas.tsx';

class NoopObserver {
  observe(): void {}
  disconnect(): void {}
  unobserve(): void {}
}

const S = wireNode('feature', 's', 'Cave S');
const a = wireNode('document', 'a', 'Notes');
const model = new GraphModel();
model.merge(wireAnswer(S, [{ nodes: [a], edges: [wireEdge('attachment', 'plan', S.key, a.key)] }], [count('attachment', 1)]));

const light = buildGraphTheme({}, false, false);
const dark = buildGraphTheme({}, true, false);
const labels = { node: () => 'x', edge: () => 'y' };

const props = (over: Partial<Parameters<typeof GraphCanvas>[0]> = {}) => ({
  view: toView(model),
  labels,
  theme: light,
  layout: 'force' as const,
  rendererKind: 'g6' as const,
  anchorKey: null,
  onNodeClick: vi.fn(),
  onNodeDoubleClick: vi.fn(),
  onEdgeClick: vi.fn(),
  onCanvasClick: vi.fn(),
  ...over,
});

beforeEach(() => {
  created.length = 0;
  vi.stubGlobal('ResizeObserver', NoopObserver);
});

describe('GraphCanvas', () => {
  it('mounts one renderer, hands it the view, and reports counts as data attributes', async () => {
    render(<GraphCanvas {...props()} />);
    const canvas = screen.getByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-ready', 'true'));
    await waitFor(() => expect(canvas).toHaveAttribute('data-drawn-nodes', '2'));
    expect(canvas).toHaveAttribute('data-node-count', '2');
    expect(canvas).toHaveAttribute('data-edge-count', '1');
    expect(canvas).toHaveAttribute('data-layout', 'force');
    expect(canvas).toHaveAttribute('data-selected', '');
    expect(created).toHaveLength(1);
    expect(created[0].views.at(-1)?.nodeCount).toBe(2);
  });

  it('repaints on a theme change and re-arranges on a layout change, without a second renderer', async () => {
    const { rerender } = render(<GraphCanvas {...props()} />);
    const canvas = screen.getByTestId('graph-canvas');
    await waitFor(() => expect(canvas).toHaveAttribute('data-ready', 'true'));
    rerender(<GraphCanvas {...props({ theme: dark, layout: 'radial' })} />);
    await waitFor(() => expect(created[0].themes).toEqual([dark]));
    expect(created[0].layouts).toEqual(['radial']);
    expect(canvas).toHaveAttribute('data-layout', 'radial');
    expect(created).toHaveLength(1);
    expect(created[0].mounts).toBe(1);
  });

  it('forwards what the renderer reports to the page', async () => {
    const p = props();
    render(<GraphCanvas {...p} />);
    await waitFor(() => expect(screen.getByTestId('graph-canvas')).toHaveAttribute('data-ready', 'true'));
    created[0].handlers.nodeClick?.('feature:s');
    created[0].handlers.canvasClick?.();
    expect(p.onNodeClick).toHaveBeenCalledWith('feature:s');
    expect(p.onCanvasClick).toHaveBeenCalled();
  });

  it('destroys its renderer on unmount, and a strict-mode double mount leaves exactly one alive', async () => {
    const { unmount } = render(
      <StrictMode>
        <GraphCanvas {...props()} />
      </StrictMode>,
    );
    await waitFor(() => expect(screen.getByTestId('graph-canvas')).toHaveAttribute('data-ready', 'true'));
    expect(created.filter((r) => !r.destroyed)).toHaveLength(1);
    unmount();
    expect(created.every((r) => r.destroyed)).toBe(true);
  });

  it('publishes the position hook only in a driven browser', async () => {
    vi.stubGlobal('navigator', { ...navigator, webdriver: true });
    const { unmount } = render(<GraphCanvas {...props()} />);
    await waitFor(() => expect(window.__silexgisGraph).toBeDefined());
    expect(Object.keys(window.__silexgisGraph!.positions())).toEqual(['feature:s', 'document:a']);
    unmount();
    expect(window.__silexgisGraph).toBeUndefined();
    vi.unstubAllGlobals();
  });
});
