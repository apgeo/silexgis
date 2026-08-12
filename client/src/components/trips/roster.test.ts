// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TripLogInfo } from '../../api/hooks.ts';
import { countPeople } from './roster.ts';

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
