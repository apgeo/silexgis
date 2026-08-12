// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  formatTripDates,
  formatUndergroundTime,
  isMultiDay,
  parseTripDay,
  tripDateEndForWrite,
  undergroundMinutes,
} from './tripDates.ts';

describe('trip dates', () => {
  it('reads a calendar day as that day wherever the reader is', () => {
    // The zone matters: parsed as an instant, "2026-03-14" is UTC midnight, which is the 13th
    // for every reader west of Greenwich. The day is a day, not a moment.
    const day = parseTripDay('2026-03-14');
    expect(day.getFullYear()).toBe(2026);
    expect(day.getMonth()).toBe(2);
    expect(day.getDate()).toBe(14);
    expect(formatTripDates('2026-03-14', null, 'en-GB')).toBe('14/03/2026');
  });

  it('reads a single-day trip as a date and a multi-day trip as a range', () => {
    expect(formatTripDates('2026-03-14', null, 'en-GB')).toBe('14/03/2026');
    expect(formatTripDates('2026-03-14', '2026-03-16', 'en-GB')).toBe('14/03/2026 – 16/03/2026');
  });

  it('does not make a range out of a trip that started and ended on the same day', () => {
    expect(isMultiDay('2026-03-14', '2026-03-14')).toBe(false);
    expect(isMultiDay('2026-03-14', null)).toBe(false);
    expect(isMultiDay('2026-03-14', '2026-03-15')).toBe(true);
    expect(formatTripDates('2026-03-14', '2026-03-14', 'en-GB')).toBe('14/03/2026');
  });
});

describe('underground time', () => {
  it('measures a day trip from its two clock times', () => {
    expect(undergroundMinutes('2026-03-14', null, '09:00:00', '17:30:00')).toBe(510);
    expect(formatUndergroundTime('2026-03-14', null, '09:00:00', '17:30:00')).toBe(
      '09:00 – 17:30 (8h 30m)',
    );
  });

  it('carries a day trip that came out after midnight over the one midnight it crossed', () => {
    expect(undergroundMinutes('2026-03-14', null, '22:00:00', '06:00:00')).toBe(480);
  });

  it('adds the days a trip spanned rather than guessing a single midnight', () => {
    // The case a single DatePicker made unreachable: two days in, out the second afternoon.
    expect(undergroundMinutes('2026-03-14', '2026-03-15', '09:00:00', '15:00:00')).toBe(30 * 60);
    expect(formatUndergroundTime('2026-03-14', '2026-03-15', '09:00:00', '15:00:00')).toBe(
      '09:00 – 15:00 (30h 0m)',
    );
    // An exit earlier in the day than the entry is already explained by the span; adding a
    // midnight on top of it would count the same night twice.
    expect(undergroundMinutes('2026-03-14', '2026-03-15', '22:00:00', '06:00:00')).toBe(480);
    expect(undergroundMinutes('2026-03-14', '2026-03-17', '08:00:00', '08:00:00')).toBe(3 * 24 * 60);
  });

  it('counts whole days across a daylight-saving change', () => {
    // Romania springs forward on 29 March 2026; that local day is 23 hours long.
    expect(undergroundMinutes('2026-03-28', '2026-03-30', '10:00:00', '10:00:00')).toBe(2 * 24 * 60);
  });

  it('shows the times but derives no length when the pair cannot give one', () => {
    expect(undergroundMinutes('2026-03-14', null, null, '17:30:00')).toBeNull();
    expect(formatUndergroundTime('2026-03-14', null, null, '17:30:00')).toBe('— – 17:30');
    expect(formatUndergroundTime('2026-03-14', null, '09:00:00', null)).toBe('09:00 – —');
    expect(formatUndergroundTime('2026-03-14', null, null, null)).toBeNull();
    // An end date before the start is nonsense rather than a negative trip.
    expect(undergroundMinutes('2026-03-16', '2026-03-14', '09:00:00', '17:00:00')).toBeNull();
    expect(formatUndergroundTime('2026-03-16', '2026-03-14', '09:00:00', '17:00:00')).toBe(
      '09:00 – 17:00',
    );
  });
});

describe('the end date as it is stored', () => {
  it('stores nothing when the picked range is one day', () => {
    expect(tripDateEndForWrite('2026-03-14', '2026-03-14')).toBeNull();
    expect(tripDateEndForWrite('2026-03-14', null)).toBeNull();
  });

  it('stores the end of a range that ran on', () => {
    expect(tripDateEndForWrite('2026-03-14', '2026-03-16')).toBe('2026-03-16');
  });
});
