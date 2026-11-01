// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GraphModel } from './graphModel.ts';
import { count, wireAnswer, wireEdge, wireLink, wireNode, wirePlaceholder } from './graphFixtures.ts';

const seed = wireNode('feature', 's', 'Cave S');
const doc = wireNode('document', 'd1', 'Survey notes');
const trip = wireNode('tripLog', 't1', 'Trip one');

function firstHop() {
  const link = wireLink('1', 'documents', true, [{ node: seed, isMain: true }, { node: doc }]);
  return wireAnswer(
    seed,
    [link, { nodes: [trip], edges: [wireEdge('tripParticipant', 'leader', trip.key, seed.key)] }],
    [count('resLink', 1), count('tripParticipant', 1, 5)],
  );
}

describe('GraphModel.merge', () => {
  it('names the seed from the first answer and keeps one node per key', () => {
    const model = new GraphModel();
    const result = model.merge(firstHop());
    expect(model.seedKey).toBe('feature:s');
    expect([...model.nodes.keys()].sort()).toEqual(['document:d1', 'feature:s', 'resLink:1', 'tripLog:t1']);
    expect(result.addedNodes).toHaveLength(4);
    expect(model.edges.size).toBe(3);
  });

  it('merges a second answer by key without duplicating what both mention', () => {
    const model = new GraphModel();
    model.merge(firstHop());
    // Expanding the document finds the same link again plus a new neighbour.
    const other = wireNode('feature', 'f2', 'Cave two');
    const link = wireLink('1', 'documents', true, [{ node: seed, isMain: true }, { node: doc }]);
    const again = wireAnswer(doc, [link, { nodes: [other], edges: [wireEdge('attachment', 'plan', doc.key, other.key)] }], [count('resLink', 1), count('attachment', 1)]);
    const result = model.merge(again);
    expect(result.addedNodes).toEqual(['feature:f2']);
    expect(result.addedEdges).toHaveLength(1);
    expect(model.nodes.size).toBe(5);
    expect(model.edges.size).toBe(4);
    expect(model.seedKey).toBe('feature:s');
  });

  it('lets a later answer fill in what an earlier one lacked but never erase it', () => {
    const model = new GraphModel();
    model.merge(wireAnswer(seed, [{ nodes: [wireNode('document', 'd1', null)], edges: [wireEdge('attachment', 'plan', seed.key, 'document:d1')] }], [count('attachment', 1)]));
    model.merge(wireAnswer(doc, [], []));
    expect(model.nodes.get('document:d1')?.title).toBe('Survey notes');
    model.merge(wireAnswer(wireNode('document', 'd1', null), [], []));
    expect(model.nodes.get('document:d1')?.title).toBe('Survey notes');
  });

  it('keeps two placeholders apart, one per member row', () => {
    const model = new GraphModel();
    const a = wirePlaceholder('m1');
    const b = wirePlaceholder('m2');
    model.merge(
      wireAnswer(seed, [
        wireLink('1', 'related-to', false, [{ node: seed }, { node: a }]),
        wireLink('2', 'related-to', false, [{ node: seed }, { node: b }]),
      ], [count('resLink', 2)]),
    );
    expect(model.nodes.get('restricted:m1')?.restricted).toBe(true);
    expect(model.nodes.get('restricted:m1')?.id).toBeNull();
    expect(model.nodes.size).toBe(5);
  });
});

describe('expansion bookkeeping', () => {
  it('records per-category progress for the node an answer is about', () => {
    const model = new GraphModel();
    model.merge(firstHop());
    expect(model.isExpanded('feature:s')).toBe(true);
    expect(model.isExpanded('document:d1')).toBe(false);
    expect(model.remaining('feature:s')).toEqual([{ category: 'tripParticipant', remaining: 4, loaded: 1, total: 5 }]);
  });

  it('counts a paged answer on top of the offset and leaves other categories alone', () => {
    const model = new GraphModel();
    model.merge(firstHop());
    const more = [wireNode('tripLog', 't2', 'Trip two'), wireNode('tripLog', 't3', 'Trip three')];
    model.merge(
      wireAnswer(seed, [{ nodes: more, edges: more.map((m) => wireEdge('tripParticipant', 'leader', m.key, seed.key)) }], [count('tripParticipant', 2, 5)]),
      { category: 'tripParticipant', offset: 1 },
    );
    expect(model.remaining('feature:s')).toEqual([{ category: 'tripParticipant', remaining: 2, loaded: 3, total: 5 }]);
    expect(model.progress('feature:s').get('resLink')).toEqual({ loaded: 1, total: 1 });
  });

  it('builds the request that takes a node further, and none when nothing can be asked', () => {
    const model = new GraphModel();
    model.merge(
      wireAnswer(seed, [{ nodes: [trip, wirePlaceholder('m1')], edges: [wireEdge('tripParticipant', 'leader', trip.key, seed.key)] }, wireLink('9', 'related-to', false, [{ node: seed }, { node: wirePlaceholder('m1') }, { node: doc }])], [count('tripParticipant', 1, 3)]),
    );
    expect(model.requestFor('feature:s')).toBeNull();
    expect(model.requestFor('tripLog:t1')).toEqual({ type: 'tripLog', id: 't1' });
    expect(model.requestFor('feature:s', 'tripParticipant')).toEqual({ type: 'feature', id: 's', category: 'tripParticipant', offset: 1 });
    expect(model.requestFor('feature:s', 'hierarchy')).toBeNull();
    expect(model.requestFor('restricted:m1')).toBeNull();
    expect(model.requestFor('resLink:9')).toBeNull();
    expect(model.requestFor('feature:nowhere')).toBeNull();
  });
});

describe('removal and collapse', () => {
  it('forgets a node and the edges that touch it, but never the seed', () => {
    const model = new GraphModel();
    model.merge(firstHop());
    expect(model.removeNode('tripLog:t1')).toBe(true);
    expect(model.nodes.has('tripLog:t1')).toBe(false);
    expect([...model.edges.values()].some((e) => e.source === 'tripLog:t1')).toBe(false);
    expect(model.removeNode('feature:s')).toBe(false);
  });

  it('returns to the seed hop, keeping the seed\'s own further pages', () => {
    const model = new GraphModel();
    model.merge(firstHop());
    const extra = wireNode('feature', 'f2', 'Cave two');
    model.merge(wireAnswer(trip, [{ nodes: [extra], edges: [wireEdge('hierarchy', 'parent', extra.key, trip.key)] }], [count('hierarchy', 1)]));
    const more = wireNode('tripLog', 't2', 'Trip two');
    model.merge(wireAnswer(seed, [{ nodes: [more], edges: [wireEdge('tripParticipant', 'leader', more.key, seed.key)] }], [count('tripParticipant', 1, 5)]), { category: 'tripParticipant', offset: 1 });
    expect(model.isExpanded('tripLog:t1')).toBe(true);

    model.collapseToSeed();
    expect(model.nodes.has('feature:f2')).toBe(false);
    expect(model.isExpanded('tripLog:t1')).toBe(false);
    expect(model.nodes.has('tripLog:t2')).toBe(true);
    expect(model.seedKey).toBe('feature:s');
    expect(model.remaining('feature:s')[0]).toMatchObject({ loaded: 2, total: 5 });
  });

  it('lists the requests that built it, in order, so a refresh can ask them all again', () => {
    const model = new GraphModel();
    model.merge(wireAnswer(seed, [{ nodes: [trip], edges: [wireEdge('tripParticipant', 'leader', trip.key, seed.key)] }], [count('tripParticipant', 1, 5)]));
    model.merge(wireAnswer(seed, [], [count('tripParticipant', 1, 5)]), { category: 'tripParticipant', offset: 1 });
    expect(model.answeredRequests()).toEqual([
      { type: 'feature', id: 's' },
      { type: 'feature', id: 's', category: 'tripParticipant', offset: 1 },
    ]);
  });
});
