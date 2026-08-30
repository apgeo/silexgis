// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TripLogInfo } from '../../api/hooks.ts';
import { caverReference, countPeople, referencesLoadedCaver } from './roster.ts';

type Row = TripLogInfo['participants'][number];

function row(caverId: string, roleId: number): Row {
  return { caverId, roleId, name: caverId, entryTime: null, exitTime: null, note: null } as unknown as Row;
}

describe('countPeople', () => {
  it('counts an ordinary roster row by row', () => {
    expect(countPeople([row('ana', 1), row('bogdan', 1)])).toBe(2);
  });

  /**
   * The roster is unique on the person *and* the job, so one person who both led and surveyed is
   * two rows. Counting rows would report three people underground where two went.
   */
  it('counts somebody holding two jobs once', () => {
    expect(countPeople([row('ana', 1), row('ana', 3), row('bogdan', 1)])).toBe(2);
  });

  it('counts an empty roster as nobody', () => {
    expect(countPeople([])).toBe(0);
  });
});

describe('caverReference', () => {
  /**
   * The reference is what a write is read by, so keeping it beside a corrected name would store
   * nothing at all and a misspelling would survive every attempt to fix it — silently, with a
   * success message.
   */
  it('lets go of the person once the name has been corrected', () => {
    expect(
      caverReference({ caverId: 'caver-1', loadedName: 'Ana Popscu', name: 'Ana Popescu' }),
    ).toEqual({ caverId: null, newCaverName: 'Ana Popescu' });
  });

  /**
   * The other half, and the one that fails quietly the other way: the name shown for somebody who
   * holds an account is their profile's, which need not be what is recorded against them, so
   * detaching on any keystroke would make two people out of one.
   */
  it('keeps the person while the name is the one it arrived under', () => {
    expect(
      caverReference({ caverId: 'caver-1', loadedName: 'Ana Popescu', name: 'Ana Popescu' }),
    ).toEqual({ caverId: 'caver-1', newCaverName: null });
    expect(
      caverReference({ caverId: 'caver-1', loadedName: 'Ana Popescu', name: '  Ana Popescu ' }),
    ).toEqual({ caverId: 'caver-1', newCaverName: null });
  });

  it('names somebody nobody chose by their name alone', () => {
    expect(caverReference({ name: ' Bogdan Ionescu ' })).toEqual({
      caverId: null,
      newCaverName: 'Bogdan Ionescu',
    });
    expect(referencesLoadedCaver({ name: 'Bogdan Ionescu' })).toBe(false);
  });
});
