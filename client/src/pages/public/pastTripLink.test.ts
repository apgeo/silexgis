// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { readPastLink, writePastLink } from './pastTripLink.ts';

const params = (query: string) => new URLSearchParams(query);

describe('a past trip named in the page’s own address', () => {
  it('reads a trip, a team and a moment out of a hyperlink', () => {
    expect(readPastLink(params('past=trip-1&team=team-a&at=2019-07-06T13:40:00Z'))).toEqual({
      tripLogId: 'trip-1',
      follow: { kind: 'team', id: 'team-a' },
      at: '2019-07-06T13:40:00Z',
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
    const written = writePastLink(params('past=trip-1&caver=2&at=X&lang=ro'), null, null);
    expect(written.toString()).toBe('lang=ro');
  });
});
