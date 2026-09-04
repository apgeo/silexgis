// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  DefaultTripPageSize,
  EmptyTripListFilter,
  clearedTripListFilter,
  isTripListNarrowed,
  readTripListFilter,
  tripFacetQuery,
  tripListQuery,
  writeTripListFilter,
} from './tripListFilter.ts';

const read = (address: string) => readTripListFilter(new URLSearchParams(address));
const write = (...args: Parameters<typeof writeTripListFilter>) =>
  writeTripListFilter(...args).toString();

describe('the trip listing filter in the address', () => {
  it('reads a bare address as the unnarrowed listing', () => {
    expect(read('')).toEqual(EmptyTripListFilter);
    expect(isTripListNarrowed(read(''))).toBe(false);
  });

  it('writes nothing for a default, so an unnarrowed listing has a bare address', () => {
    expect(write(EmptyTripListFilter)).toBe('');
  });

  it('survives the round trip, which is what makes a narrowed listing a link', () => {
    const address =
      'q=coiba&from=2026-03-01&to=2026-03-31&types=3,7&states=done,published' +
      '&visibilities=private&hadIncident=true&participantIds=p1,p2&areaIds=a1&sort=title&page=3';
    const filter = read(address);

    expect(filter.types).toEqual(['3', '7']);
    expect(filter.states).toEqual(['done', 'published']);
    expect(filter.participantIds).toEqual(['p1', 'p2']);
    expect(filter.areaIds).toEqual(['a1']);
    expect(filter.hadIncident).toBe(true);
    expect(filter.page).toBe(3);
    expect(read(write(filter))).toEqual(filter);
  });

  it('reads an empty facet as no opinion rather than as matching nothing', () => {
    // A control that clears its last choice leaves a bare or trailing-comma value behind, which
    // is somebody undoing a choice and not somebody asking for a listing of nothing.
    const filter = read('types=&states=done,,&visibilities=');

    expect(filter.types).toEqual([]);
    expect(filter.states).toEqual(['done']);
    expect(tripFacetQuery(filter).types).toBeUndefined();
    expect(tripFacetQuery(filter).states).toBe('done');
  });

  it('reads an incident word it does not know as no opinion, never as "no"', () => {
    // A mistyped address shows more than was asked for and never less: reading it as false would
    // silently hide every trip where something went wrong.
    expect(read('hadIncident=yes').hadIncident).toBeUndefined();
    expect(read('hadIncident=false').hadIncident).toBe(false);
  });

  it('ignores a page that is not a page', () => {
    expect(read('page=0&pageSize=-4').page).toBe(1);
    expect(read('page=nonsense').pageSize).toBe(DefaultTripPageSize);
  });

  it('asks the counts about the narrowings and nothing else', () => {
    // The three that change no count are left out, so a page turn does not re-ask for the panel
    // and the counts cannot end up describing a different request from the rows.
    const filter = read('q=coiba&types=3&page=4&pageSize=50&sort=-title');
    const asked = tripFacetQuery(filter) as Record<string, unknown>;

    expect(asked.search).toBe('coiba');
    expect(asked.types).toBe('3');
    expect('page' in asked).toBe(false);
    expect('pageSize' in asked).toBe(false);
    expect('sort' in asked).toBe(false);
    expect(tripListQuery(filter)).toMatchObject({ page: 4, pageSize: 50, sort: '-title' });
  });

  it('clears the narrowings and leaves where the reader is standing alone', () => {
    const filter = read('q=coiba&types=3&participantIds=p1&page=4&pageSize=50&sort=-title');
    const cleared = clearedTripListFilter(filter);

    expect(isTripListNarrowed(cleared)).toBe(false);
    expect(cleared.pageSize).toBe(50);
    expect(cleared.sort).toBe('-title');
    expect(cleared.page).toBe(1);
  });

  it('counts a cave or a camp carried in from another page as a narrowing', () => {
    // The cave page opens this listing already narrowed. The reader did not set that in the
    // panel, so the panel cannot show it — but a reset that left it in place would be a reset
    // that did not reset, and the count above the table would keep saying a number nobody asked
    // for with no way to widen it.
    expect(isTripListNarrowed(read('caveId=c1'))).toBe(true);
    expect(isTripListNarrowed(read('expeditionId=e1'))).toBe(true);
    expect(clearedTripListFilter(read('caveId=c1')).caveId).toBeUndefined();
  });
});
