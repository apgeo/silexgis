// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GraphModel } from './graphModel.ts';
import { count, wireAnswer, wireEdge, wireNode } from './graphFixtures.ts';
import { noFilters, toggleCategory } from './filters.ts';
import { legendOf } from './legend.ts';
import { toView } from './view.ts';

const S = wireNode('feature', 's', 'Cave S');
const doc = wireNode('document', 'd', 'Notes');
const tag = wireNode('tag', 'k', 'karst');

function explored(total = 1): GraphModel {
  const model = new GraphModel();
  model.merge(
    wireAnswer(
      S,
      [
        { nodes: [doc], edges: [wireEdge('attachment', 'plan', S.key, doc.key, { directed: false })] },
        { nodes: [tag], edges: [wireEdge('tag', 'tagged', S.key, tag.key)] },
      ],
      [count('attachment', 1), count('tag', 1, total)],
    ),
  );
  return model;
}

describe('legendOf', () => {
  it('lists the types and categories on screen once each', () => {
    const legend = legendOf(toView(explored()));
    expect(legend.types.sort()).toEqual(['document', 'feature', 'tag']);
    expect(legend.categories.sort()).toEqual(['attachment', 'tag']);
    expect(legend.hasMore).toBe(false);
    expect(legend.hasRestricted).toBe(false);
  });

  it('drops what a filter hides and notes a "+N more" stub', () => {
    const view = toView(explored(5), toggleCategory(noFilters(), 'attachment', false), null, { showMore: true });
    const legend = legendOf(view);
    expect(legend.types).not.toContain('document');
    expect(legend.categories).toEqual(['tag']);
    expect(legend.hasMore).toBe(true);
  });
});
