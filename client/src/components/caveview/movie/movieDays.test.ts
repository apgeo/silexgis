// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { formatTripDates } from '../../trips/tripDates.ts';
import { movieTripDays } from './movieDays.ts';

describe('movieTripDays', () => {
  it('spans from the earliest start to the latest end, whatever order the trips come in', () => {
    const days = movieTripDays(
      [
        { tripDate: '2026-09-14', tripDateEnd: null },
        { tripDate: '2026-09-10', tripDateEnd: '2026-09-11' },
        { tripDate: '2026-09-12', tripDateEnd: '2026-09-16' },
      ],
      'en',
    );
    expect(days).toBe(formatTripDates('2026-09-10', '2026-09-16', 'en'));
  });

  it('reads trips all on one day as that day, and skips undated trips', () => {
    const days = movieTripDays(
      [
        { tripDate: '2026-09-12', tripDateEnd: '2026-09-12' },
        { tripDate: null, tripDateEnd: null },
        { tripDate: '2026-09-12', tripDateEnd: null },
      ],
      'en',
    );
    expect(days).toBe(formatTripDates('2026-09-12', null, 'en'));
  });

  it('says nothing when no trip is dated', () => {
    expect(movieTripDays([{ tripDate: null, tripDateEnd: null }], 'en')).toBeNull();
    expect(movieTripDays([], 'en')).toBeNull();
  });
});
