// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import ro from '../i18n/locales/ro.json';
import { GRAPH_CATEGORIES, GRAPH_NODE_TYPES } from './types.ts';
import {
  CATEGORY_LABEL_KEYS,
  CATEGORY_VISUALS,
  NODE_TYPE_LABEL_KEYS,
  NODE_VISUALS,
  PRESET_NAMES,
  buildGraphTheme,
  categoryLabel,
  humanise,
  nodeLabel,
  nodeTypeLabel,
  nodeVisual,
  paintEdge,
  paintNode,
  relationLabel,
  type LabelDeps,
  type Translate,
} from './visuals.ts';
import type { ViewEdge, ViewNode } from './view.ts';

const light = buildGraphTheme({}, false, false);
const dark = buildGraphTheme({}, true, false);

function vn(over: Partial<ViewNode> = {}): ViewNode {
  return {
    key: 'feature:a',
    kind: 'node',
    type: 'feature',
    node: { key: 'feature:a', type: 'feature', id: 'a', title: 'Cave A', subtitle: null, kind: null, restricted: false, expandable: true, route: null, shortCode: null, memberCount: null },
    state: 'normal',
    isSeed: false,
    restricted: false,
    expanded: false,
    more: null,
    ...over,
  };
}

function ve(over: Partial<ViewEdge> = {}): ViewEdge {
  return { key: 'e', category: 'hierarchy', relation: 'parent', relationName: null, inverseName: null, directed: true, source: 'a', target: 'b', anchorKind: null, link: null, state: 'normal', isMore: false, ...over };
}

describe('the visual tables', () => {
  it('have a row for every node type and every category the client knows', () => {
    for (const type of GRAPH_NODE_TYPES) {
      expect(NODE_VISUALS[type], type).toBeDefined();
      expect(NODE_TYPE_LABEL_KEYS[type], type).toBeTruthy();
    }
    for (const category of GRAPH_CATEGORIES) {
      expect(CATEGORY_VISUALS[category], category).toBeDefined();
      expect(CATEGORY_LABEL_KEYS[category], category).toBeTruthy();
    }
    expect(Object.keys(NODE_VISUALS).sort()).toEqual([...GRAPH_NODE_TYPES].sort());
    expect(Object.keys(CATEGORY_VISUALS).sort()).toEqual([...GRAPH_CATEGORIES].sort());
  });

  it('tell every node type apart by its icon, and give every item type one', () => {
    for (const type of GRAPH_NODE_TYPES) {
      expect(PRESET_NAMES).toContain(NODE_VISUALS[type].preset);
    }
    const items = GRAPH_NODE_TYPES.filter((t) => t !== 'resLink');
    const icons = items.map((t) => NODE_VISUALS[t].icon);
    expect(icons).not.toContain(null);
    expect(new Set(icons).size).toBe(icons.length);
    // The hub is a junction, not an item: a small diamond with no icon.
    expect(NODE_VISUALS.resLink).toMatchObject({ shape: 'diamond', icon: null });
  });

  it('draw attachments and tags dashed, and the other categories solid', () => {
    expect(GRAPH_CATEGORIES.filter((c) => CATEGORY_VISUALS[c].dashed).sort()).toEqual(['attachment', 'tag']);
  });

  it('draw a cave entrance as a smaller circle than a cave', () => {
    const cave = nodeVisual({ kind: 'node', type: 'feature', isSeed: false, node: { kind: 'cave' } });
    const entrance = nodeVisual({ kind: 'node', type: 'feature', isSeed: false, node: { kind: 'caveEntrance' } });
    expect(entrance.shape).toBe('circle');
    expect(entrance.size).toBeLessThan(cave.size);
    expect(entrance.icon).toBe('entrance');
  });

  it('keep the cave icon for caves alone: a survey line or any other feature carries its own', () => {
    const icon = (kind: string | null) => nodeVisual({ kind: 'node', type: 'feature', isSeed: false, node: { kind } }).icon;
    expect(icon('cave')).toBe('cave');
    expect(icon('centerline')).toBe('surveyLine');
    expect(icon('generic')).toBe('place');
    // A feature whose kind is not told looks like the type's own.
    expect(icon(null)).toBe('cave');
  });

  it('fall back to a generic look for an unknown type and category', () => {
    expect(nodeVisual({ kind: 'node', type: 'hologram', isSeed: false }).shape).toBe('circle');
    expect(paintEdge(ve({ category: 'telepathy' }), light).stroke).toBe(light.preset.neutral);
  });
});

describe('theme', () => {
  it('reads antd tokens by name and falls back when a token is missing', () => {
    const t = buildGraphTheme({ colorBgContainer: '#111', colorText: '#eee', blue6: '#0000ff', colorPrimary: '#123456' }, true, true);
    expect(t).toMatchObject({ dark: true, reduceMotion: true, background: '#111', text: '#eee', selected: '#123456' });
    expect(t.preset.blue).toBe('#0000ff');
    expect(t.preset.green).toBe(light.preset.green);
  });

  it('differs between light and dark where it must (the canvas) and defines every preset in both', () => {
    expect(light.background).not.toBe(dark.background);
    for (const name of PRESET_NAMES) {
      expect(light.preset[name]).toBeTruthy();
      expect(dark.preset[name]).toBeTruthy();
    }
  });
});

describe('paint', () => {
  it('draws a restricted placeholder as a dashed hole with a lock, in no type colour', () => {
    const ph = paintNode(vn({ restricted: true, node: { ...vn().node!, restricted: true, title: null } }), light);
    expect(ph).toMatchObject({ dashed: true, icon: 'lock', iconOnFill: false, fill: light.background });
    expect(Object.values(light.preset)).not.toContain(ph.fill);
  });

  it('gives each state its own treatment', () => {
    const normal = paintNode(vn(), light);
    const selected = paintNode(vn({ state: 'selected' }), light);
    const faded = paintNode(vn({ state: 'faded' }), light);
    expect(selected.stroke).toBe(light.selected);
    expect(selected.strokeWidth).toBeGreaterThan(normal.strokeWidth);
    expect(faded.opacity).toBeLessThan(normal.opacity);
    expect(paintNode(vn({ state: 'neighbour' }), light).strokeWidth).toBeGreaterThan(normal.strokeWidth);
  });

  it('draws the seed larger and a "+N more" stub as a ring', () => {
    expect(paintNode(vn({ isSeed: true }), light).size).toBeGreaterThan(paintNode(vn(), light).size);
    const stub = paintNode(vn({ kind: 'more', type: 'more', node: null }), light);
    expect(stub).toMatchObject({ dashed: true, icon: 'more', iconOnFill: false });
  });

  it('puts an arrow on directed edges only, never on a stub edge, and lights a selected one', () => {
    expect(paintEdge(ve(), light).arrow).toBe(true);
    expect(paintEdge(ve({ directed: false }), light).arrow).toBe(false);
    expect(paintEdge(ve({ isMore: true }), light)).toMatchObject({ arrow: false, dashed: true });
    expect(paintEdge(ve({ state: 'selected' }), light)).toMatchObject({ stroke: light.selected, width: 3.5 });
    expect(paintEdge(ve({ state: 'faded' }), light).opacity).toBeLessThan(1);
    expect(paintEdge(ve({ category: 'tag' }), light).dashed).toBe(true);
  });
});

describe('text', () => {
  const t: Translate = (key, options) => {
    const k = Array.isArray(key) ? key[0] : key;
    return options && 'count' in options ? `${k}#${String(options.count)}` : options && 'defaultValue' in options && !k.endsWith('.name') ? `${k}|${String(options.defaultValue)}` : k;
  };
  const deps: LabelDeps = { t, reslinkPhrase: (relation, reading) => `${relation?.code ?? 'none'}/${reading}` };

  it('labels a placeholder "Restricted" whatever it stands for, a hub by its short code, a stub by its count', () => {
    expect(nodeLabel(vn({ restricted: true }), t)).toBe('graph.restricted');
    expect(nodeLabel(vn({ kind: 'hub', type: 'resLink', node: { ...vn().node!, shortCode: 'AB12' } }), t)).toBe('AB12');
    expect(nodeLabel(vn({ kind: 'more', type: 'more', node: null, more: { nodeKey: 'x', category: 'tag', remaining: 7 } }), t)).toBe('graph.more#7');
    expect(nodeLabel(vn(), t)).toBe('Cave A');
    expect(nodeLabel(vn({ node: { ...vn().node!, title: '' } }), t)).toBe('graph.untitled');
  });

  it('names types and categories, with a readable fallback for unknown ones', () => {
    expect(nodeTypeLabel('tripLog', t)).toBe('resLinks.targetTypes.tripLog');
    expect(nodeTypeLabel('tag', t)).toBe('graph.types.tag');
    expect(nodeTypeLabel('hologram', t)).toBe('resLinks.targetTypes.unknown');
    expect(categoryLabel('groupMembership', t)).toBe('graph.categories.groupMembership');
    expect(categoryLabel('someNewThing', t)).toBe('Some new thing');
  });

  it('words a resource-link relation through the injected phrase and the others through graph.relations', () => {
    expect(relationLabel(ve({ category: 'resLink', relation: 'documents' }), 'inverse', deps)).toBe('documents/inverse');
    expect(relationLabel(ve({ category: 'resLink', relation: 'unspecified' }), 'forward', deps)).toBe('none/forward');
    expect(relationLabel(ve({ category: 'cabinet', relation: 'parent' }), 'forward', deps)).toBe('graph.relations.cabinet.parent.name');
    expect(relationLabel(ve({ category: 'cabinet', relation: 'parent' }), 'inverse', deps)).toBe('graph.relations.cabinet.parent.inverse|Parent');
    expect(relationLabel(ve({ isMore: true }), 'forward', deps)).toBe('');
  });

  it('spaces out a camel-case code', () => {
    expect(humanise('tripParticipant')).toBe('Trip participant');
    expect(humanise('co_leader')).toBe('Co leader');
    expect(humanise('')).toBe('');
  });
});

describe('relation wording of the closed vocabularies', () => {
  // The seeded feature-link kinds; the other three vocabularies are read from the locale families
  // that already word the same codes elsewhere in the application.
  const FEATURE_LINK_KINDS = ['associated_cave', 'hydro_connection', 'same_system', 'related'];
  const vocabularies: Record<string, string[]> = {
    featureLink: FEATURE_LINK_KINDS,
    attachment: Object.keys(en.attachments.roles),
    tripParticipant: Object.keys(en.trips.participantRoleValues),
    groupMembership: Object.keys(en.cavingGroups.roles),
  };
  for (const [lang, bundle] of [['en', en], ['ro', ro]] as const) {
    it(`has a name and an inverse in ${lang} for every code of every category`, () => {
      const relations = bundle.graph.relations as unknown as Record<string, Record<string, { name?: string; inverse?: string }>>;
      for (const [category, codes] of Object.entries(vocabularies)) {
        for (const code of codes) {
          const entry = relations[category]?.[code];
          expect(entry?.name, `${lang} ${category}.${code}.name`).toBeTruthy();
          expect(entry?.inverse, `${lang} ${category}.${code}.inverse`).toBeTruthy();
        }
      }
    });
  }
});
