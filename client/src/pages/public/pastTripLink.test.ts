// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { momentLink, readPastLink, writePastLink } from './pastTripLink.ts';

const params = (query: string) => new URLSearchParams(query);

describe('a past trip named in the page’s own address', () => {
  it('reads a trip, a team and a moment out of a hyperlink', () => {
    expect(readPastLink(params('past=trip-1&team=team-a&at=2019-07-06T13:40:00Z'))).toEqual({
      tripLogId: 'trip-1',
      follow: { kind: 'team', id: 'team-a' },
      at: '2019-07-06T13:40:00Z',
      play: false,
    });
  });

  it('asks for nothing of the past when the address names no trip', () => {
    // The positive twin is the test above: the same reader is asking for something only when
    // `past` is there, so a plain follow link keeps behaving exactly as it did.
    expect(readPastLink(params(''))).toBeNull();
    expect(readPastLink(params('team=team-a&at=2019-07-06T13:40:00Z'))).toBeNull();
    expect(readPastLink(params('past='))).toBeNull();
  });

  it('prefers the person when a link names both a person and a team', () => {
    // A link that cannot mean one thing is resolved to the narrower of the two: a person is a
    // place, a team is a group of them, and whoever wrote both plainly meant the person.
    expect(readPastLink(params('past=trip-1&team=team-a&caver=3'))?.follow).toEqual({
      kind: 'caver',
      id: '3',
    });
  });

  it('reads an empty team as the group of everybody on no team', () => {
    // The one place an absent value and an empty one mean different things: that group is real
    // and has no id.
    expect(readPastLink(params('past=trip-1&team='))?.follow).toEqual({ kind: 'team', id: null });
    expect(readPastLink(params('past=trip-1'))?.follow).toBeNull();
  });

  it('writes what is on screen back into the address, keeping whatever else was there', () => {
    const written = writePastLink(params('lang=ro'), 'trip-1', { kind: 'team', id: 'team-a' });
    expect(written.get('lang')).toBe('ro');
    expect(written.get('past')).toBe('trip-1');
    expect(written.get('team')).toBe('team-a');
  });

  it('never writes a moment, because a playing scrubber would rewrite the address five times a second', () => {
    const written = writePastLink(params('past=trip-1&at=2019-07-06T13:40:00Z'), 'trip-1', null);
    expect(written.get('at')).toBeNull();
    // But a link may still carry one: reading is what `at` exists for.
    expect(readPastLink(params('past=trip-1&at=2019-07-06T13:40:00Z'))?.at).toBe(
      '2019-07-06T13:40:00Z',
    );
  });

  it('leaves no trace of the past behind when the reader leaves it', () => {
    const written = writePastLink(params('past=trip-1&caver=2&at=X&play=1&lang=ro'), null, null);
    expect(written.toString()).toBe('lang=ro');
  });
});

describe('a link that names a moment and may press play', () => {
  it('reads play in every way somebody would write yes, the bare word included', () => {
    for (const query of ['play', 'play=', 'play=1', 'play=true', 'play=yes', 'play=PLAY']) {
      expect(readPastLink(params(`past=trip-1&${query}`))?.play, query).toBe(true);
    }
  });

  it('reads the short list of ways to write no as no, and an absent play as no', () => {
    // The positive twin is the test above: the same reader answers yes to everything else.
    for (const query of ['play=0', 'play=no', 'play=false', 'play=off', 'play=OFF', 'play=False']) {
      expect(readPastLink(params(`past=trip-1&${query}`))?.play, query).toBe(false);
    }
    expect(readPastLink(params('past=trip-1&at=2019-07-06T13:40:00Z'))?.play).toBe(false);
  });

  it('asks for nothing when play is there and no trip is', () => {
    expect(readPastLink(params('play=1&at=2019-07-06T13:40:00Z'))).toBeNull();
  });

  it('reads play without a moment as the trip, set playing from wherever it opens', () => {
    expect(readPastLink(params('past=trip-1&play=1'))).toEqual({
      tripLogId: 'trip-1',
      follow: null,
      at: null,
      play: true,
    });
  });

  it('hands a moment it cannot read on as written, without losing the rest of the link', () => {
    // Parsed where it is used, so a mistyped instant is one absence — the trip, whom it follows
    // and whether it plays are still what the link said.
    expect(readPastLink(params('past=trip-1&caver=2&at=half-past-one&play=1'))).toEqual({
      tripLogId: 'trip-1',
      follow: { kind: 'caver', id: '2' },
      at: 'half-past-one',
      play: true,
    });
  });

  it('ignores names it does not answer to, and keeps them when it writes', () => {
    const address = params('past=trip-1&utm_source=newsletter&speed=300');
    expect(readPastLink(address)).toEqual({
      tripLogId: 'trip-1',
      follow: null,
      at: null,
      play: false,
    });
    const written = momentLink(address, 'trip-1', null, Date.parse('2019-07-06T13:40:00Z'), true);
    expect(written.get('utm_source')).toBe('newsletter');
    expect(written.get('speed')).toBe('300');
  });

  it('writes a moment to the second in UTC, and reads the same link back', () => {
    const follow = { kind: 'team', id: 'team-a' } as const;
    const written = momentLink(
      params('lang=en'),
      'trip-1',
      follow,
      Date.parse('2019-07-06T13:40:27.650Z'),
      true,
    );
    expect(written.toString()).toBe(
      'lang=en&past=trip-1&team=team-a&at=2019-07-06T13%3A40%3A27Z&play=1',
    );
    expect(readPastLink(written)).toEqual({
      tripLogId: 'trip-1',
      follow,
      at: '2019-07-06T13:40:27Z',
      play: true,
    });
  });

  it('writes no play into a link that only names the moment', () => {
    const written = momentLink(params(''), 'trip-1', null, Date.parse('2019-07-06T13:40:00Z'), false);
    expect(written.has('play')).toBe(false);
    expect(readPastLink(written)).toEqual({
      tripLogId: 'trip-1',
      follow: null,
      at: '2019-07-06T13:40:00Z',
      play: false,
    });
  });

  it('replaces a moment and a play the address already carried, rather than adding to them', () => {
    const written = momentLink(
      params('past=trip-0&caver=2&at=2001-01-01T00:00:00Z&play=1'),
      'trip-1',
      { kind: 'team', id: null },
      Date.parse('2019-07-06T13:40:00Z'),
      false,
    );
    expect(written.toString()).toBe('past=trip-1&team=&at=2019-07-06T13%3A40%3A00Z');
  });

  it('writes the trip alone when the replay has no moment to name yet', () => {
    // A track still in flight, or a trip with nothing to play: there is no clock to read.
    expect(momentLink(params(''), 'trip-1', null, null, true).toString()).toBe('past=trip-1&play=1');
    expect(momentLink(params(''), 'trip-1', null, Number.NaN, false).toString()).toBe('past=trip-1');
  });
});
