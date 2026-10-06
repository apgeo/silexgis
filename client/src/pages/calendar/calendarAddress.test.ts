// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  CALENDAR_SOURCES,
  EmptyCalendarAddress,
  isCalendarNarrowed,
  kindQuery,
  readCalendarAddress,
  sourceQuery,
  wantsNoSource,
  wantsSource,
  withSource,
  writeCalendarAddress,
  type CalendarAddress,
} from './calendarAddress.ts';

const read = (search: string): CalendarAddress => readCalendarAddress(new URLSearchParams(search));
const write = (address: CalendarAddress): string => writeCalendarAddress(address).toString();

describe('the calendar in the address bar', () => {
  /**
   * The untouched calendar has a bare address. A default written out as a key would give "nothing
   * is chosen" two spellings, and the second of them is the one every shared link would carry.
   */
  it('writes the untouched calendar as no keys at all, and reads no keys as the untouched calendar', () => {
    expect(write(EmptyCalendarAddress)).toBe('');
    expect(read('')).toEqual(EmptyCalendarAddress);
  });

  /** Everything a reader can choose survives the trip through an address and back. */
  it('carries the whole view there and back', () => {
    const chosen: CalendarAddress = {
      view: 'agenda',
      from: '2026-09-01',
      to: '2026-10-31',
      day: '2026-09-14',
      sources: ['expedition', 'event'],
      kinds: ['training', 'deadline'],
      cavingGroupId: '11111111-1111-1111-1111-111111111111',
      mine: true,
      showPast: false,
      showCancelled: false,
      showMap: false,
      sort: '-title',
    };

    expect(read(write(chosen))).toEqual(chosen);
  });

  /**
   * The keys that are also questions to the server are spelled the way the server spells them, so
   * somebody who can read the address can read the request, and a list is one readable key.
   */
  it('spells a narrowing the way the request does, a set as one comma-separated key', () => {
    const params = writeCalendarAddress({
      ...EmptyCalendarAddress,
      sources: ['tripLog', 'event'],
      kinds: ['training', 'gearCheck'],
      showPast: false,
      showCancelled: false,
      mine: true,
    });

    expect(params.get('source')).toBe('tripLog,event');
    expect(params.get('kind')).toBe('training,gearCheck');
    expect(params.get('includePast')).toBe('false');
    expect(params.get('includeCancelled')).toBe('false');
    expect(params.get('mine')).toBe('true');
  });

  /**
   * Wanting no family is a state the page can be put in and has to be able to stay in across a
   * reload, so it has a word of its own — a blank value would read as the key somebody half
   * deleted, which means all of them.
   */
  it('tells no family from every family', () => {
    expect(write({ ...EmptyCalendarAddress, sources: [] })).toBe('source=none');
    expect(read('source=none').sources).toEqual([]);
    expect(wantsNoSource(read('source=none'))).toBe(true);

    expect(read('source=').sources).toBeUndefined();
    expect(wantsNoSource(read('source='))).toBe(false);
    expect(wantsNoSource(read(''))).toBe(false);
  });

  /**
   * Only the word that departs from the ordinary state means anything, so a mistyped address
   * shows more than was asked for and never less.
   */
  it('reads a switch it does not recognise as the ordinary state', () => {
    const odd = read('mine=yes&includePast=no&includeCancelled=0&map=off');

    expect(odd.mine).toBe(false);
    expect(odd.showPast).toBe(true);
    expect(odd.showCancelled).toBe(true);
    expect(odd.showMap).toBe(true);
  });

  /** How the same rows are laid out hides nothing, so an unknown reading is the ordinary one. */
  it('falls back to the record for a reading it does not have', () => {
    expect(read('view=fortnight').view).toBe('record');
    expect(read('view=week').view).toBe('week');
  });

  /**
   * Half a window is not a window, a window written backwards is not one either, and a day that
   * does not exist is not a day. Each is read as no window at all, so the page opens on its own —
   * and shows which days those are in the control beside it.
   */
  it('takes a window only when both ends are real days in order', () => {
    expect(read('from=2026-09-01')).toMatchObject({ from: undefined, to: undefined });
    expect(read('from=2026-10-01&to=2026-09-01')).toMatchObject({ from: undefined, to: undefined });
    expect(read('from=2026-02-31&to=2026-03-10')).toMatchObject({ from: undefined, to: undefined });
    expect(read('from=yesterday&to=tomorrow')).toMatchObject({ from: undefined, to: undefined });

    expect(read('from=2026-09-01&to=2026-09-01')).toMatchObject({
      from: '2026-09-01',
      to: '2026-09-01',
    });
    expect(read('day=2026-13-01').day).toBeUndefined();
    expect(read('day=2026-12-01').day).toBe('2026-12-01');
  });
});

describe('the families of record a calendar is asked for', () => {
  it('wants every family until one is turned off, and writes all of them as no narrowing', () => {
    const untouched = read('');
    for (const family of CALENDAR_SOURCES) {
      expect(wantsSource(untouched, family)).toBe(true);
    }
    expect(sourceQuery(untouched)).toBeUndefined();

    const withoutTrips = { ...untouched, sources: withSource(untouched, 'tripLog', false) };
    expect(withoutTrips.sources).toEqual(['expedition', 'event']);
    expect(sourceQuery(withoutTrips)).toBe('expedition,event');

    // Turned back on, the narrowing is gone rather than spelled out as all three.
    expect(withSource(withoutTrips, 'tripLog', true)).toBeUndefined();
  });

  /**
   * Each family is turned off by itself, so the last one going leaves none — which the page says
   * in words and does not ask the server about.
   */
  it('reaches no family one toggle at a time', () => {
    let address = read('');
    for (const family of CALENDAR_SOURCES) {
      address = { ...address, sources: withSource(address, family, false) };
    }

    expect(address.sources).toEqual([]);
    expect(wantsNoSource(address)).toBe(true);
    expect(sourceQuery(address)).toBeUndefined();
  });

  /**
   * A family this application does not have is not corrected in the address: it is handed to the
   * server, which refuses it, because dropping it would draw a calendar the link did not ask for.
   * The first toggle touched lets go of it.
   */
  it('hands an unknown family to the server, and lets go of it at the first toggle', () => {
    const odd = read('source=tripLog,meetings');

    expect(sourceQuery(odd)).toBe('tripLog,meetings');
    expect(wantsNoSource(odd)).toBe(false);
    expect(withSource(odd, 'event', true)).toEqual(['tripLog', 'event']);

    // Unknown and alone, it is still a question — and its refusal is the answer.
    expect(wantsNoSource(read('source=meetings'))).toBe(false);
    expect(sourceQuery(read('source=meetings'))).toBe('meetings');
  });
});

describe('the kinds of event a calendar is asked for', () => {
  it('sends the chosen kinds while events are wanted', () => {
    expect(kindQuery(read('kind=training,deadline'))).toBe('training,deadline');
    expect(kindQuery(read(''))).toBeUndefined();
  });

  /**
   * A kind is something only an event has. With the events turned off there is nothing for it to
   * narrow, so it is not sent — but it stays chosen, and is back in force when the events are.
   */
  it('keeps a kind chosen but does not send it while events are turned off', () => {
    const eventsOff = read('source=tripLog,expedition&kind=training');

    expect(eventsOff.kinds).toEqual(['training']);
    expect(kindQuery(eventsOff)).toBeUndefined();

    const backOn = { ...eventsOff, sources: withSource(eventsOff, 'event', true) };
    expect(kindQuery(backOn)).toBe('training');
  });

  /** A word that names no kind goes to the server as written, to be refused rather than ignored. */
  it('hands an unknown kind to the server', () => {
    expect(kindQuery(read('kind=training,banana'))).toBe('training,banana');
  });
});

describe('whether a calendar is narrowed', () => {
  /**
   * What an empty calendar may say depends on it: "nothing is recorded in these days" is a claim
   * about the club, and a calendar emptied by its own filters has not earned it.
   */
  it('counts what takes rows away, and nothing that only rearranges them', () => {
    expect(isCalendarNarrowed(read(''))).toBe(false);
    expect(isCalendarNarrowed(read('view=month&day=2026-09-14&sort=-title&map=false'))).toBe(false);
    expect(isCalendarNarrowed(read('from=2026-09-01&to=2026-09-30'))).toBe(false);

    expect(isCalendarNarrowed(read('source=tripLog'))).toBe(true);
    expect(isCalendarNarrowed(read('kind=training'))).toBe(true);
    expect(isCalendarNarrowed(read('mine=true'))).toBe(true);
    expect(isCalendarNarrowed(read('includePast=false'))).toBe(true);
    expect(isCalendarNarrowed(read('includeCancelled=false'))).toBe(true);
    expect(isCalendarNarrowed(read('cavingGroupId=11111111-1111-1111-1111-111111111111'))).toBe(true);
  });
});
