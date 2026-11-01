// SPDX-License-Identifier: AGPL-3.0-or-later
import type { LayoutKind } from '../renderer.ts';
import type { NodeKey } from '../types.ts';
import type { EdgePaint, GraphTheme, NodePaint, NodeShape } from '../visuals.ts';

/**
 * Translates the library-neutral paint values (`paintNode`, `paintEdge`) into G6 style options.
 * Kept free of the library import so the mapping is unit-testable without a canvas: the objects
 * returned here are plain data that `G6Renderer` hands to the graph.
 */

/** G6 draws each of these shapes under the same name. */
export const G6_NODE_TYPE: Record<NodeShape, string> = {
  circle: 'circle',
  ellipse: 'ellipse',
  rect: 'rect',
  triangle: 'triangle',
  diamond: 'diamond',
  hexagon: 'hexagon',
  star: 'star',
};

const LABEL_LIMIT = 30;

/** Long titles are cut so a label never spans half the canvas; the full title is in the list view and drawer. */
export function truncateLabel(text: string, limit = LABEL_LIMIT): string {
  const chars = [...text];
  return chars.length <= limit ? text : `${chars.slice(0, limit - 1).join('')}…`;
}

/** The width and height of a node of the given shape: a few shapes are not square. */
export function nodeSize(shape: NodeShape, size: number): number | [number, number] {
  switch (shape) {
    case 'ellipse':
      return [Math.round(size * 1.3), Math.round(size * 0.85)];
    case 'rect':
      return [Math.round(size * 1.2), Math.round(size * 0.85)];
    default:
      return size;
  }
}

export type G6Style = Record<string, unknown>;

export function nodeStyle(paint: NodePaint, label: string, theme: GraphTheme): G6Style {
  const hasGlyph = paint.glyph !== '';
  const style: G6Style = {
    size: nodeSize(paint.shape, paint.size),
    fill: paint.fill,
    stroke: paint.stroke,
    lineWidth: paint.strokeWidth,
    lineDash: paint.dashed ? [4, 3] : [],
    opacity: paint.opacity,
    // A placeholder or "+N more" stub is hollow, so its glyph is drawn in the text colour, not white.
    icon: hasGlyph,
    iconText: paint.glyph,
    iconFill: paint.dashed ? theme.textSecondary : '#ffffff',
    iconFontSize: Math.round(paint.size * 0.45),
    iconFontWeight: 600,
    iconOpacity: paint.opacity,
    label: label !== '',
    labelText: truncateLabel(label),
    labelFill: paint.labelColor,
    labelFontSize: 12,
    labelPlacement: 'bottom',
    labelOpacity: paint.opacity,
    // A backing in the canvas colour keeps lines that pass under a name from striking through it.
    labelBackground: label !== '',
    labelBackgroundFill: theme.background,
    labelBackgroundOpacity: 0.85 * paint.opacity,
    badge: paint.lock,
    // A plain geometric mark rather than a padlock emoji: emoji need a colour-emoji font that many
    // machines lack and would draw as an empty box. U+25A0 exists in every ordinary text font.
    badges: paint.lock
      ? [{ text: '\u25A0', placement: 'right-top', fontSize: 9, fill: theme.textSecondary, backgroundFill: 'transparent' }]
      : [],
    cursor: 'pointer',
  };
  return style;
}

export function edgeStyle(paint: EdgePaint, label: string, theme: GraphTheme): G6Style {
  return {
    stroke: paint.stroke,
    lineWidth: paint.width,
    lineDash: paint.dashed ? [5, 4] : [],
    opacity: paint.opacity,
    endArrow: paint.arrow,
    label: label !== '',
    labelText: truncateLabel(label, 24),
    labelFill: theme.textSecondary,
    labelFontSize: 10,
    labelBackground: true,
    labelBackgroundFill: theme.background,
    labelBackgroundOpacity: 0.85,
    labelOpacity: paint.opacity,
    cursor: 'pointer',
  };
}

/**
 * Edge labels on every line are clutter even on a modest neighbourhood (a dozen lines already hide
 * the node names), so past this many edges only the lines touching the selection are labelled. The
 * relation names of every line stay readable in the list view, and selecting a node names its own.
 */
export const EDGE_LABEL_LIMIT = 6;

export const showEdgeLabel = (paint: Pick<EdgePaint, 'state'>, edgeCount: number): boolean =>
  edgeCount <= EDGE_LABEL_LIMIT || paint.state === 'selected' || paint.state === 'neighbour';

export interface LayoutContext {
  /** The node the layout is built around (radial) — the seed unless the user asked for another. */
  focusKey: NodeKey | null;
  width: number;
  height: number;
}

/**
 * The options handed to `graph.layout(...)`. Every layout runs without animation: it is a
 * deliberate re-arrangement and the result matters, not the way there. The force layout is bounded
 * by `iterations` so a large neighbourhood cannot keep the main thread busy for long.
 */
export function layoutOptions(kind: LayoutKind, ctx: LayoutContext): G6Style {
  switch (kind) {
    case 'radial':
      return {
        type: 'radial',
        animation: false,
        focusNode: ctx.focusKey,
        linkDistance: 150,
        unitRadius: 150,
        preventOverlap: true,
        nodeSize: 52,
        nodeSpacing: 12,
        strictRadial: false,
        maxIteration: 200,
      };
    case 'layered':
      return { type: 'antv-dagre', animation: false, rankdir: 'LR', nodesep: 36, ranksep: 110 };
    case 'force':
    default:
      return {
        type: 'd3-force',
        animation: false,
        iterations: 300,
        link: { distance: 120, strength: 0.7 },
        manyBody: { strength: -320 },
        collide: { radius: 38, strength: 0.9 },
        center: { x: ctx.width / 2, y: ctx.height / 2, strength: 0.05 },
      };
  }
}
