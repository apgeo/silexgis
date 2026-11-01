// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { toggleCategory, toggleRelation, toggleType, noFilters } from './filters.ts';
import { GraphModel } from './graphModel.ts';
import { count, wireAnswer, wireEdge, wireLink, wireNode, wirePlaceholder } from './graphFixtures.ts';
import { moreEdgeKey, moreKey, neighboursOf, toView } from './view.ts';

const S = wireNode('feature', 's', 'Cave S');
const doc = wireNode('document', 'd1', 'Survey notes');
const trip = wireNode('tripLog', 't1', 'Trip one');
const caver = wireNode('caver', 'c1', 'Ion A.');
const parent = wireNode('feature', 'p', 'Cave P');
const tag = wireNode('tag', 'karst', 'karst');
const other = wireNode('feature', 'f2', 'Cave two');
const hidden = wirePlaceholder('m7');

/** S has: a two-member directed link to doc (S main), a three-member link with trip and caver, a parent, a tag (which leads on to Cave two) and a link to a placeholder. */
function explored(): GraphModel {
  const model = new GraphModel();
  model.merge(
    wireAnswer(
      S,
      [
        wireLink('1', 'documents', true, [{ node: doc }, { node: S, isMain: true }]),
        wireLink('2', 'related-to', false, [{ node: S }, { node: trip }, { node: caver }]),
        wireLink('3', 'related-to', false, [{ node: S }, { node: hidden }]),
        { nodes: [parent], edges: [wireEdge('hierarchy', 'parent', parent.key, S.key)] },
        { nodes: [tag], edges: [wireEdge('tag', 'tagged', S.key, tag.key)] },
      ],
      [count('resLink', 3), count('hierarchy', 1), count('tag', 1), count('attachment', 0, 0)],
    ),
  );
  model.merge(wireAnswer(tag, [{ nodes: [other], edges: [wireEdge('tag', 'tagged', other.key, tag.key)] }], [count('tag', 1)]));
  return model;
}

describe('link-as-node rule', () => {
  it('draws a two-member link as one edge from the main member, and a larger one as a hub', () => {
    const view = toView(explored());
    const keys = view.nodes.map((n) => n.key);
    expect(keys).not.toContain('resLink:1');
    expect(keys).not.toContain('resLink:3');
    expect(keys).toContain('resLink:2');
    expect(view.nodes.find((n) => n.key === 'resLink:2')?.kind).toBe('hub');

    const collapsed = view.edges.find((e) => e.key === 'resLink:1');
    expect(collapsed).toMatchObject({ source: S.key, target: doc.key, directed: true, relation: 'documents' });
    expect(collapsed?.link).toEqual({ key: 'resLink:1', shortCode: 'L1' });
    expect(view.edges.filter((e) => e.source === 'resLink:2')).toHaveLength(3);
  });

  it('collapses a link to a placeholder into an edge to that placeholder, which stays a node of its own', () => {
    const view = toView(explored());
    const ph = view.nodes.find((n) => n.key === hidden.key);
    expect(ph).toMatchObject({ restricted: true, kind: 'node' });
    expect(ph?.node?.title).toBeNull();
    expect(view.edges.find((e) => e.key === 'resLink:3')).toMatchObject({ source: S.key, target: hidden.key });
  });

  it('keeps the member order of the server for an undirected two-member link', () => {
    const view = toView(explored());
    expect(view.edges.find((e) => e.key === 'resLink:3')?.source).toBe(S.key);
  });

  it('decides on the whole membership, so hiding a member\'s type leaves the hub a hub', () => {
    const view = toView(explored(), toggleType(noFilters(), 'caver'));
    expect(view.nodes.some((n) => n.key === 'resLink:2' && n.kind === 'hub')).toBe(true);
    expect(view.nodes.some((n) => n.key === caver.key)).toBe(false);
    expect(view.edges.filter((e) => e.source === 'resLink:2')).toHaveLength(2);
  });
});

describe('a seed that is itself a two-member link', () => {
  it('stays a hub node with an edge to each member instead of vanishing into a collapsed edge', () => {
    const link = wireLink('9', 'documents', true, [{ node: doc }, { node: S, isMain: true }]);
    const seedHub = link.nodes[0];
    const model = new GraphModel();
    model.merge(wireAnswer(seedHub, [link], [count('resLink', 2)]));
    const view = toView(model);
    expect(view.seedKey).toBe('resLink:9');
    expect(view.nodes.map((n) => n.key).sort()).toEqual(['document:d1', 'feature:s', 'resLink:9']);
    expect(view.nodes.find((n) => n.key === 'resLink:9')).toMatchObject({ kind: 'hub', isSeed: true });
    expect(view.edges).toHaveLength(2);
    const keys = new Set([...view.nodes.map((n) => n.key), ...view.edges.map((e) => e.key)]);
    expect(keys.size).toBe(view.nodes.length + view.edges.length);
  });
});

describe('visibility under filters', () => {
  it('shows everything reachable with no filters, counting hubs as nodes', () => {
    const view = toView(explored());
    expect(view.nodeCount).toBe(view.nodes.length);
    expect(view.nodes.map((n) => n.key).sort()).toEqual(
      ['caver:c1', 'document:d1', 'feature:f2', 'feature:p', 'feature:s', 'restricted:m7', 'resLink:2', 'tag:karst', 'tripLog:t1'].sort(),
    );
  });

  it('removes a switched-off category\'s edges, and what only they led to', () => {
    const view = toView(explored(), toggleCategory(noFilters(), 'tag'));
    const keys = view.nodes.map((n) => n.key);
    expect(keys).not.toContain('tag:karst');
    // Cave two was reachable only through the tag, so it goes with it rather than floating free.
    expect(keys).not.toContain('feature:f2');
    expect(keys).toContain('feature:p');
  });

  it('removes a node type and the branch behind it', () => {
    const keys = toView(explored(), toggleType(noFilters(), 'tag')).nodes.map((n) => n.key);
    expect(keys).not.toContain('tag:karst');
    expect(keys).not.toContain('feature:f2');
  });

  it('filters by relation code inside a category, hub links included', () => {
    const view = toView(explored(), toggleRelation(noFilters(), 'resLink', 'related-to'));
    const keys = view.nodes.map((n) => n.key);
    expect(keys).not.toContain('resLink:2');
    expect(keys).not.toContain('tripLog:t1');
    expect(keys).not.toContain('restricted:m7');
    expect(keys).toContain('document:d1');
  });

  it('always keeps the seed, even with its own type and every category off', () => {
    let f = toggleType(noFilters(), 'feature');
    for (const c of ['resLink', 'hierarchy', 'tag']) f = toggleCategory(f, c);
    const view = toView(explored(), f);
    expect(view.nodes.map((n) => n.key)).toEqual(['feature:s']);
    expect(view.edges).toEqual([]);
  });

  it('is empty for a model with no seed yet', () => {
    expect(toView(new GraphModel())).toMatchObject({ nodes: [], edges: [], seedKey: null });
  });
});

describe('highlight states', () => {
  it('marks the selection, its neighbours, and fades the rest', () => {
    const view = toView(explored(), noFilters(), { kind: 'node', key: S.key });
    const state = (key: string) => view.nodes.find((n) => n.key === key)?.state;
    expect(state(S.key)).toBe('selected');
    expect(state(doc.key)).toBe('neighbour');
    expect(state('resLink:2')).toBe('neighbour');
    expect(state(trip.key)).toBe('faded');
    expect(state(other.key)).toBe('faded');
    expect(view.edges.find((e) => e.key === 'resLink:1')?.state).toBe('neighbour');
    expect(view.edges.find((e) => e.source === 'resLink:2' && e.target === trip.key)?.state).toBe('faded');
  });

  it('selecting a hub makes its members its neighbours', () => {
    const view = toView(explored(), noFilters(), { kind: 'node', key: 'resLink:2' });
    expect(view.nodes.filter((n) => n.state === 'neighbour').map((n) => n.key).sort()).toEqual(['caver:c1', 'feature:s', 'tripLog:t1']);
  });

  it('selecting an edge selects it and lights its two ends', () => {
    const view = toView(explored(), noFilters(), { kind: 'edge', key: 'resLink:1' });
    expect(view.edges.find((e) => e.key === 'resLink:1')?.state).toBe('selected');
    expect(view.nodes.filter((n) => n.state === 'neighbour').map((n) => n.key).sort()).toEqual([doc.key, S.key].sort());
    expect(view.nodes.find((n) => n.key === trip.key)?.state).toBe('faded');
  });

  it('treats a selection the filters have hidden as no selection', () => {
    const view = toView(explored(), toggleType(noFilters(), 'document'), { kind: 'node', key: doc.key });
    expect(view.selection).toBeNull();
    expect(view.nodes.every((n) => n.state === 'normal')).toBe(true);
  });

  it('leaves everything normal with nothing selected', () => {
    const view = toView(explored());
    expect(view.nodes.every((n) => n.state === 'normal')).toBe(true);
    expect(view.edges.every((e) => e.state === 'normal')).toBe(true);
  });
});

describe('"+N more" stubs', () => {
  function paged(): GraphModel {
    const model = new GraphModel();
    model.merge(wireAnswer(S, [{ nodes: [trip], edges: [wireEdge('tripParticipant', 'leader', trip.key, S.key)] }], [count('tripParticipant', 1, 6), count('tag', 0, 0)]));
    return model;
  }

  it('adds no stubs unless asked, and counts only real items either way', () => {
    expect(toView(paged()).nodes.some((n) => n.kind === 'more')).toBe(false);
    const view = toView(paged(), noFilters(), null, { showMore: true });
    const stub = view.nodes.find((n) => n.kind === 'more');
    expect(stub?.key).toBe(moreKey(S.key, 'tripParticipant'));
    expect(stub?.more).toEqual({ nodeKey: S.key, category: 'tripParticipant', remaining: 5 });
    expect(view.nodeCount).toBe(2);
    expect(view.edgeCount).toBe(1);
    expect(view.edges.filter((e) => e.isMore)).toHaveLength(1);
    expect(view.edges.find((e) => e.isMore)?.key).toBe(moreEdgeKey(S.key, 'tripParticipant'));
    // One drawing-library id space for nodes and edges: no node may share a key with an edge.
    const edgeKeys = new Set(view.edges.map((e) => e.key));
    expect(view.nodes.filter((n) => edgeKeys.has(n.key))).toEqual([]);
  });

  it('drops the stub of a category that is switched off, and never selects one', () => {
    const view = toView(paged(), toggleCategory(noFilters(), 'tripParticipant'), null, { showMore: true });
    expect(view.nodes.some((n) => n.kind === 'more')).toBe(false);
    const sel = toView(paged(), noFilters(), { kind: 'node', key: moreKey(S.key, 'tripParticipant') }, { showMore: true });
    expect(sel.selection).toBeNull();
  });
});

describe('neighboursOf', () => {
  it('lists a node\'s visible relations with the reading from its end', () => {
    const view = toView(explored());
    const fromSeed = neighboursOf(view, S.key).find((n) => n.edge.key === 'resLink:1');
    expect(fromSeed).toMatchObject({ reading: 'forward', other: { key: doc.key } });
    const fromDoc = neighboursOf(view, doc.key);
    expect(fromDoc).toHaveLength(1);
    expect(fromDoc[0]).toMatchObject({ reading: 'inverse', other: { key: S.key } });
    const undirected = neighboursOf(view, trip.key);
    expect(undirected[0].reading).toBe('forward');
  });
});
