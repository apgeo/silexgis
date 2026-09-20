// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackingParticipant } from '../../api/hooks.ts';
import {
  lastHeardAtIso,
  lastHeardInWords,
  trackingStandingOf,
  trackingStandings,
} from './trackingWatch.ts';

const NOON = Date.parse('2026-09-16T12:00:00Z');

/**
 * One row of the watch as the read actually sends it.
 *
 * Whole participants rather than the two flags the functions ask for, and that is the point of the
 * factory: the tests below can then put a report kind on a row that disagrees with the standing
 * beside it, which is the only way to show that the kind is no longer being read.
 */
function participant(overrides: Partial<TrackingParticipant> = {}): TrackingParticipant {
  return {
    caverId: 'caver-1',
    teamId: null,
    lastKind: 'entered',
    lastRecordedAt: '2026-09-16T09:00:00Z',
    positionRecordedAt: null,
    stationName: null,
    depthM: null,
    positionSurveyModelId: null,
    in: true,
    out: false,
    label: null,
    // What a published page would print for this person: the caption where somebody typed one,
    // otherwise the roster's own name where the installation publishes names. Null is the
    // ordinary value here and means the page would call them by their place in the party.
    publishedAs: null,
    ...overrides,
  };
}

/** Somebody the server says is underground. */
const heard = participant({ lastKind: 'atStation', in: true, out: false });
/** Somebody reported out. */
const outside = participant({ lastKind: 'exited', in: false, out: true });
/** Somebody nobody has said a single word about — no report of any kind exists. */
const silent = participant({ lastKind: null, lastRecordedAt: null, in: false, out: false });

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
   * <b>The standing is the server's, and this is the test that says so.</b> Each row here carries
   * a report kind that the client's old fold would have read one way and the server's answer the
   * other, and every one of them is answered by the server's answer:
   *
   * - a station report on somebody nobody has stated a standing for. The fold read any kind but an
   *   exit as "inside", so a station made them underground; the domain rule says a station raises
   *   *unheard* to underground and states nothing on its own, and a row that arrives with `in`
   *   false is a row nothing has placed.
   * - a note on somebody who has not been placed — "no answer from Maria", the commonest thing a
   *   relayed phone call carries. The fold drew them as a caver in a cave. They are not.
   * - a note on somebody who *has* been placed, which the fold got right for the wrong reason and
   *   which must keep coming out right.
   * - an entry that the server has since overturned, where reading the kind would put somebody
   *   back inside a cave they were reported out of.
   *
   * Written as whole rows on purpose: the kind is present, it disagrees, and it is ignored.
   */
  it('reads the standing the read carries rather than the kind of the last report', () => {
    expect(trackingStandingOf(participant({ lastKind: 'atStation', in: false, out: false }))).toBe(
      'unheard',
    );
    expect(trackingStandingOf(participant({ lastKind: 'note', in: false, out: false }))).toBe(
      'unheard',
    );
    expect(trackingStandingOf(participant({ lastKind: 'note', in: true, out: false }))).toBe(
      'underground',
    );
    expect(trackingStandingOf(participant({ lastKind: 'entered', in: false, out: true }))).toBe(
      'out',
    );
  });

  // Out is a statement somebody made and it outranks the rest: a report that says nothing about a
  // place must not move somebody back into a cave they have already left.
  it('reads out as out whatever else the row carries', () => {
    expect(trackingStandingOf(participant({ lastKind: null, in: false, out: true }))).toBe('out');
    expect(trackingStandingOf(participant({ lastKind: 'note', in: true, out: true }))).toBe('out');
    expect(trackingStandingOf(participant({ lastKind: 'atStation', in: true, out: true }))).toBe(
      'out',
    );
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
   *
   * The party is the one from the test above: two of these rows carry a kind that disagrees with
   * their standing, so a counter that went back to folding kinds would come out at 3 underground
   * and 0 unheard over the same four rows.
   */
  it('divides the party exactly as the rows are tagged, row for row', () => {
    const party = [
      participant({ lastKind: 'entered', in: true, out: false }),
      participant({ lastKind: 'note', in: true, out: false }),
      participant({ lastKind: 'atStation', in: false, out: false }),
      participant({ lastKind: 'exited', in: false, out: true }),
    ];

    expect(trackingStandings(party)).toEqual({ underground: 2, out: 1, unheard: 1 });
    expect(party.map((person) => trackingStandingOf(person))).toEqual([
      'underground',
      'underground',
      'unheard',
      'out',
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
   * The moment this function carries is the moment of the last report of *any* kind. It is not the
   * moment the position beside it was reported, and this pins which of the two is answered: a row
   * whose last word is a note is aged from the note, not from the station reported hours earlier.
   * The row below carries both moments, so an implementation that reached for the wrong one has a
   * wrong one to reach for.
   */
  it('ages a row from its last word and never from its position', () => {
    const notedAtEleven = participant({
      lastKind: 'note',
      lastRecordedAt: '2026-09-16T11:00:00Z',
      positionRecordedAt: '2026-09-16T08:00:00Z',
      stationName: 'p.g.7',
    });

    expect(lastHeardAtIso(notedAtEleven)).toBe('2026-09-16T11:00:00Z');
    expect(lastHeardInWords(notedAtEleven, NOON, 'en')).toBe('1 hour ago');
    // And the twin, so the assertion above is about which field is read rather than about the
    // wording: the position's own moment is four hours older and is what the place is dated from.
    expect(lastHeardInWords(notedAtEleven, NOON, 'en')).not.toBe('4 hours ago');
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
