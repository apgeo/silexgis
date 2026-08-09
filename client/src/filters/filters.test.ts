// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  condition,
  conditionCount,
  depth,
  emptyDocument,
  fromGroups,
  groupsOf,
  isEmpty,
  nodeCount,
  whereOf,
  withSort,
  withWhere,
  withWorlds,
} from './document.ts';
import { isComplete, preflight } from './preflight.ts';
import { decodeFilter, encodeFilter, withFilterParam } from './url.ts';
import type { FilterLimits, WorldVocabulary } from './types.ts';

/**
 * The whole file is the decoupling claim, made good.
 *
 * Nothing here renders anything or imports anything that could. If this logic ever grows a hook or
 * a component import, these tests keep passing and the dependency rules fail the build instead —
 * which is the arrangement that survives somebody adding "just one" convenience import.
 */

const LIMITS: FilterLimits = {
  maxNodes: 200,
  maxDepth: 6,
  maxValuesPerCondition: 200,
  maxWorlds: 12,
  maxTextValueLength: 200,
  maxPageSize: 500,
};

const VOCABULARIES: WorldVocabulary[] = [
  {
    world: 'feature',
    labelKey: 'filters.worlds.feature',
    fields: [
      { key: 'name', labelKey: 'x', kind: 'text', ops: ['contains'], options: null, sortable: true },
    ],
    sorts: ['created', 'updated', 'title', 'owner'],
  },
];

const named = (text: string) => condition('name', 'contains', [{ type: 'text', value: text }]);

describe('a filter document', () => {
  it('asks nothing until somebody narrows something', () => {
    const document = emptyDocument(['feature', 'tripLog']);

    expect(isEmpty(document)).toBe(true);
    expect(document.scope.map((s) => s.world)).toEqual(['feature', 'tripLog']);
  });

  it('keeps the conditions of a world that survives a change of scope', () => {
    // The behaviour the world buttons stand on. Somebody who loses their conditions by turning
    // documents on will not trust the buttons again, and will stop using them.
    const withCaves = withWhere(emptyDocument(['feature']), 'feature', named('urs'));
    const widened = withWorlds(withCaves, ['feature', 'document']);

    expect(whereOf(widened, 'feature')).toEqual(named('urs'));
    expect(whereOf(widened, 'document')).toBeNull();

    const narrowed = withWorlds(widened, ['feature']);
    expect(whereOf(narrowed, 'feature')).toEqual(named('urs'));
  });

  it('never changes the document it was given', () => {
    // Two controls open on one filter is an ordinary situation — the panel and a dialog — and
    // editing in place would show one of them changes nobody made in it.
    const original = emptyDocument(['feature']);
    const changed = withSort(withWhere(original, 'feature', named('urs')), 'title', false);

    expect(original).toEqual(emptyDocument(['feature']));
    expect(changed.sort).toBe('title');
  });
});

describe('groups, as the editor shows them', () => {
  it('reads one condition as a single group', () => {
    expect(groupsOf(named('urs'))).toEqual([[named('urs')]]);
  });

  it('reads an or of ands as the rows a person wrote', () => {
    const tree = fromGroups([[named('a'), named('b')], [named('c')]]);

    expect(groupsOf(tree)).toEqual([[named('a'), named('b')], [named('c')]]);
  });

  it('writes one shape for one question', () => {
    // A single group is not wrapped in an OR and a single condition not in an AND, so a filter can
    // be compared to the one on screen without a walk that knows about wrappers.
    expect(fromGroups([[named('a')]])).toEqual(named('a'));
    expect(fromGroups([[named('a'), named('b')]])).toEqual({
      node: 'allOf',
      of: [named('a'), named('b')],
    });
  });

  it('treats empty groups as nothing at all', () => {
    expect(fromGroups([])).toBeNull();
    expect(fromGroups([[], []])).toBeNull();
  });

  it('counts what the server counts', () => {
    const document = withWhere(
      emptyDocument(['feature']),
      'feature',
      fromGroups([[named('a'), named('b')], [named('c')]]),
    );

    expect(conditionCount(document)).toBe(3);
    // The or, the one and it needed, and the three conditions. The single-condition group is not
    // wrapped in an and of its own, which is the normalisation showing up in the count.
    expect(nodeCount(document)).toBe(5);
    expect(depth(document)).toBe(3);
  });
});

describe('a filter as a link', () => {
  it('survives the round trip', () => {
    const document = withWhere(emptyDocument(['feature']), 'feature', named('Peștera'));

    expect(decodeFilter(encodeFilter(document))).toEqual(document);
  });

  it('is safe to put in a URL without further escaping', () => {
    const token = encodeFilter(withWhere(emptyDocument(['feature']), 'feature', named('a/b+c=d')));

    expect(token).toMatch(/^[A-Za-z0-9_-]+$/);
  });

  it('opens the page with nothing filtered rather than failing', () => {
    // Every reason a token can be unreadable ends here: truncated by a chat client, written by a
    // newer version, or never a filter. The person who opened the link did nothing wrong and has
    // no way to repair it, so an error screen would be the one useless answer.
    expect(decodeFilter(null)).toBeNull();
    expect(decodeFilter('')).toBeNull();
    expect(decodeFilter('not-base64!!')).toBeNull();
    expect(decodeFilter(encodeFilter({ ...emptyDocument(['feature']), version: 99 }))).toBeNull();
  });

  it('leaves no parameter behind when there is nothing to say', () => {
    const params = withFilterParam(new URLSearchParams('filter=old&page=2'), null);

    expect(params.get('filter')).toBeNull();
    expect(params.get('page')).toBe('2');
  });
});

describe('the pre-flight check', () => {
  it('passes an ordinary filter', () => {
    const document = withWhere(emptyDocument(['feature']), 'feature', named('urs'));

    expect(preflight(document, LIMITS, VOCABULARIES)).toEqual([]);
  });

  it('says so before the request when a filter is too large', () => {
    const document = withWhere(
      emptyDocument(['feature']),
      'feature',
      fromGroups([Array.from({ length: LIMITS.maxNodes + 1 }, (_, i) => named(`${i}`))]),
    );

    expect(preflight(document, LIMITS, VOCABULARIES).map((p) => p.messageKey))
      .toContain('filters.problems.tooLarge');
  });

  it('notices a sort no world in scope can answer', () => {
    // Otherwise the list comes back in an order nobody chose, with nothing on screen to say the
    // sort was ignored.
    const document = withSort(emptyDocument(['feature']), 'proximity', true);

    expect(preflight(document, LIMITS, VOCABULARIES).map((p) => p.messageKey))
      .toContain('filters.problems.sortUnavailable');
  });

  it('has no opinion about whether a field exists', () => {
    // That answer depends on who is asking, and the server gives it. A second opinion here would
    // be wrong for anybody whose vocabulary differs from the one that happened to be loaded.
    const document = withWhere(
      emptyDocument(['feature']),
      'feature',
      condition('somethingElse', 'contains', [{ type: 'text', value: 'x' }]),
    );

    expect(preflight(document, LIMITS, VOCABULARIES)).toEqual([]);
  });
});

describe('a half-written condition', () => {
  it('is left out of the request rather than complained about', () => {
    expect(isComplete(condition('name', 'contains', []))).toBe(false);
    expect(isComplete(condition('name', 'contains', [{ type: 'text', value: '' }]))).toBe(false);
    expect(isComplete(named('urs'))).toBe(true);
  });

  it('knows the operators that need nothing and the one that needs two', () => {
    expect(isComplete(condition('name', 'isEmpty'))).toBe(true);
    expect(isComplete(condition('createdAt', 'between', [{ type: 'instant', value: 'a' }]))).toBe(false);
  });
});

describe('the wire shape', () => {
  it('spells a document exactly the way the server reads one', () => {
    // The generated contract cannot check this. The tool describes a polymorphic list of values as
    // an unknown, so nothing in the build would notice if the two sides disagreed about how a value
    // says what type it is — the filter would simply be refused as malformed, at run time, in
    // somebody's face.
    //
    // The same literal appears in the server's FilterWireContractTests. If either side changes how
    // it spells a document, exactly one of the two tests fails and says so.
    const document = withSort(
      withWhere(
        emptyDocument(['feature']),
        'feature',
        fromGroups([[named('urs'), condition('createdAt', 'greaterThan', [
          { type: 'instant', value: '2026-01-01T00:00:00+00:00' },
        ])]]),
      ),
      'title',
      false,
    );

    expect(JSON.stringify(document)).toBe(
      '{"version":1,"scope":[{"world":"feature","where":{"node":"allOf","of":['
      + '{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":"urs"}]},'
      + '{"node":"condition","field":"createdAt","op":"greaterThan","values":'
      + '[{"type":"instant","value":"2026-01-01T00:00:00+00:00"}]}]}}],'
      + '"sort":"title","descending":false}',
    );
  });
});
