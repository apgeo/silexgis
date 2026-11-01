// SPDX-License-Identifier: AGPL-3.0-or-later
import type { NodeKey } from './types.ts';
import type { GraphTheme } from './visuals.ts';
import type { GraphView, ViewEdge, ViewNode } from './view.ts';

export type LayoutKind = 'force' | 'radial' | 'layered';
export const LAYOUT_KINDS: readonly LayoutKind[] = ['force', 'radial', 'layered'];

export interface Point {
  x: number;
  y: number;
}

export interface RendererHandlers {
  nodeClick?: (key: NodeKey) => void;
  nodeDoubleClick?: (key: NodeKey) => void;
  edgeClick?: (key: string) => void;
  canvasClick?: () => void;
  /** `at` is in viewport pixels relative to the container, where a menu would open. */
  nodeContextMenu?: (key: NodeKey, at: Point) => void;
}

export interface RendererMountOptions {
  theme: GraphTheme;
  reduceMotion: boolean;
  /** The arrangement for the first picture; the explorer's current choice. Defaults to `force`. */
  layout?: LayoutKind;
}

/**
 * The words on the canvas. A renderer draws text but has no translations, so the page hands it these
 * (built from `nodeLabel` / `relationLabel` with the current `t`). A new object is a new language or
 * a new wording: the renderer repaints every label when the object it was given last is replaced.
 */
export interface GraphLabels {
  node(node: ViewNode): string;
  /** The label of an edge read from its source (the forward reading). */
  edge(edge: ViewEdge): string;
}

/**
 * What the explorer needs from a drawing library, and nothing of the library's own vocabulary.
 * The model decides what is visible and how it is highlighted; a renderer only draws it, keeps
 * the picture steady between updates, and reports what the user touched. A second renderer
 * (Cytoscape.js, say) implements this and the page, the store and the list view do not change.
 */
export interface GraphRenderer {
  mount(container: HTMLElement, options: RendererMountOptions): Promise<void>;
  /**
   * Shows this view. The renderer diffs it against what it shows (see `diffView`): existing nodes
   * KEEP their positions, new nodes are placed near `anchorKey` — the node just expanded — when
   * it is given. `labels` is the text to draw; keep passing the same object while the wording is
   * unchanged. The first view with nodes is also laid out and fitted to the viewport.
   */
  setView(view: GraphView, opts?: { anchorKey?: NodeKey; labels?: GraphLabels }): Promise<void>;
  /** Re-lays out everything. Positions change, deliberately. */
  setLayout(kind: LayoutKind, opts?: { focusKey?: NodeKey }): Promise<void>;
  /** Animates the node to the centre of the viewport. */
  focus(key: NodeKey): Promise<void>;
  fitView(): Promise<void>;
  /** The container changed size: match the drawing surface to it. Cheap when nothing changed. */
  resize(): void;
  /** Repaints with new colours without remounting or moving anything. */
  setTheme(theme: GraphTheme): void;
  setHandlers(handlers: RendererHandlers): void;
  /** Where each node is now, in graph coordinates; for tests and stability checks. */
  positions(): ReadonlyMap<NodeKey, Point>;
  destroy(): void;
}

// ---- the diff every renderer works from ----------------------------------------------------------------

export interface ViewDiff {
  addedNodes: ViewNode[];
  removedNodes: NodeKey[];
  /** Nodes present before and after whose painted properties differ (state, label inputs, expansion). */
  changedNodes: ViewNode[];
  addedEdges: ViewEdge[];
  removedEdges: string[];
  changedEdges: ViewEdge[];
}

const sameNode = (a: ViewNode, b: ViewNode): boolean =>
  a.state === b.state &&
  a.expanded === b.expanded &&
  a.isSeed === b.isSeed &&
  a.node?.title === b.node?.title &&
  a.more?.remaining === b.more?.remaining;

const sameEdge = (a: ViewEdge, b: ViewEdge): boolean =>
  a.state === b.state &&
  a.source === b.source &&
  a.target === b.target &&
  a.relation === b.relation &&
  a.directed === b.directed;

/**
 * What changed between two views. Renderers apply this instead of redrawing, which is what keeps
 * existing nodes where they are when the graph grows: only `addedNodes` need a position.
 */
export function diffView(previous: GraphView | null, next: GraphView): ViewDiff {
  const before = new Map((previous?.nodes ?? []).map((n) => [n.key, n]));
  const beforeEdges = new Map((previous?.edges ?? []).map((e) => [e.key, e]));
  const diff: ViewDiff = { addedNodes: [], removedNodes: [], changedNodes: [], addedEdges: [], removedEdges: [], changedEdges: [] };
  const nextNodeKeys = new Set<NodeKey>();
  for (const node of next.nodes) {
    nextNodeKeys.add(node.key);
    const old = before.get(node.key);
    if (old === undefined) {
      diff.addedNodes.push(node);
    } else if (!sameNode(old, node)) {
      diff.changedNodes.push(node);
    }
  }
  diff.removedNodes = [...before.keys()].filter((key) => !nextNodeKeys.has(key));
  const nextEdgeKeys = new Set<string>();
  for (const edge of next.edges) {
    nextEdgeKeys.add(edge.key);
    const old = beforeEdges.get(edge.key);
    if (old === undefined) {
      diff.addedEdges.push(edge);
    } else if (!sameEdge(old, edge)) {
      diff.changedEdges.push(edge);
    }
  }
  diff.removedEdges = [...beforeEdges.keys()].filter((key) => !nextEdgeKeys.has(key));
  return diff;
}

export const isEmptyDiff = (d: ViewDiff): boolean =>
  d.addedNodes.length + d.removedNodes.length + d.changedNodes.length +
    d.addedEdges.length + d.removedEdges.length + d.changedEdges.length === 0;
