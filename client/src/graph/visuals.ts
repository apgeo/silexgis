// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphIcon } from './icons.ts';
import type { GraphCategory, GraphNodeType, HighlightState } from './types.ts';
import type { ViewEdge, ViewNode } from './view.ts';

/**
 * What the graph looks like, as data: which icon and colour each kind of node and each relation
 * category gets, and how a highlight state changes it. The tables are closed over the type and
 * category lists (a test fails when a new entry in either has no row here), and everything an
 * unknown value falls back to is explicit, so a newer server never breaks the page.
 *
 * Nothing here knows a drawing library. A renderer reads `paintNode` / `paintEdge` and translates
 * the plain values it gets into its own style options.
 */

/**
 * Every item is a round badge carrying an icon; only a resource link's hub, which is a junction
 * rather than an item, is a small diamond without one.
 */
export type NodeShape = 'circle' | 'diamond';

/** The antd preset colours the graph draws with, plus a neutral for things that are not an item of a kind. */
export const PRESET_NAMES = [
  'blue',
  'green',
  'purple',
  'orange',
  'magenta',
  'cyan',
  'gold',
  'volcano',
  'geekblue',
  'lime',
  'neutral',
] as const;
export type PresetName = (typeof PRESET_NAMES)[number];

export interface NodeVisual {
  shape: NodeShape;
  preset: PresetName;
  /** The pictogram drawn inside the node, so a type reads without its colour; none on a hub. */
  icon: GraphIcon | null;
  size: number;
}

export const NODE_VISUALS: Record<GraphNodeType, NodeVisual> = {
  feature: { shape: 'circle', preset: 'blue', icon: 'cave', size: 34 },
  document: { shape: 'circle', preset: 'orange', icon: 'document', size: 32 },
  tripLog: { shape: 'circle', preset: 'green', icon: 'trip', size: 32 },
  caver: { shape: 'circle', preset: 'magenta', icon: 'person', size: 32 },
  cavingGroup: { shape: 'circle', preset: 'purple', icon: 'group', size: 34 },
  mapView: { shape: 'circle', preset: 'cyan', icon: 'map', size: 32 },
  surveyModel: { shape: 'circle', preset: 'geekblue', icon: 'model', size: 32 },
  geofile: { shape: 'circle', preset: 'lime', icon: 'layers', size: 32 },
  cabinet: { shape: 'circle', preset: 'gold', icon: 'cabinet', size: 32 },
  expedition: { shape: 'circle', preset: 'volcano', icon: 'tent', size: 34 },
  tag: { shape: 'circle', preset: 'neutral', icon: 'tag', size: 28 },
  resLink: { shape: 'diamond', preset: 'purple', icon: null, size: 16 },
  file: { shape: 'circle', preset: 'neutral', icon: 'attachment', size: 26 },
};

/** A feature of kind cave entrance: the feature's colour, a smaller badge with the entrance icon. */
export const ENTRANCE_NODE_VISUAL: NodeVisual = { shape: 'circle', preset: 'blue', icon: 'entrance', size: 28 };

/** A survey line (centerline) feature: the feature's colour, survey legs instead of the cave. */
export const CENTERLINE_NODE_VISUAL: NodeVisual = { shape: 'circle', preset: 'blue', icon: 'surveyLine', size: 28 };
/** Any other feature (a bivouac, a sinkhole, a spring…): the feature's colour, a map pin. */
export const GENERIC_FEATURE_NODE_VISUAL: NodeVisual = { shape: 'circle', preset: 'blue', icon: 'place', size: 28 };

/** A type this client has never heard of. */
export const UNKNOWN_NODE_VISUAL: NodeVisual = { shape: 'circle', preset: 'neutral', icon: 'unknown', size: 28 };
/** The "+N more" stub: a small ring that reads as "there is more here". */
export const MORE_NODE_VISUAL: NodeVisual = { shape: 'circle', preset: 'neutral', icon: 'more', size: 28 };
/** The seed is drawn larger than its type's usual size, so the page's subject is found at a glance. */
const SEED_SCALE = 1.25;

export interface EdgeVisual {
  preset: PresetName;
  dashed: boolean;
}

export const CATEGORY_VISUALS: Record<GraphCategory, EdgeVisual> = {
  resLink: { preset: 'purple', dashed: false },
  featureLink: { preset: 'blue', dashed: false },
  hierarchy: { preset: 'cyan', dashed: false },
  attachment: { preset: 'orange', dashed: true },
  tripParticipant: { preset: 'green', dashed: false },
  expedition: { preset: 'volcano', dashed: false },
  cabinet: { preset: 'gold', dashed: false },
  tag: { preset: 'neutral', dashed: true },
  groupMembership: { preset: 'magenta', dashed: false },
  survey: { preset: 'lime', dashed: false },
};

export const UNKNOWN_EDGE_VISUAL: EdgeVisual = { preset: 'neutral', dashed: false };

/** Translation keys, written out so the key scanners see every one. */
export const NODE_TYPE_LABEL_KEYS: Record<GraphNodeType, string> = {
  feature: 'resLinks.targetTypes.feature',
  document: 'resLinks.targetTypes.document',
  tripLog: 'resLinks.targetTypes.tripLog',
  caver: 'resLinks.targetTypes.caver',
  cavingGroup: 'resLinks.targetTypes.cavingGroup',
  mapView: 'resLinks.targetTypes.mapView',
  surveyModel: 'resLinks.targetTypes.surveyModel',
  geofile: 'resLinks.targetTypes.geofile',
  cabinet: 'resLinks.targetTypes.cabinet',
  expedition: 'resLinks.targetTypes.expedition',
  tag: 'graph.types.tag',
  resLink: 'graph.types.resLink',
  file: 'graph.types.file',
};
export const UNKNOWN_TYPE_LABEL_KEY = 'resLinks.targetTypes.unknown';

export const CATEGORY_LABEL_KEYS: Record<GraphCategory, string> = {
  resLink: 'graph.categories.resLink',
  featureLink: 'graph.categories.featureLink',
  hierarchy: 'graph.categories.hierarchy',
  attachment: 'graph.categories.attachment',
  tripParticipant: 'graph.categories.tripParticipant',
  expedition: 'graph.categories.expedition',
  cabinet: 'graph.categories.cabinet',
  tag: 'graph.categories.tag',
  groupMembership: 'graph.categories.groupMembership',
  survey: 'graph.categories.survey',
};

export const nodeTypeVisual = (type: string): NodeVisual =>
  (NODE_VISUALS as Record<string, NodeVisual | undefined>)[type] ?? UNKNOWN_NODE_VISUAL;

export const categoryVisual = (category: string): EdgeVisual =>
  (CATEGORY_VISUALS as Record<string, EdgeVisual | undefined>)[category] ?? UNKNOWN_EDGE_VISUAL;

function featureVisual(kind: string | null): NodeVisual {
  switch (kind) {
    case 'caveEntrance':
      return ENTRANCE_NODE_VISUAL;
    case 'centerline':
      return CENTERLINE_NODE_VISUAL;
    case 'generic':
      return GENERIC_FEATURE_NODE_VISUAL;
    default:
      // A cave, or a placeholder whose kind is not disclosed.
      return nodeTypeVisual('feature');
  }
}

/** The visual of one node in a view: a stub, a placeholder and the seed each differ from their type's. */
export function nodeVisual(vn: Pick<ViewNode, 'kind' | 'type' | 'isSeed'> & { node?: { kind: string | null } | null }): NodeVisual {
  if (vn.kind === 'more') {
    return MORE_NODE_VISUAL;
  }
  // Only a cave carries the cave icon: an entrance, a survey line or any other feature is drawn in
  // the same colour but smaller and with its own icon, so a neighbourhood does not show a bivouac
  // or a sinkhole as one more cave.
  const base = vn.type === 'feature' ? featureVisual(vn.node?.kind ?? null) : nodeTypeVisual(vn.type);
  return vn.isSeed ? { ...base, size: Math.round(base.size * SEED_SCALE) } : base;
}

// ---- theme ---------------------------------------------------------------------------------------------

export interface GraphTheme {
  dark: boolean;
  reduceMotion: boolean;
  /** The canvas colour; also the fill of a restricted placeholder, so it reads as a hole. */
  background: string;
  text: string;
  textSecondary: string;
  border: string;
  /** The ring of the selected element. */
  selected: string;
  preset: Record<PresetName, string>;
}

const FALLBACK_PRESET: Record<PresetName, string> = {
  blue: '#1677ff',
  green: '#52c41a',
  purple: '#722ed1',
  orange: '#fa8c16',
  magenta: '#eb2f96',
  cyan: '#13c2c2',
  gold: '#faad14',
  volcano: '#fa541c',
  geekblue: '#2f54eb',
  lime: '#a0d911',
  neutral: '#8c8c8c',
};

const text = (token: Readonly<Record<string, unknown>>, name: string, fallback: string): string => {
  const value = token[name];
  return typeof value === 'string' && value !== '' ? value : fallback;
};

/**
 * The look for the current antd theme. `token` is the object `theme.useToken()` returns; it is read
 * by name and with fallbacks so this stays a plain function that needs no component library to run
 * or to test. A canvas cannot use CSS variables, so a theme change builds a new GraphTheme and
 * hands it to the renderer.
 */
export function buildGraphTheme(token: Readonly<Record<string, unknown>>, dark: boolean, reduceMotion: boolean): GraphTheme {
  const preset = { ...FALLBACK_PRESET };
  for (const name of PRESET_NAMES) {
    if (name !== 'neutral') {
      preset[name] = text(token, `${name}6`, FALLBACK_PRESET[name]);
    }
  }
  preset.neutral = text(token, 'colorTextQuaternary', dark ? '#6b6b6b' : FALLBACK_PRESET.neutral);
  return {
    dark,
    reduceMotion,
    background: text(token, 'colorBgContainer', dark ? '#141414' : '#ffffff'),
    text: text(token, 'colorText', dark ? '#ffffffd9' : '#000000e0'),
    textSecondary: text(token, 'colorTextSecondary', dark ? '#ffffffa6' : '#00000073'),
    border: text(token, 'colorBorder', dark ? '#424242' : '#d9d9d9'),
    selected: text(token, 'colorPrimary', FALLBACK_PRESET.blue),
    preset,
  };
}

// ---- paint ---------------------------------------------------------------------------------------------

export interface NodePaint {
  shape: NodeShape;
  size: number;
  fill: string;
  stroke: string;
  strokeWidth: number;
  dashed: boolean;
  opacity: number;
  /**
   * The pictogram, and whether it sits on the type's colour (drawn white) or on a hollow ring (drawn
   * in the secondary text colour). A restricted placeholder carries a lock, never its type's icon.
   */
  icon: GraphIcon | null;
  iconOnFill: boolean;
  labelColor: string;
  state: HighlightState;
}

export interface EdgePaint {
  stroke: string;
  width: number;
  dashed: boolean;
  opacity: number;
  /** Draw an arrowhead at the target. */
  arrow: boolean;
  state: HighlightState;
}

export const FADED_OPACITY = 0.2;

export function paintNode(vn: ViewNode, theme: GraphTheme): NodePaint {
  const visual = nodeVisual(vn);
  const color = theme.preset[visual.preset];
  const placeholder = vn.restricted;
  const stub = vn.kind === 'more';
  const ring =
    vn.state === 'selected'
      ? { stroke: theme.selected, strokeWidth: 4 }
      : vn.state === 'neighbour'
        ? { stroke: theme.text, strokeWidth: 2 }
        : { stroke: placeholder || stub ? theme.preset.neutral : theme.background, strokeWidth: placeholder || stub ? 2 : 1.5 };
  return {
    shape: visual.shape,
    size: visual.size,
    // A placeholder is a hole in the colour scheme: it must not suggest what it is by colour.
    fill: placeholder || stub ? theme.background : color,
    stroke: ring.stroke,
    strokeWidth: ring.strokeWidth,
    dashed: placeholder || stub,
    opacity: vn.state === 'faded' ? FADED_OPACITY : 1,
    icon: placeholder ? 'lock' : visual.icon,
    iconOnFill: !(placeholder || stub),
    labelColor: theme.text,
    state: vn.state,
  };
}

export function paintEdge(edge: ViewEdge, theme: GraphTheme): EdgePaint {
  const visual = edge.isMore ? { preset: 'neutral' as const, dashed: true } : categoryVisual(edge.category);
  return {
    stroke: edge.state === 'selected' ? theme.selected : theme.preset[visual.preset],
    width: edge.state === 'selected' ? 3.5 : edge.state === 'neighbour' ? 2.5 : 1.5,
    dashed: visual.dashed,
    opacity: edge.state === 'faded' ? FADED_OPACITY : 1,
    arrow: edge.directed && !edge.isMore,
    state: edge.state,
  };
}

// ---- text ----------------------------------------------------------------------------------------------

export type Translate = (key: string | string[], options?: Record<string, unknown>) => string;
export type Reading = 'forward' | 'inverse';

/** The relation of a resource link, as far as the edge knows it; null when the link has none. */
export interface RelationRef {
  code: string;
  directed: boolean;
  name: string | null;
  inverseName: string | null;
}

export interface LabelDeps {
  t: Translate;
  /**
   * The wording of a resource-link relation in either reading. The component supplies the
   * application's own relation wording; keeping it injected keeps this module free of the
   * component tree and of i18next.
   */
  reslinkPhrase: (relation: RelationRef | null, reading: Reading) => string;
}

/** "tripParticipant" -> "Trip participant": the last resort for a relation code nobody has worded. */
export function humanise(code: string): string {
  const spaced = code.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').trim().toLowerCase();
  return spaced === '' ? code : spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export function nodeTypeLabel(type: string, t: Translate): string {
  return t((NODE_TYPE_LABEL_KEYS as Record<string, string | undefined>)[type] ?? UNKNOWN_TYPE_LABEL_KEY);
}

export function categoryLabel(category: string, t: Translate): string {
  const key = (CATEGORY_LABEL_KEYS as Record<string, string | undefined>)[category];
  return key === undefined ? humanise(category) : t(key);
}

/** The text drawn beside a node. A placeholder is always "Restricted" and never says what it stands for. */
export function nodeLabel(vn: ViewNode, t: Translate): string {
  if (vn.kind === 'more' && vn.more !== null) {
    return t('graph.more', { count: vn.more.remaining });
  }
  if (vn.restricted) {
    return t('graph.restricted');
  }
  if (vn.kind === 'hub') {
    return vn.node?.shortCode ?? '';
  }
  const title = vn.node?.title;
  return title === null || title === undefined || title === '' ? t('graph.untitled') : title;
}

/**
 * What an edge reads as from one end. A resource link uses the application's relation wording; the
 * other categories use `graph.relations.<category>.<code>` and fall back to the code, spaced out,
 * so a relation code the locale files have not caught up with still shows something readable.
 */
export function relationLabel(edge: Pick<ViewEdge, 'category' | 'relation' | 'directed' | 'relationName' | 'inverseName' | 'isMore'>, reading: Reading, deps: LabelDeps): string {
  if (edge.isMore) {
    return '';
  }
  if (edge.category === 'resLink') {
    const relation: RelationRef | null =
      edge.relation === 'unspecified'
        ? null
        : { code: edge.relation, directed: edge.directed, name: edge.relationName, inverseName: edge.inverseName };
    return deps.reslinkPhrase(relation, reading);
  }
  const { category, relation } = edge;
  const fallback = humanise(relation);
  return reading === 'inverse' && edge.directed
    ? deps.t([`graph.relations.${category}.${relation}.inverse`, `graph.relations.${category}.${relation}.name`], { defaultValue: fallback })
    : deps.t(`graph.relations.${category}.${relation}.name`, { defaultValue: fallback });
}
