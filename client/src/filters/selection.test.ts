// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { groupsOf, whereOf } from './document.ts';
import {
  choose,
  documentFor,
  effectiveState,
  initialState,
  pressSort,
  readPrefix,
  rowsToShow,
  scopesInPlay,
  shouldAsk,
  sortsOffered,
  toggleScope,
  unresolvedIds,
  type SelectorScope,
} from './selection.ts';
import type { FilterHit, WorldVocabulary } from './types.ts';

/**
 * The selector, tested without one existing.
 *
 * Everything a person can do to the control is a function here, so what it does is asserted on
 * values rather than driven through a rendered tree. The component that follows is a shell.
 */

const SCOPES: SelectorScope[] = [
  { id: 'caves', labelKey: 'x', world: 'feature', where: { field: 'kind', value: 'cave' }, prefix: '#' },
  {
    id: 'entrances',
    labelKey: 'x',
    world: 'feature',
    where: { field: 'kind', value: 'caveEntrance' },
  },
  { id: 'trips', labelKey: 'x', world: 'tripLog', where: null, prefix: '@' },
];

const TEXT_FIELD = (world: string) => (world === 'feature' ? 'name' : 'title');

const hit = (id: string, title: string, world = 'feature'): FilterHit =>
  ({ world, id, title, subtitle: null, symbol: null, placeable: false });

describe('which scopes are in play', () => {
  it('treats none switched on as everything', () => {
    // Not nothing. A control that answered nothing when somebody turned off the last button would
    // look broken at the moment they were trying to widen the search.
    expect(scopesInPlay(SCOPES, initialState())).toHaveLength(3);
  });

  it('narrows to the ones switched on', () => {
    const state = toggleScope(initialState(), 'caves');

    expect(scopesInPlay(SCOPES, state).map((s) => s.id)).toEqual(['caves']);
  });
});

describe('the prefix characters', () => {
  it('selects a scope when typed first', () => {
    const { scope, query } = readPrefix(SCOPES, '#urs');

    expect(scope?.id).toBe('caves');
    expect(query).toBe('urs');
  });

  it('leaves the character alone anywhere else', () => {
    // Cave names contain punctuation. A control that swallowed a character mid-word would be
    // unusable for exactly the people whose caves are named that way.
    expect(readPrefix(SCOPES, 'urs#2').scope).toBeNull();
    expect(readPrefix(SCOPES, 'urs#2').query).toBe('urs#2');
  });

  it('ignores a character no scope claims', () => {
    expect(readPrefix(SCOPES, '!urs').scope).toBeNull();
    expect(readPrefix(SCOPES, '!urs').query).toBe('!urs');
  });

  it('beats the buttons while it is typed', () => {
    // The person typed something more specific than the buttons say; the typing wins.
    const state = { ...initialState(), activeScopeIds: ['trips'], query: '#urs' };
    const document = documentFor(SCOPES, state, TEXT_FIELD);

    expect(document.scope.map((s) => s.world)).toEqual(['feature']);
  });
});

describe('the document the control asks for', () => {
  it('names a world once even when two buttons share it', () => {
    // Naming it twice makes the server refuse the whole request rather than the button at fault.
    const state = { ...initialState(), activeScopeIds: ['caves', 'entrances'], query: 'urs' };
    const document = documentFor(SCOPES, state, TEXT_FIELD);

    expect(document.scope.map((s) => s.world)).toEqual(['feature']);
  });

  it('makes the two buttons alternatives and applies the typing to both', () => {
    const state = { ...initialState(), activeScopeIds: ['caves', 'entrances'], query: 'urs' };
    const groups = groupsOf(whereOf(documentFor(SCOPES, state, TEXT_FIELD), 'feature'));

    expect(groups).toHaveLength(2);
    for (const group of groups) {
      expect(group.map((c) => c.node === 'condition' && c.field)).toEqual(['kind', 'name']);
    }
  });

  it('asks each world about its own text field', () => {
    const state = { ...initialState(), query: 'urs' };
    const document = documentFor(SCOPES, state, TEXT_FIELD);

    const trip = groupsOf(whereOf(document, 'tripLog'))[0];
    expect(trip.map((c) => c.node === 'condition' && c.field)).toEqual(['title']);
  });

  it('asks for the whole world when nothing is typed', () => {
    const state = { ...initialState(), activeScopeIds: ['trips'] };

    expect(whereOf(documentFor(SCOPES, state, TEXT_FIELD), 'tripLog')).toBeNull();
  });
});

describe('when the server is asked at all', () => {
  it('stays quiet on an empty box', () => {
    // A picker that queried on empty would hand back whatever the first page of a domain happens
    // to be, which is how it becomes a way to enumerate.
    const state = initialState();

    expect(shouldAsk(state, { minChars: 2, browseOnEmpty: false })).toBe(false);
    expect(shouldAsk(state, { minChars: 2, browseOnEmpty: true })).toBe(true);
  });

  it('waits for enough characters', () => {
    expect(shouldAsk({ ...initialState(), query: 'u' }, { minChars: 2, browseOnEmpty: false }))
      .toBe(false);
    expect(shouldAsk({ ...initialState(), query: 'ur' }, { minChars: 2, browseOnEmpty: false }))
      .toBe(true);
  });
});

describe('the rows shown', () => {
  it('keeps a stored choice visible when the search does not find it', () => {
    // Otherwise it vanishes the moment somebody types, and the control looks like it has
    // forgotten what they picked.
    const state = { ...initialState(), chosen: ['a'] };
    const rows = rowsToShow([hit('b', 'Bear')], [hit('a', 'Ursilor')], state);

    expect(rows.map((r) => r.id)).toEqual(['a', 'b']);
  });

  it('does not show it twice when the search does find it', () => {
    const state = { ...initialState(), chosen: ['a'] };
    const rows = rowsToShow([hit('a', 'Ursilor')], [hit('a', 'Ursilor')], state);

    expect(rows.map((r) => r.id)).toEqual(['a']);
  });

  it('reports a choice it cannot describe without saying why', () => {
    // Gone and not-yours are indistinguishable on purpose: telling them apart would confirm the
    // row exists.
    const state = { ...initialState(), chosen: ['a', 'b'] };

    expect(unresolvedIds(state, [hit('a', 'Ursilor')])).toEqual(['b']);
  });
});

describe('choosing', () => {
  it('replaces when the control takes one', () => {
    const state = choose(choose(initialState(), 'a', false), 'b', false);

    expect(state.chosen).toEqual(['b']);
  });

  it('adds and removes when it takes several', () => {
    let state = choose(choose(initialState(), 'a', true), 'b', true);
    expect(state.chosen).toEqual(['a', 'b']);

    state = choose(state, 'a', true);
    expect(state.chosen).toEqual(['b']);
  });
});

describe('the sort buttons', () => {
  const VOCABULARIES: WorldVocabulary[] = [
    { world: 'feature', labelKey: 'x', fields: [], sorts: ['created', 'updated', 'title'] },
    { world: 'tripLog', labelKey: 'x', fields: [], sorts: ['created', 'updated', 'occurred'] },
  ];

  it('offers only what every world in play can answer', () => {
    // A union would leave the worlds that cannot honour a sort in an order nobody chose, with
    // nothing on screen to say so.
    expect(sortsOffered(VOCABULARIES, ['feature', 'tripLog'])).toEqual(['created', 'updated']);
    expect(sortsOffered(VOCABULARIES, ['tripLog'])).toEqual(['created', 'updated', 'occurred']);
    expect(sortsOffered(VOCABULARIES, [])).toEqual([]);
  });

  it('reverses on a second press and starts sensibly on a first', () => {
    let state = pressSort(initialState(), 'created');
    expect([state.sort, state.descending]).toEqual(['created', true]);

    state = pressSort(state, 'created');
    expect(state.descending).toBe(false);

    // Nobody reads an alphabetical list backwards first.
    expect(pressSort(state, 'title').descending).toBe(false);
  });
});

describe('an arrangement that outlived what it referred to', () => {
  it('ignores a scope the caller no longer offers, without forgetting it', () => {
    // Somebody who narrowed to documents, used a screen that has none, and came back should find
    // their choice where they left it — so it is pruned on the way out, never written back.
    const stored = { ...initialState(), activeScopeIds: ['documents', 'caves'] };
    const effective = effectiveState(stored, ['caves', 'trips'], ['created']);

    expect(effective.activeScopeIds).toEqual(['caves']);
    expect(stored.activeScopeIds).toEqual(['documents', 'caves']);
  });

  it('falls back to a sort that exists when the remembered one does not', () => {
    const stored = { ...initialState(), sort: 'proximity' as const };

    expect(effectiveState(stored, [], ['created', 'updated']).sort).toBe('created');
  });

  it('leaves the sort alone while nothing is known about what is available', () => {
    // The vocabulary arrives after the first render. Substituting during that moment would flip
    // the sort somebody chose and then flip it back.
    const stored = { ...initialState(), sort: 'occurred' as const };

    expect(effectiveState(stored, [], []).sort).toBe('occurred');
  });
});
