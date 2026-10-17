// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { emptyDocument, withSort, withWhere } from './document.ts';
import type { FilterDocument, FilterNode } from './types.ts';
import { decodeFilter, encodeFilter } from './url.ts';
import { resultQuestion, takeIntoWorkingSet, workingSetRef } from './workingSet.ts';

describe('a list result as the working set', () => {
  it('names each row by what it is, so the map panel reads it as the right thing', () => {
    expect(workingSetRef('a', 'cave')).toEqual({ kind: 'cave', id: 'a' });
    expect(workingSetRef('b', 'caveEntrance')).toEqual({ kind: 'entrance', id: 'b' });
    expect(workingSetRef('c', 'generic')).toEqual({ kind: 'feature', id: 'c' });
    expect(workingSetRef('d', undefined)).toEqual({ kind: 'feature', id: 'd' });
  });

  it('takes the first rows in the list order and says when the cap left some out', () => {
    const rows = Array.from({ length: 5 }, (_, i) => ({ id: `r${i}`, kind: 'cave' }));

    const capped = takeIntoWorkingSet(rows, 1234, 3);
    expect(capped.refs.map((r) => r.id)).toEqual(['r0', 'r1', 'r2']);
    expect(capped).toMatchObject({ taken: 3, total: 1234, truncated: true });

    const whole = takeIntoWorkingSet(rows, 5, 200);
    expect(whole).toMatchObject({ taken: 5, total: 5, truncated: false });
  });

  it('never claims fewer were found than were taken', () => {
    // A count read a moment before rows were added cannot make "first 6 of 5" appear.
    const rows = Array.from({ length: 6 }, (_, i) => ({ id: `r${i}` }));
    expect(takeIntoWorkingSet(rows, 5, 200)).toMatchObject({ taken: 6, total: 6, truncated: false });
  });
});

describe('the question a working set keeps of the list that filled it', () => {
  const deep: FilterNode = {
    node: 'condition', field: 'depth', op: 'greaterOrEqual', values: [{ type: 'number', value: 100 }],
  };
  const near: FilterNode = {
    node: 'condition',
    field: 'position',
    op: 'within',
    values: [{ type: 'number', value: 500 }, { type: 'point', longitude: 23.5, latitude: 45.25 }],
  };
  const question = withWhere(emptyDocument(['feature']), 'feature', deep);
  const nearestFirst: FilterDocument = {
    ...withSort(question, 'proximity', true),
    anchor: { anchor: 'point', longitude: 22.5, latitude: 46.25 },
  };

  it('keeps a plain question whole, for the map and for saving', () => {
    const token = encodeFilter(question);
    expect(resultQuestion(token, false)).toEqual({ savable: question, mapToken: token });
  });

  it('drops the place a nearest-first list was measured from, from both', () => {
    const { savable, mapToken } = resultQuestion(encodeFilter(nearestFirst), false);
    expect(savable?.anchor).toBeUndefined();
    expect(savable?.sort).toBe('proximity');
    expect(decodeFilter(mapToken)?.anchor).toBeUndefined();
    expect(decodeFilter(mapToken)?.scope).toEqual(question.scope);
  });

  it('offers no saving for a question naming a point of its own, and still opens the map on it', () => {
    const typed = withWhere(emptyDocument(['feature']), 'feature', near);
    const token = encodeFilter(typed);
    expect(resultQuestion(token, false)).toEqual({ savable: null, mapToken: token });
  });

  it('offers no saving for a result cut to the map’s window', () => {
    expect(resultQuestion(encodeFilter(question), true).savable).toBeNull();
  });

  it('has nothing to save and opens the map unfiltered without a filter', () => {
    expect(resultQuestion(undefined, false)).toEqual({ savable: null, mapToken: undefined });
  });
});
