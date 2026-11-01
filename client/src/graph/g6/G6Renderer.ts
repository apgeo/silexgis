// SPDX-License-Identifier: AGPL-3.0-or-later
import { CanvasEvent, EdgeEvent, Graph, NodeEvent, type EdgeData, type NodeData } from '@antv/g6';
import {
  diffView,
  isEmptyDiff,
  type GraphLabels,
  type GraphRenderer,
  type LayoutKind,
  type Point,
  type RendererHandlers,
  type RendererMountOptions,
} from '../renderer.ts';
import type { NodeKey } from '../types.ts';
import { paintEdge, paintNode, type GraphTheme } from '../visuals.ts';
import type { GraphView, ViewEdge, ViewNode } from '../view.ts';
import { placeNewNodes } from './placement.ts';
import { G6_NODE_TYPE, edgeStyle, layoutOptions, nodeStyle, showEdgeLabel } from './styleMap.ts';

/**
 * The renderer behind the explorer, drawing on a canvas 2D context (the development machine has no
 * GPU, and a few hundred nodes do not need one). This is the only file in the application that
 * imports the library; everything above it speaks `GraphRenderer`.
 *
 * Three decisions shape it:
 * - The model decides highlight states and G6's own select/hover behaviours are not used, so a
 *   second renderer behaves identically. Only `drag-canvas`, `zoom-canvas` and `drag-element` are on.
 * - No layout is configured on the graph. G6 re-runs a configured layout when data changes and
 *   would move everything the user was looking at; instead a layout runs only on request
 *   (`setLayout`, and once for the first picture) and nodes that join later are placed by
 *   `placeNewNodes`, near the node that was expanded.
 * - Calls are queued. The page fires `setView` from effects, faster than a draw finishes, and two
 *   overlapping updates to one canvas corrupt each other.
 */
export function createG6Renderer(): GraphRenderer {
  return new G6Renderer();
}

const ANIMATION_MS = 250;

class G6Renderer implements GraphRenderer {
  private graph: Graph | null = null;
  private container: HTMLElement | null = null;
  private theme: GraphTheme | null = null;
  private reduceMotion = false;
  private layoutKind: LayoutKind = 'force';
  private shown: GraphView | null = null;
  private labels: GraphLabels | null = null;
  private handlers: RendererHandlers = {};
  private queue: Promise<unknown> = Promise.resolve();
  private destroyed = false;

  // ---- lifecycle -----------------------------------------------------------------------------------

  mount(container: HTMLElement, options: RendererMountOptions): Promise<void> {
    return this.enqueue(async () => {
      this.container = container;
      this.theme = options.theme;
      this.reduceMotion = options.reduceMotion;
      this.layoutKind = options.layout ?? 'force';
      container.style.backgroundColor = options.theme.background;
      const graph = new Graph({
        container,
        width: Math.max(container.clientWidth, 200),
        height: Math.max(container.clientHeight, 200),
        autoResize: true,
        animation: !options.reduceMotion,
        zoomRange: [0.15, 4],
        behaviors: ['drag-canvas', 'zoom-canvas', 'drag-element'],
        // Two relations between the same pair of items (an attachment and a link, say) would draw on
        // top of each other; bundling fans them apart.
        transforms: [{ type: 'process-parallel-edges', mode: 'bundle', distance: 22 }],
        edge: { type: 'line' },
      });
      this.graph = graph;
      this.bindEvents(graph);
      await graph.render();
    });
  }

  destroy(): void {
    this.destroyed = true;
    const graph = this.graph;
    this.graph = null;
    this.shown = null;
    if (graph !== null) {
      try {
        graph.stopLayout();
        graph.destroy();
      } catch {
        // A canvas torn down mid-draw can throw on the way out; there is nothing left to protect.
      }
    }
  }

  // ---- the picture -------------------------------------------------------------------------------------

  setView(view: GraphView, opts?: { anchorKey?: NodeKey; labels?: GraphLabels }): Promise<void> {
    return this.enqueue(async () => {
      const graph = this.live();
      if (graph === null || this.theme === null) {
        return;
      }
      const theme = this.theme;
      const relabel = opts?.labels !== undefined && opts.labels !== this.labels;
      if (opts?.labels !== undefined) {
        this.labels = opts.labels;
      }
      const hadNodes = graph.getNodeData().length > 0;
      const diff = diffView(this.shown, view);
      this.shown = view;
      if (isEmptyDiff(diff) && !relabel) {
        return;
      }

      // Removals first, so a node leaving never strands an edge pointing at it.
      if (diff.removedEdges.length > 0) {
        graph.removeEdgeData(diff.removedEdges);
      }
      if (diff.removedNodes.length > 0) {
        graph.removeNodeData(diff.removedNodes);
      }

      const placed = this.nodePositions();
      const first = placed.size === 0;
      const fresh = placeNewNodes({
        added: diff.addedNodes.map((n) => n.key),
        edges: view.edges,
        // On a first picture the seed is the one node with nothing to hang from: it starts in the middle.
        positions: first && view.seedKey !== null ? new Map([[view.seedKey, this.centre()]]) : placed,
        anchorKey: opts?.anchorKey,
        center: this.centre(),
      });
      if (first && view.seedKey !== null) {
        fresh.set(view.seedKey, this.centre());
      }

      const nodeData = (vn: ViewNode, at?: Point): NodeData => {
        const paint = paintNode(vn, theme);
        return {
          id: vn.key,
          type: G6_NODE_TYPE[paint.shape],
          style: { ...(at ? { x: at.x, y: at.y } : {}), ...nodeStyle(paint, this.nodeLabel(vn), theme) },
        };
      };
      const edgeData = (ve: ViewEdge): EdgeData => {
        const paint = paintEdge(ve, theme);
        return {
          id: ve.key,
          source: ve.source,
          target: ve.target,
          style: edgeStyle(paint, showEdgeLabel(paint, view.edges.length) ? this.edgeLabel(ve) : '', theme),
        };
      };

      if (diff.addedNodes.length > 0) {
        graph.addNodeData(diff.addedNodes.map((vn) => nodeData(vn, fresh.get(vn.key))));
      }
      if (diff.addedEdges.length > 0) {
        graph.addEdgeData(diff.addedEdges.map(edgeData));
      }
      // A change of wording or of language touches every element; otherwise only the ones the diff names.
      const addedNodes = new Set(diff.addedNodes);
      const addedEdges = new Set(diff.addedEdges);
      const changedNodes = relabel ? view.nodes.filter((n) => !addedNodes.has(n)) : diff.changedNodes;
      const changedEdges = relabel ? view.edges.filter((e) => !addedEdges.has(e)) : diff.changedEdges;
      if (changedNodes.length > 0) {
        // No position in these: an update merges style, so a node keeps the place the user left it.
        graph.updateNodeData(changedNodes.map((vn) => nodeData(vn)));
      }
      if (changedEdges.length > 0) {
        graph.updateEdgeData(changedEdges.map(edgeData));
      }
      await graph.draw();

      if (!hadNodes && view.nodes.length > 1) {
        await this.arrange(graph, view.seedKey);
        await graph.fitView({ when: 'always' }, false);
      } else if (!hadNodes && view.nodes.length === 1) {
        await graph.fitCenter(false);
      } else if (hadNodes) {
        // An update that only adds, removes or repaints must not move what the user was looking at.
        // The library applies a finished layout without waiting for its element redraw, so a late
        // write from an earlier arrangement can land during this update; put every surviving node
        // back where it was, now and again once the pending work has had a turn.
        await this.pinPositions(graph, placed);
        await new Promise<void>((resolve) => setTimeout(resolve, 0));
        await this.pinPositions(graph, placed);
      }
    });
  }

  setLayout(kind: LayoutKind, opts?: { focusKey?: NodeKey }): Promise<void> {
    return this.enqueue(async () => {
      this.layoutKind = kind;
      const graph = this.live();
      if (graph === null || graph.getNodeData().length === 0) {
        return;
      }
      await this.arrange(graph, opts?.focusKey ?? this.shown?.seedKey ?? null);
      await graph.fitView({ when: 'always' }, this.animation());
    });
  }

  focus(key: NodeKey): Promise<void> {
    return this.enqueue(async () => {
      const graph = this.live();
      if (graph === null || !graph.hasNode(key)) {
        return;
      }
      await graph.focusElement(key, this.animation());
    });
  }

  fitView(): Promise<void> {
    return this.enqueue(async () => {
      const graph = this.live();
      if (graph !== null && graph.getNodeData().length > 0) {
        await graph.fitView({ when: 'always' }, this.animation());
      }
    });
  }

  resize(): void {
    const graph = this.live();
    if (graph === null || this.container === null) {
      return;
    }
    const width = this.container.clientWidth;
    const height = this.container.clientHeight;
    const [current, currentHeight] = graph.getSize();
    // A hidden or collapsed container reports zero; resizing to that would blank the canvas.
    if (width > 0 && height > 0 && (width !== current || height !== currentHeight)) {
      graph.resize(width, height);
    }
  }

  setTheme(theme: GraphTheme): void {
    this.theme = theme;
    this.reduceMotion = theme.reduceMotion;
    if (this.container !== null) {
      this.container.style.backgroundColor = theme.background;
    }
    // Repaint from the view that is showing, so the change reaches every element and moves none.
    void this.enqueue(async () => {
      const graph = this.live();
      const view = this.shown;
      if (graph === null || view === null || view.nodes.length === 0) {
        return;
      }
      graph.updateNodeData(
        view.nodes.map((vn) => ({ id: vn.key, style: nodeStyle(paintNode(vn, theme), this.nodeLabel(vn), theme) })),
      );
      graph.updateEdgeData(
        view.edges.map((ve) => {
          const paint = paintEdge(ve, theme);
          return { id: ve.key, style: edgeStyle(paint, showEdgeLabel(paint, view.edges.length) ? this.edgeLabel(ve) : '', theme) };
        }),
      );
      await graph.draw();
    });
  }

  setHandlers(handlers: RendererHandlers): void {
    this.handlers = handlers;
  }

  positions(): ReadonlyMap<NodeKey, Point> {
    return this.nodePositions();
  }

  // ---- internals ---------------------------------------------------------------------------------------

  /** Runs `task` after every earlier one, and keeps the chain alive when one of them fails. */
  private enqueue<T>(task: () => Promise<T>): Promise<T> {
    const run = this.queue.then(() => (this.destroyed ? (undefined as T) : task()));
    this.queue = run.catch(() => undefined);
    return run;
  }

  private live(): Graph | null {
    return this.destroyed || this.graph === null || this.graph.destroyed ? null : this.graph;
  }

  private animation(): false | { duration: number } {
    return this.reduceMotion ? false : { duration: ANIMATION_MS };
  }

  private centre(): Point {
    const [width, height] = this.graph?.getSize() ?? [800, 600];
    return { x: width / 2, y: height / 2 };
  }

  private nodePositions(): Map<NodeKey, Point> {
    const result = new Map<NodeKey, Point>();
    const graph = this.live();
    if (graph === null) {
      return result;
    }
    for (const node of graph.getNodeData()) {
      const style = node.style ?? {};
      if (typeof style.x === 'number' && typeof style.y === 'number') {
        result.set(String(node.id), { x: style.x, y: style.y });
      }
    }
    return result;
  }

  /** Writes back the remembered position of every node that has drifted from it. */
  private async pinPositions(graph: Graph, remembered: ReadonlyMap<NodeKey, Point>): Promise<void> {
    if (this.live() !== graph) {
      return;
    }
    const now = this.nodePositions();
    const moved: NodeData[] = [];
    for (const [key, was] of remembered) {
      const is = now.get(key);
      if (is !== undefined && (Math.abs(is.x - was.x) > 0.5 || Math.abs(is.y - was.y) > 0.5)) {
        moved.push({ id: key, style: { x: was.x, y: was.y } });
      }
    }
    if (moved.length > 0) {
      graph.updateNodeData(moved);
      await graph.draw();
    }
  }

  /** Re-lays out every node with the current kind, around the focus node for the radial layout. */
  private async arrange(graph: Graph, focusKey: NodeKey | null): Promise<void> {
    const [width, height] = graph.getSize();
    const options = layoutOptions(this.layoutKind, { focusKey, width, height });
    // The library's layout option union is wider than the plain object built here.
    await graph.layout(options as unknown as Parameters<Graph['layout']>[0]);
  }

  private nodeLabel(vn: ViewNode): string {
    return this.labels?.node(vn) ?? '';
  }

  private edgeLabel(ve: ViewEdge): string {
    return this.labels?.edge(ve) ?? '';
  }

  private bindEvents(graph: Graph): void {
    const idOf = (e: unknown): string => String((e as { target: { id: string } }).target.id);
    // G6 also reports a right-click as a click; that belongs to the context menu alone.
    const primary = (e: unknown): boolean => ((e as { button?: number }).button ?? 0) === 0;
    graph.on(NodeEvent.CLICK, (e: unknown) => primary(e) && this.handlers.nodeClick?.(idOf(e)));
    graph.on(NodeEvent.DBLCLICK, (e: unknown) => primary(e) && this.handlers.nodeDoubleClick?.(idOf(e)));
    graph.on(EdgeEvent.CLICK, (e: unknown) => primary(e) && this.handlers.edgeClick?.(idOf(e)));
    graph.on(CanvasEvent.CLICK, (e: unknown) => primary(e) && this.handlers.canvasClick?.());
    graph.on(NodeEvent.CONTEXT_MENU, (e: unknown) => {
      const event = e as { client?: { x: number; y: number }; preventDefault?: () => void };
      event.preventDefault?.();
      const box = this.container?.getBoundingClientRect();
      this.handlers.nodeContextMenu?.(idOf(e), {
        x: (event.client?.x ?? 0) - (box?.left ?? 0),
        y: (event.client?.y ?? 0) - (box?.top ?? 0),
      });
    });
  }
}
