// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import { formatStayDates, isStillThere, stayEndForWrite } from './stayDates.ts';

/**
 * A stay's days read by the opposite rule from a trip's: no last day is somebody who has not
 * left, and one day keeps its day. Pinned apart from the trip's own helper because the mistake
 * is to reuse that one, and it would look right on every stay that ran on.
 */

// The words are the caller's; what is pinned is which key is asked for and with which day.
const t = ((key: string, values?: Record<string, unknown>) =>
  `${key}|${String(values?.date ?? '')}`) as unknown as TFunction;

describe('a stay with no last day', () => {
  it('is somebody still there', () => {
    expect(isStillThere(null)).toBe(true);
    expect(isStillThere(undefined)).toBe(true);
    expect(isStillThere('2026-07-20')).toBe(false);
  });

  it('reads as the day they arrived, said to be still going', () => {
    const arrived = new Date(2026, 6, 20).toLocaleDateString('en-GB');
    expect(formatStayDates('2026-07-20', null, 'en-GB', t)).toBe(`expeditions.stay.since|${arrived}`);
  });

  it('is written as nothing only when "still there" was chosen', () => {
    expect(stayEndForWrite(true, '2026-07-26')).toBeNull();
    expect(stayEndForWrite(false, '2026-07-26')).toBe('2026-07-26');
  });
});

describe('a stay of one day', () => {
  it('reads as that day and not as a range of it', () => {
    const day = new Date(2026, 6, 20).toLocaleDateString('en-GB');
    expect(formatStayDates('2026-07-20', '2026-07-20', 'en-GB', t)).toBe(day);
  });

  it('is written with its day as the last, never folded to nothing', () => {
    expect(stayEndForWrite(false, '2026-07-20')).toBe('2026-07-20');
  });
});

describe('a stay that ran on', () => {
  it('reads as a range', () => {
    const from = new Date(2026, 6, 20).toLocaleDateString('en-GB');
    const to = new Date(2026, 6, 26).toLocaleDateString('en-GB');
    expect(formatStayDates('2026-07-20', '2026-07-26', 'en-GB', t)).toBe(`${from} – ${to}`);
  });
});
