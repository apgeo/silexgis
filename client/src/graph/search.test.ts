// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GraphModel } from './graphModel.ts';
import { count, wireAnswer, wireEdge, wireNode, wirePlaceholder } from './graphFixtures.ts';
import { nextMatch, normalise, previousMatch, searchNodes } from './search.ts';
import { toView } from './view.ts';

describe('normalise', () => {
  it('ignores case, spacing and the diacritics of both Romanian comma and cedilla spellings', () => {
    expect(normalise('  Peștera  Ţiganului ')).toBe('pestera tiganului');
    expect(normalise('Peşterea ȚARA')).toBe('pesterea tara');
    expect(normalise('Âîă')).toBe('aia');
  });
});

describe('searchNodes', () => {
  const seed = wireNode('feature', 's', 'Peștera Mare', { subtitle: 'Bihor' });
  const view = toView(
    (() => {
      const model = new GraphModel();
      model.merge(
        wireAnswer(
          seed,
          [
            {
              nodes: [
                wireNode('feature', 'a', 'Avenul de la Mare'),
                wireNode('document', 'b', 'Nota Mare', { subtitle: 'cu peștera' }),
                wirePlaceholder('m1'),
                // A placeholder that (against the contract) carries text must still never match.
                { ...wirePlaceholder('m2'), title: 'Mare ascunsa', subtitle: 'mare' },
                wireNode('caver', 'c', 'Ion'),
              ],
              edges: ['feature:a', 'document:b', 'restricted:m1', 'restricted:m2', 'caver:c'].map((t) => wireEdge('hierarchy', 'parent', seed.key, t)),
            },
          ],
          [count('hierarchy', 5)],
        ),
      );
      return model;
    })(),
  );

  it('ranks a title that starts with the text before one that contains it, then a subtitle match', () => {
    expect(searchNodes(view.nodes, 'pestera')).toEqual(['feature:s', 'document:b']);
    expect(searchNodes(view.nodes, 'MARE')).toEqual(['feature:s', 'feature:a', 'document:b']);
  });

  it('finds nothing for empty text and never matches a restricted placeholder', () => {
    expect(searchNodes(view.nodes, '   ')).toEqual([]);
    expect(searchNodes(view.nodes, 'restricted')).toEqual([]);
    expect(searchNodes(view.nodes, 'm1')).toEqual([]);
    // 'mare' matches readable nodes and not the text-carrying placeholder.
    const hits = searchNodes(view.nodes, 'mare');
    expect(hits).toContain('feature:a');
    expect(hits).not.toContain('restricted:m2');
  });

  it('steps through matches and wraps in both directions', () => {
    const m = ['a', 'b', 'c'];
    expect(nextMatch(m, null)).toBe('a');
    expect(nextMatch(m, 'a')).toBe('b');
    expect(nextMatch(m, 'c')).toBe('a');
    expect(nextMatch(m, 'gone')).toBe('a');
    expect(previousMatch(m, 'a')).toBe('c');
    expect(previousMatch(m, 'c')).toBe('b');
    expect(previousMatch(m, null)).toBe('c');
    expect(nextMatch([], 'a')).toBeNull();
    expect(previousMatch([], 'a')).toBeNull();
  });
});
