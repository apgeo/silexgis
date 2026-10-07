// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  momentInSheetZone,
  ownSheetZone,
  SHEET_ZONE_AS_WRITTEN,
  sheetDayInWords,
  sheetZoneNames,
} from './trackingCsvZones.ts';

afterEach(() => vi.restoreAllMocks());

/** What the browser is made to say its own zone is. */
function browserIn(timeZone: string) {
  const real = Intl.DateTimeFormat.prototype.resolvedOptions;
  vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockImplementation(function (
    this: Intl.DateTimeFormat,
  ) {
    return { ...real.call(this), timeZone };
  });
}

describe('the zones a sheet can be read in', () => {
  it("offers the browser's own zone by its name", () => {
    browserIn('Europe/Bucharest');
    expect(ownSheetZone()).toBe('Europe/Bucharest');
  });

  it('offers no zone of its own on a machine kept in UTC, where it would be "as written" twice', () => {
    browserIn('UTC');
    expect(ownSheetZone()).toBeNull();
    browserIn('Etc/UTC');
    expect(ownSheetZone()).toBeNull();
  });

  it('lists zones by the names a server reads a sheet in, and never the choice that names none', () => {
    const names = sheetZoneNames();
    expect(names).toContain('Europe/Bucharest');
    expect(names).not.toContain(SHEET_ZONE_AS_WRITTEN);
    // Area and location: the shape the server asks of a name.
    expect(names.filter((name) => name !== 'UTC' && !name.includes('/'))).toEqual([]);
  });
});

describe('a moment shown on the clocks of a zone', () => {
  const INSTANT = '2026-07-01T11:05:00Z';

  it('is the hour that zone showed, whatever zone the reader is in', () => {
    // Summer in Bucharest is three hours ahead of UTC: 11:05 UTC was 14:05 on the hut's wall.
    expect(momentInSheetZone(INSTANT, 'en-GB', 'Europe/Bucharest')).toContain('14:05');
    expect(momentInSheetZone(INSTANT, 'en-GB', 'Asia/Tokyo')).toContain('20:05');
  });

  it("is on the reader's own clock when the sheet was read in no zone", () => {
    expect(momentInSheetZone(INSTANT, 'en', null)).toBe(new Date(INSTANT).toLocaleString('en'));
  });

  it("falls back to the reader's own clock for a name this browser cannot format in", () => {
    expect(momentInSheetZone(INSTANT, 'en', 'Nowhere/Invented')).toBe(
      new Date(INSTANT).toLocaleString('en'),
    );
  });
});

describe('the day a sheet of bare times was put on', () => {
  it('is that calendar day in the language asked for, not shifted by any clock', () => {
    expect(sheetDayInWords('2026-09-12', 'en-GB')).toBe('12/09/2026');
    expect(sheetDayInWords('2026-01-01', 'en-GB')).toBe('01/01/2026');
  });

  it('is shown as written when it is not a day at all', () => {
    expect(sheetDayInWords('soon', 'en')).toBe('soon');
  });
});
