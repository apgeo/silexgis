// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  lastHeardAtIso,
  lastHeardInWords,
  trackingStandingOf,
  trackingStandings,
} from './trackingWatch.ts';

const NOON = Date.parse('2026-09-16T12:00:00Z');

/** Somebody reported at a station, and neither out nor silent. */
const heard = { lastKind: 'atStation', out: false } as const;
/** Somebody reported out. */
const outside = { lastKind: 'exited', out: true } as const;
/** Somebody nobody has said a single word about — no report of any kind exists. */
const silent = { lastKind: null, out: false } as const;

describe('where somebody on a watch stands', () => {
  /**
   * The rule the whole line exists for. Silence is not information: a caver nobody has reported
   * has not been placed underground and has not come out, and a surface that folded them into
   * either would tell a coordinator something nobody has said.
   */
  it('keeps silence apart from both of the other two', () => {
    expect(trackingStandingOf(silent)).toBe('unheard');
    // The positive twins, so this cannot pass by calling everybody unheard.
    expect(trackingStandingOf(heard)).toBe('underground');
    expect(trackingStandingOf(outside)).toBe('out');
  });

  /**
   * <b>Read from what the last report *was*, never from whether one exists.</b> The two questions
   * are different — "has anybody said anything at all" and "has anybody said they went in" — and
   * the type of this function is what now pins which one is asked: there is no moment on it to
   * mistake for an answer. Each kind that states a standing is checked separately, because "it
   * came out right for a station" says nothing about what an entry does.
   */
  it('reads each kind of report for what it says about where somebody is', () => {
    expect(trackingStandingOf({ lastKind: 'entered', out: false })).toBe('underground');
    expect(trackingStandingOf({ lastKind: 'atStation', out: false })).toBe('underground');
    expect(trackingStandingOf({ lastKind: 'atDepth', out: false })).toBe('underground');
    expect(trackingStandingOf({ lastKind: 'exited', out: true })).toBe('out');
    expect(trackingStandingOf({ lastKind: null, out: false })).toBe('unheard');
  });

  /**
   * <b>The one case this client cannot answer, pinned on purpose so that changing it is a
   * decision.</b> A note says something happened, not where and not whether, so the standing it
   * leaves behind is whatever came before it — which a single report cannot show. Read as
   * underground here: a party that went in at nine and radioed "all fine" at eleven has a note as
   * its last word, and calling *them* "not heard from" would report a party demonstrably in a cave
   * as one that may never have set off. The cost is the other way round and is real — somebody
   * whose only report is a note about not reaching them is drawn as underground — and it is
   * removed, not reduced, when the read carries the server's own fold.
   */
  it('reads a note as leaving somebody where they were, which it can only assume', () => {
    expect(trackingStandingOf({ lastKind: 'note', out: false })).toBe('underground');
    // And the twin that keeps the assumption from swallowing the state it is nearest to: a note is
    // assumed, no report at all is known, and the two must not arrive at the same answer.
    expect(trackingStandingOf({ lastKind: null, out: false })).toBe('unheard');
  });

  // Out is a statement somebody made and it outranks the rest: a report that says nothing about a
  // place must not move somebody back into a cave they have already left.
  it('reads out as out whatever else the row carries', () => {
    expect(trackingStandingOf({ lastKind: null, out: true })).toBe('out');
    expect(trackingStandingOf({ lastKind: 'note', out: true })).toBe('out');
    expect(trackingStandingOf({ lastKind: 'atStation', out: true })).toBe('out');
  });
});

describe('how the party divides', () => {
  /**
   * The count that was missing. A watch that said "1 underground, 1 out" over a party of three
   * would be leaving out the one person the coordinator most needs to think about, and doing it
   * silently — the figures would look complete.
   */
  it('counts the silent as their own figure, and loses nobody', () => {
    const counts = trackingStandings([heard, outside, silent, silent]);

    expect(counts).toEqual({ underground: 1, out: 1, unheard: 2 });
    expect(counts.underground + counts.out + counts.unheard).toBe(4);
  });

  /**
   * The count and the row have to be made the same way. Two readings of the same party — one for
   * the tag beside a name, one for the figure above the table — is how a watch ends up saying "2
   * underground" over three blue tags, and the reader cannot tell which of the two is lying.
   */
  it('divides the party exactly as the rows are tagged, kind for kind', () => {
    const party = [
      { lastKind: 'entered', out: false },
      { lastKind: 'note', out: false },
      { lastKind: 'exited', out: true },
      { lastKind: null, out: false },
    ] as const;

    expect(trackingStandings(party)).toEqual({ underground: 2, out: 1, unheard: 1 });
    expect(party.map((person) => trackingStandingOf(person))).toEqual([
      'underground',
      'underground',
      'out',
      'unheard',
    ]);
  });

  it('answers a party nobody has said anything about with three honest figures', () => {
    expect(trackingStandings([silent, silent])).toEqual({
      underground: 0,
      out: 0,
      unheard: 2,
    });
    // The twin: a party that has been reported is not counted as silent, so the figure above
    // cannot be arrived at by counting everybody.
    expect(trackingStandings([heard, heard])).toEqual({
      underground: 2,
      out: 0,
      unheard: 0,
    });
  });

  it('answers an empty roster with zeroes rather than with nothing', () => {
    expect(trackingStandings([])).toEqual({ underground: 0, out: 0, unheard: 0 });
  });
});

describe('how old the last word is', () => {
  /**
   * The moment this read carries is the moment of the last report of *any* kind. It is not the
   * moment the position beside it was reported, and this test pins which of the two is being
   * answered: a row whose last word is a note is aged from the note, not from the station that
   * was reported hours earlier.
   */
  it('ages a row from its last word and from nothing else', () => {
    const noteAtEleven = { lastRecordedAt: '2026-09-16T11:00:00Z' };

    expect(lastHeardAtIso(noteAtEleven)).toBe('2026-09-16T11:00:00Z');
    expect(lastHeardInWords(noteAtEleven, NOON, 'en')).toBe('1 hour ago');
  });

  it('says how long ago rather than at what time', () => {
    expect(lastHeardInWords({ lastRecordedAt: '2026-09-16T09:00:00Z' }, NOON, 'en')).toBe(
      '3 hours ago',
    );
    expect(lastHeardInWords({ lastRecordedAt: '2026-09-16T11:58:00Z' }, NOON, 'en')).toBe(
      '2 minutes ago',
    );
  });

  /**
   * Silence gets no age, because every set of words for the age of a report that never arrived —
   * "just now", "0 minutes ago", a date in 1970 — is a claim this watch was never given. The
   * caller draws silence as silence; what this refuses to do is invent a number for it.
   */
  it('gives a person nobody has reported no age at all', () => {
    expect(lastHeardAtIso({ lastRecordedAt: null })).toBeNull();
    expect(lastHeardInWords({ lastRecordedAt: null }, NOON, 'en')).toBeNull();
    // And the twin: a moment that does exist is turned into words.
    expect(lastHeardInWords({ lastRecordedAt: '2026-09-16T11:00:00Z' }, NOON, 'en')).not.toBeNull();
  });

  // The words are the reader's, not English with the numbers swapped — the same rule the followed
  // page uses, called rather than copied.
  it('says it in the reader’s own language', () => {
    expect(lastHeardInWords({ lastRecordedAt: '2026-09-16T09:00:00Z' }, NOON, 'ro')).toContain(
      'ore',
    );
  });
});
