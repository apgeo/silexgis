import { describe, expect, it } from 'vitest';
import { LAYOUT_KINDS } from '../renderer.ts';
import { GRAPH_NODE_TYPES } from '../types.ts';
import { NODE_VISUALS, buildGraphTheme, paintEdge, paintNode } from '../visuals.ts';
import type { ViewEdge, ViewNode } from '../view.ts';
import { iconDataUri } from '../icons.ts';
import { EDGE_LABEL_LIMIT, G6_NODE_TYPE, edgeStyle, layoutOptions, nodeStyle, showEdgeLabel, truncateLabel } from './styleMap.ts';

const theme = buildGraphTheme({}, false, false);

const viewNode = (over: Partial<ViewNode> = {}): ViewNode => ({
  key: 'feature:1',
  kind: 'node',
  type: 'feature',
  node: null,
  state: 'normal',
  isSeed: false,
  restricted: false,
  expanded: false,
  more: null,
  ...over,
});

const viewEdge = (over: Partial<ViewEdge> = {}): ViewEdge => ({
  key: 'e1',
  category: 'attachment',
  relation: 'plan',
  relationName: null,
  inverseName: null,
  directed: true,
  source: 'a',
  target: 'b',
  anchorKind: null,
  link: null,
  state: 'normal',
  isMore: false,
  ...over,
});

describe('G6 style mapping', () => {
  it('has a G6 shape for every shape the visuals table uses', () => {
    for (const type of GRAPH_NODE_TYPES) {
      expect(G6_NODE_TYPE[NODE_VISUALS[type].shape]).toBeTruthy();
    }
  });

  it('draws a restricted placeholder hollow, dashed, with a lock in the text colour and no colour hint', () => {
    const style = nodeStyle(paintNode(viewNode({ restricted: true, type: 'document' }), theme), 'Restricted', theme);
    expect(style.fill).toBe(theme.background);
    expect((style.lineDash as number[]).length).toBeGreaterThan(0);
    expect(style.iconSrc).toBe(iconDataUri('lock', theme.textSecondary));
  });

  it('draws an ordinary node filled in its type colour with its white icon', () => {
    const style = nodeStyle(paintNode(viewNode(), theme), 'Cave S', theme);
    expect(style.fill).toBe(theme.preset.blue);
    expect(style.iconSrc).toBe(iconDataUri('cave', '#ffffff'));
    expect(style.iconWidth).toBeLessThan(style.size as number);
    expect(style.labelText).toBe('Cave S');
    // Lines passing under the name must not strike through it.
    expect(style.labelBackground).toBe(true);
    expect(style.labelBackgroundFill).toBe(theme.background);
  });

  it('fades the label with the node', () => {
    const style = nodeStyle(paintNode(viewNode({ state: 'faded' }), theme), 'Cave S', theme);
    expect(style.opacity).toBeLessThan(1);
    expect(style.labelOpacity).toBe(style.opacity);
  });

  it('truncates long labels and keeps short ones', () => {
    expect(truncateLabel('short')).toBe('short');
    const cut = truncateLabel('x'.repeat(80));
    expect([...cut]).toHaveLength(30);
    expect(cut.endsWith('…')).toBe(true);
  });

  it('draws an arrow for a directed edge and none for an undirected one', () => {
    expect(edgeStyle(paintEdge(viewEdge(), theme), '', theme).endArrow).toBe(true);
    expect(edgeStyle(paintEdge(viewEdge({ directed: false }), theme), '', theme).endArrow).toBe(false);
  });

  it('labels every edge of a small picture and only touched edges of a large one', () => {
    const normal = paintEdge(viewEdge(), theme);
    const touched = paintEdge(viewEdge({ state: 'neighbour' }), theme);
    expect(showEdgeLabel(normal, EDGE_LABEL_LIMIT)).toBe(true);
    expect(showEdgeLabel(normal, EDGE_LABEL_LIMIT + 1)).toBe(false);
    expect(showEdgeLabel(touched, EDGE_LABEL_LIMIT + 1)).toBe(true);
  });

  it('builds options for every layout kind, none animated', () => {
    for (const kind of LAYOUT_KINDS) {
      const options = layoutOptions(kind, { focusKey: 'feature:1', width: 800, height: 600 });
      expect(typeof options.type).toBe('string');
      expect(options.animation).toBe(false);
    }
    expect(layoutOptions('radial', { focusKey: 'feature:1', width: 1, height: 1 }).focusNode).toBe('feature:1');
  });
});
