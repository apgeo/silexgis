// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GraphModel } from './graphModel.ts';
import { count, wireAnswer, wireEdge, wireNode } from './graphFixtures.ts';
import { DEFAULT_RENDERER, RENDERER_KINDS, hasRendererChoice } from './rendererRegistry.ts';
import {
  LAYOUT_KINDS,
  diffView,
  isEmptyDiff,
  type GraphRenderer,
  type Point,
  type RendererHandlers,
} from './renderer.ts';
import type { NodeKey } from './types.ts';
import type { GraphTheme } from './visuals.ts';
import { buildGraphTheme } from './visuals.ts';
import { toView, type GraphView } from './view.ts';

const S = wireNode('feature', 's', 'Cave S');
const a = wireNode('document', 'a', 'A');
const b = wireNode('tripLog', 'b', 'B');

function modelWith(...extra: ReturnType<typeof wireNode>[]): GraphModel {
  const model = new GraphModel();
  model.merge(wireAnswer(S, [{ nodes: [a], edges: [wireEdge('attachment', 'plan', S.key, a.key)] }], [count('attachment', 1)]));
  if (extra.length > 0) {
    model.merge(wireAnswer(a, [{ nodes: extra, edges: extra.map((n) => wireEdge('tripParticipant', 'leader', n.key, a.key)) }], [count('tripParticipant', extra.length)]));
  }
  return model;
}

/**
 * A renderer with no library: it satisfies the contract the page relies on (diff against what is
 * shown, existing nodes keep their place, new nodes appear beside the anchor) and records what it
 * was told. It stands in for a real renderer in tests of everything above the seam.
 */
class FakeRenderer implements GraphRenderer {
  shown: GraphView | null = null;
  theme: GraphTheme | null = null;
  handlers: RendererHandlers = {};
  layout = 'force';
  destroyed = false;
  private pos = new Map<NodeKey, Point>();
  private next = 0;
  mount(): Promise<void> {
    return Promise.resolve();
  }
  setView(view: GraphView, opts?: { anchorKey?: NodeKey }): Promise<void> {
    const diff = diffView(this.shown, view);
    for (const key of diff.removedNodes) this.pos.delete(key);
    for (const node of diff.addedNodes) {
      const anchor = opts?.anchorKey === undefined ? undefined : this.pos.get(opts.anchorKey);
      this.pos.set(node.key, anchor ? { x: anchor.x + 10, y: anchor.y + 10 } : { x: this.next++ * 100, y: 0 });
    }
    this.shown = view;
    return Promise.resolve();
  }
  setLayout(kind: string): Promise<void> {
    this.layout = kind;
    return Promise.resolve();
  }
  focus(): Promise<void> {
    return Promise.resolve();
  }
  fitView(): Promise<void> {
    return Promise.resolve();
  }
  resize(): void {}
  setTheme(theme: GraphTheme): void {
    this.theme = theme;
  }
  setHandlers(handlers: RendererHandlers): void {
    this.handlers = handlers;
  }
  positions(): ReadonlyMap<NodeKey, Point> {
    return this.pos;
  }
  destroy(): void {
    this.destroyed = true;
  }
}

describe('diffView', () => {
  it('reports everything as added against nothing, and nothing against itself', () => {
    const view = toView(modelWith());
    const first = diffView(null, view);
    expect(first.addedNodes.map((n) => n.key).sort()).toEqual([a.key, S.key].sort());
    expect(first.addedEdges).toHaveLength(1);
    expect(isEmptyDiff(diffView(view, toView(modelWith())))).toBe(true);
  });

  it('separates new, removed and re-painted elements', () => {
    const before = toView(modelWith());
    const grown = toView(modelWith(b), undefined, { kind: 'node', key: a.key });
    const diff = diffView(before, grown);
    expect(diff.addedNodes.map((n) => n.key)).toEqual([b.key]);
    expect(diff.addedEdges).toHaveLength(1);
    // Selecting a node repaints the old ones without adding or removing them.
    expect(diff.changedNodes.map((n) => n.key).sort()).toEqual([a.key, S.key].sort());
    expect(diff.changedEdges).toHaveLength(1);
    const shrunk = diffView(grown, before);
    expect(shrunk.removedNodes).toEqual([b.key]);
    expect(shrunk.removedEdges).toHaveLength(1);
  });

  it('treats a node becoming expanded as a change, so its look can update', () => {
    const before = toView(modelWith());
    const model = modelWith();
    model.merge(wireAnswer(a, [], []));
    expect(diffView(before, toView(model)).changedNodes.map((n) => n.key)).toEqual([a.key]);
  });
});

describe('the renderer contract, against a fake', () => {
  it('keeps existing positions when the view grows and puts new nodes beside the anchor', async () => {
    const r = new FakeRenderer();
    await r.mount();
    await r.setView(toView(modelWith()));
    const before = new Map(r.positions());
    await r.setView(toView(modelWith(b)), { anchorKey: a.key });
    for (const [key, at] of before) {
      expect(r.positions().get(key)).toEqual(at);
    }
    const anchor = r.positions().get(a.key)!;
    const placed = r.positions().get(b.key)!;
    expect(Math.hypot(placed.x - anchor.x, placed.y - anchor.y)).toBeLessThan(50);
  });

  it('forgets the position of a node that leaves the view', async () => {
    const r = new FakeRenderer();
    await r.setView(toView(modelWith(b)));
    await r.setView(toView(modelWith()));
    expect(r.positions().has(b.key)).toBe(false);
  });

  it('is driven through the interface alone: theme, handlers, layout, destroy', () => {
    const r: GraphRenderer = new FakeRenderer();
    const theme = buildGraphTheme({}, true, false);
    r.setTheme(theme);
    r.setHandlers({ nodeClick: () => undefined });
    void r.setLayout('radial', { focusKey: S.key });
    r.destroy();
    const fake = r as FakeRenderer;
    expect(fake.theme).toBe(theme);
    expect(fake.layout).toBe('radial');
    expect(fake.destroyed).toBe(true);
  });
});

describe('the registry', () => {
  it('offers one renderer now, so no switch is shown, and defaults to it', () => {
    expect(RENDERER_KINDS).toEqual(['g6']);
    expect(DEFAULT_RENDERER).toBe('g6');
    expect(hasRendererChoice()).toBe(false);
    expect(LAYOUT_KINDS).toEqual(['force', 'radial', 'layered']);
  });
});
