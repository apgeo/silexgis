// SPDX-License-Identifier: AGPL-3.0-or-later
import { iconDataUri } from '../icons.ts';
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
  diamond: 'diamond',
};

const LABEL_LIMIT = 30;

/** Long titles are cut so a label never spans half the canvas; the full title is in the list view and drawer. */
export function truncateLabel(text: string, limit = LABEL_LIMIT): string {
  const chars = [...text];
  return chars.length <= limit ? text : `${chars.slice(0, limit - 1).join('')}…`;
}

/** The icon fills this share of its badge, leaving a rim of the type's colour around it. */
export const ICON_SHARE = 0.58;

export type G6Style = Record<string, unknown>;

export function nodeStyle(paint: NodePaint, label: string, theme: GraphTheme): G6Style {
  const iconSide = Math.round(paint.size * ICON_SHARE);
  const style: G6Style = {
    size: paint.size,
    fill: paint.fill,
    stroke: paint.stroke,
    lineWidth: paint.strokeWidth,
    lineDash: paint.dashed ? [4, 3] : [],
    opacity: paint.opacity,
    // The icon is drawn as an image; on a hollow placeholder or "+N more" ring it takes the text
    // colour, since white would vanish on the canvas.
    icon: paint.icon !== null,
    iconSrc: paint.icon === null ? undefined : iconDataUri(paint.icon, paint.iconOnFill ? '#ffffff' : theme.textSecondary),
    iconWidth: iconSide,
    iconHeight: iconSide,
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
