// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { takeIntoWorkingSet, workingSetRef } from './workingSet.ts';

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
