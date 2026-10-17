// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs from 'dayjs';
import { afterEach, describe, expect, it } from 'vitest';
import { dayBound, pickedDay } from './dayBounds.ts';

// Node re-reads the zone when TZ is assigned, so each case runs as a reader in that zone.
// "Etc/GMT-3" is UTC+3 and "Etc/GMT+5" is UTC−5 (the sign is inverted in those names).
const original = process.env.TZ;
afterEach(() => {
  if (original === undefined) delete process.env.TZ;
  else process.env.TZ = original;
});

describe.each([
  ['UTC+3', 'Etc/GMT-3', '+03:00'],
  ['UTC−5', 'Etc/GMT+5', '-05:00'],
  ['UTC', 'Etc/UTC', '+00:00'],
])('a day picked by a reader at %s', (_name, zone, offset) => {
  it('is bounded by its own first and last moment, written with the reader\'s offset', () => {
    process.env.TZ = zone;
    const fifth = dayjs('2026-10-05');

    // The calendar day each bound names, as written, is the day picked — on both sides of midnight.
    expect(dayBound('greaterOrEqual', fifth, 'low')).toBe(`2026-10-05T00:00:00.000${offset}`);
    expect(dayBound('lessThan', fifth, 'low')).toBe(`2026-10-05T00:00:00.000${offset}`);
    expect(dayBound('lessOrEqual', fifth, 'low')).toBe(`2026-10-05T23:59:59.999${offset}`);
    expect(dayBound('greaterThan', fifth, 'low')).toBe(`2026-10-05T23:59:59.999${offset}`);
    expect(dayBound('between', fifth, 'low')).toBe(`2026-10-05T00:00:00.000${offset}`);
    expect(dayBound('between', fifth, 'high')).toBe(`2026-10-05T23:59:59.999${offset}`);

    // And as a moment it is still the reader's own midnight, for the fields that compare moments.
    expect(new Date(dayBound('greaterOrEqual', fifth, 'low')).getTime()).toBe(fifth.startOf('day').valueOf());
  });

  it('reads a bound back as the day it names, whoever wrote it', () => {
    process.env.TZ = zone;
    // Written east of UTC, read here: still the 5th, not the evening of the 4th.
    expect(pickedDay('2026-10-05T00:00:00.000+03:00')?.format('YYYY-MM-DD')).toBe('2026-10-05');
    // Written west of UTC at the last moment of the 5th: still the 5th, not the morning of the 6th.
    expect(pickedDay('2026-10-05T23:59:59.999-05:00')?.format('YYYY-MM-DD')).toBe('2026-10-05');
    // A plain day at midnight UTC, as the gallery writes it.
    expect(pickedDay('2026-10-05T00:00:00Z')?.format('YYYY-MM-DD')).toBe('2026-10-05');
    expect(pickedDay('not a day')).toBeNull();
  });
});
