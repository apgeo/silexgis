// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { daysLeft, formatDays } from './tripRestoreWindow.ts';

describe('formatDays', () => {
  it('says a number of days in the reader\'s language, plural and singular alike', () => {
    expect(formatDays(30, 'en')).toBe('30 days');
    expect(formatDays(1, 'en')).toBe('1 day');
    expect(formatDays(30, 'ro')).toBe('30 de zile');
    expect(formatDays(1, 'ro')).toBe('1 zi');
  });

  it('still says something for a language the platform does not know', () => {
    expect(formatDays(7, 'not a language tag')).toBe('7 days');
    expect(formatDays(7, undefined)).toMatch(/7/);
  });
});

describe('daysLeft', () => {
  const now = new Date('2026-10-06T12:00:00Z');

  it('counts whole days and rounds down, so nobody is promised a day that is not there', () => {
    expect(daysLeft('2026-10-09T11:00:00Z', now)).toBe(2);
    expect(daysLeft('2026-10-09T12:00:00Z', now)).toBe(3);
    expect(daysLeft('2026-10-06T13:00:00Z', now)).toBe(0);
  });

  it('never goes below nothing left', () => {
    expect(daysLeft('2026-10-01T00:00:00Z', now)).toBe(0);
  });

  it('is nothing at all where nothing removes the trip', () => {
    expect(daysLeft(null, now)).toBeNull();
    expect(daysLeft(undefined, now)).toBeNull();
  });
});
