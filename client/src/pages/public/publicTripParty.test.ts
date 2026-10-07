// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PublicTripParticipant } from '../../api/hooks.ts';
import {
  clockInWords,
  durationInWords,
  followedSpanInWords,
  momentOrAge,
  watchStartedAt,
  instantOf,
  partyByTeam,
  partyStandings,
  positionAgeInWords,
  sinceInWords,
  standingOf,
  tripsByCamp,
} from './publicTripParty.ts';

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';
const GONE = '33333333-3333-3333-3333-333333333333';

function participant(overrides: Partial<PublicTripParticipant> = {}): PublicTripParticipant {
  return {
    ordinal: 1,
    label: null,
    teamId: null,
    stationName: null,
    depthM: null,
    lastRecordedAt: null,
    positionRecordedAt: null,
    positionOnOtherModel: false,
    in: false,
    out: false,
    ...overrides,
  };
}

describe('where one member of a published party stands', () => {
  it('tells somebody who has not been reported apart from somebody who is out', () => {
    // The whole reason this is three states and not two: a page read by somebody waiting at home
    // must not draw a party that has not set off as one that is already back.
    expect(standingOf(participant({ in: false, out: false }))).toBe('unheard');
    expect(standingOf(participant({ in: true, out: false }))).toBe('underground');
    expect(standingOf(participant({ in: false, out: true }))).toBe('out');
  });

  it('counts the party into the three', () => {
    expect(
      partyStandings([
        participant({ in: true }),
        participant({ in: true }),
        participant({ out: true }),
        participant(),
      ]),
    ).toEqual({ underground: 2, out: 1, unheard: 1 });
  });
});

describe('arranging a published party', () => {
  const teams = [
    { id: TEAM_A, title: 'Advance' },
    { id: TEAM_B, title: 'Survey' },
  ];

  it('keeps the club’s own order of teams and gathers the unteamed at the end', () => {
    const groups = partyByTeam(
      [
        participant({ ordinal: 1, teamId: TEAM_B }),
        participant({ ordinal: 2, teamId: TEAM_A }),
        participant({ ordinal: 3, teamId: null }),
      ],
      teams,
    );

    expect(groups.map((group) => group.title)).toEqual(['Advance', 'Survey', null]);
    expect(groups[0].members.map((member) => member.ordinal)).toEqual([2]);
    expect(groups[2].members.map((member) => member.ordinal)).toEqual([3]);
  });

  it('leaves out a team nobody is on', () => {
    const groups = partyByTeam([participant({ teamId: TEAM_A })], teams);

    expect(groups.map((group) => group.title)).toEqual(['Advance']);
  });

  it('still shows somebody whose team the envelope never named', () => {
    // Nothing should produce this. The alternative to handling it is a person quietly missing from
    // the one page their family is watching.
    const groups = partyByTeam([participant({ ordinal: 9, teamId: GONE })], teams);

    expect(groups).toHaveLength(1);
    expect(groups[0].title).toBeNull();
    expect(groups[0].members.map((member) => member.ordinal)).toEqual([9]);
  });
});

describe('how long ago the last word was', () => {
  const now = Date.parse('2026-09-14T12:00:00Z');

  it('says the gap rather than the clock, in the units a reader thinks in', () => {
    expect(sinceInWords('2026-09-14T11:58:00Z', now, 'en')).toMatch(/2 minutes ago/);
    expect(sinceInWords('2026-09-14T09:00:00Z', now, 'en')).toMatch(/3 hours ago/);
    expect(sinceInWords('2026-09-12T12:00:00Z', now, 'en')).toMatch(/2 days ago/);
  });

  it('rounds towards the longer silence rather than away from it', () => {
    // Two hours and fifty minutes is "2 hours ago" and never "3": a page that rounded up would be
    // over-reporting a silence, and one that rounded to nearest would under-report the other half
    // of the time. Down is the direction that is never a claim the reports do not support.
    expect(sinceInWords('2026-09-14T09:10:00Z', now, 'en')).toMatch(/2 hours ago/);
  });

  it('is written in the language the page is being read in', () => {
    expect(sinceInWords('2026-09-14T11:58:00Z', now, 'ro')).toMatch(/2 minute/);
  });
});

describe('how long ago a position was reported', () => {
  const now = Date.parse('2026-09-14T12:00:00Z');

  /**
   * The position's own moment, worded by the same rule as every other gap on these pages. Read
   * together with the row above: the two ages are rounded identically, so a coordinator and a
   * family looking at one station cannot be told it is two different ages.
   */
  it('words the moment it is given, exactly as the last word is worded', () => {
    expect(positionAgeInWords('2026-09-14T08:00:00Z', now, 'en')).toBe('4 hours ago');
    expect(positionAgeInWords('2026-09-14T08:00:00Z', now, 'en')).toBe(
      sinceInWords('2026-09-14T08:00:00Z', now, 'en'),
    );
    expect(positionAgeInWords('2026-09-14T11:58:00Z', now, 'ro')).toMatch(/2 minute/);
  });

  /**
   * <b>Silence stays silence, and there is nothing to fall back to.</b> A position nobody reported
   * and one this reader may not be told arrive identically — as no moment — and the answer for
   * both is no age at all. The caller draws that as it likes; what cannot happen is words.
   *
   * The signature is the other half of the guard and is why this test can only be written this
   * way: there is no participant here to read a second field off, so "fill the gap from the last
   * word" is not an implementation this function could have. The surfaces prove the same thing
   * about the field they hand over — see the two pages' own tests.
   */
  it('gives an unreported position no age, and no borrowed one', () => {
    expect(positionAgeInWords(null, now, 'en')).toBeNull();
    // The twin: a moment that exists is turned into words, so the null above is about absence
    // rather than about this function never answering.
    expect(positionAgeInWords('2026-09-14T08:00:00Z', now, 'en')).not.toBeNull();
  });

  /**
   * <b>Three ways a moment goes missing and only one of them is spelled `null`.</b> The generated
   * client declares this field required, so a read answered by anything that does not write it —
   * a server built before the field, a payload trimmed in transit — arrives as `undefined`, which
   * is not `null` and passes any guard written against `null` alone. A string that will not parse
   * arrives looking like a moment and is `NaN` the instant it is read.
   *
   * <b>What made this worth a test is where the failure lands.</b> `Intl.RelativeTimeFormat`
   * throws on a non-finite value rather than returning anything, so the words are not merely wrong:
   * the throw leaves render, and the followed page a family is watching is replaced wholesale by
   * the application's error boundary. One undated position, and nobody can see the party at all.
   */
  it('reads an absent and an unreadable moment as the silence they are, not as a crash', () => {
    expect(positionAgeInWords(undefined, now, 'en')).toBeNull();
    expect(positionAgeInWords('not-a-date', now, 'en')).toBeNull();
    expect(positionAgeInWords('', now, 'en')).toBeNull();
    // And the twin, so none of the above passes by way of this function having stopped answering:
    // a moment that reads is still turned into words.
    expect(positionAgeInWords('2026-09-14T08:00:00Z', now, 'en')).toBe('4 hours ago');
  });
});

/**
 * The rule the two above are built on, asked directly.
 *
 * Worth its own tests because it is now the single home of "there is no moment here", read from
 * both pages, from the marker labels on the model and from the arithmetic that decides which
 * member of a team speaks for its position — and each of those does something different and
 * equally unhelpful with a `NaN` that reaches it.
 */
describe('reading a reported moment', () => {
  it('answers a comparable instant for a moment that reads', () => {
    expect(instantOf('2026-09-14T08:00:00Z')).toBe(Date.parse('2026-09-14T08:00:00Z'));
  });

  it('answers no instant for each of the ways a moment can be missing', () => {
    expect(instantOf(null)).toBeNull();
    expect(instantOf(undefined)).toBeNull();
    expect(instantOf('not-a-date')).toBeNull();
    expect(instantOf('')).toBeNull();
  });

  /**
   * The property the callers actually depend on: whatever comes back is either null or a number
   * arithmetic and the formatters can be handed. `NaN` is the one answer that would pass a
   * `!== null` check and then break everything downstream of it.
   */
  it('never answers a number that is not one', () => {
    for (const value of [null, undefined, 'not-a-date', '', '2026-09-14T08:00:00Z']) {
      const at = instantOf(value);
      expect(at === null || Number.isFinite(at)).toBe(true);
    }
  });
});

/**
 * Moments built from the reader's own calendar rather than from UTC strings, so these read the
 * same on a machine in any time zone: what is under test is "today where the reader is".
 */
const local = (day: number, hour: number, minute = 0) => new Date(2026, 8, day, hour, minute).getTime();
const iso = (instant: number) => new Date(instant).toISOString();

describe('the hour of a moment', () => {
  it('is the hour alone on the reader’s own day', () => {
    const said = clockInWords(local(14, 8, 40), local(14, 15), 'en');

    expect(said).toMatch(/8:40/);
    expect(said).not.toMatch(/Sep|2026/);
  });

  it('carries its date once it is not today, so yesterday evening is not read as tonight', () => {
    const said = clockInWords(local(13, 23, 50), local(14, 0, 10), 'en');

    expect(said).toMatch(/11:50/);
    expect(said).toMatch(/Sep 13/);
  });

  it('is said in the reader’s language', () => {
    expect(clockInWords(local(13, 23, 50), local(14, 0, 10), 'ro')).toMatch(/13 sept\. 2026, 23:50/);
  });
});

describe('a moment said as a gap or as an hour', () => {
  const heardAt = local(14, 11, 55);
  const now = local(14, 12);

  it('is a gap while the trip is being followed', () => {
    expect(momentOrAge(iso(heardAt), now, 'en', false)).toBe('5 minutes ago');
  });

  it('is the hour once the trip has settled, and no longer grows', () => {
    const atNoon = momentOrAge(iso(heardAt), now, 'en', true);
    const atNight = momentOrAge(iso(heardAt), local(14, 23), 'en', true);

    expect(atNoon).toMatch(/11:55/);
    expect(atNoon).not.toMatch(/ago/);
    // The point of it: eleven hours on, the same words.
    expect(atNight).toBe(atNoon);
  });

  it('says nothing for a moment that is not one, in either wording', () => {
    for (const settled of [false, true]) {
      expect(momentOrAge(null, now, 'en', settled)).toBeNull();
      expect(momentOrAge(undefined, now, 'en', settled)).toBeNull();
      expect(momentOrAge('not a moment', now, 'en', settled)).toBeNull();
    }
    // The twin, so the nulls above are not a function that answers nothing at all.
    expect(momentOrAge(iso(heardAt), now, 'en', false)).not.toBeNull();
  });
});

describe('a length of time in words', () => {
  const from = local(14, 8, 40);

  it('rounds down to the minute, never up', () => {
    expect(durationInWords(from, from + 59_999, 'en')).toBe('0 min');
    expect(durationInWords(from, from + 10 * 60_000 + 59_000, 'en')).toBe('10 min');
  });

  it('says hours and minutes, and leaves out a zero of minutes', () => {
    expect(durationInWords(from, from + 190 * 60_000, 'en')).toBe('3 hr 10 min');
    expect(durationInWords(from, from + 180 * 60_000, 'en')).toBe('3 hr');
  });

  it('goes on being a length across midnight and past a day', () => {
    // Half past ten at night until two in the morning: three and a half hours, not minus twenty.
    expect(durationInWords(local(14, 22, 30), local(15, 2), 'en')).toBe('3 hr 30 min');
    expect(durationInWords(from, from + (2 * 24 + 3) * 3_600_000 + 59 * 60_000, 'en')).toBe('2 days 3 hr');
  });

  it('is worded by the reader’s language without a plural form of our own', () => {
    expect(durationInWords(from, from + 190 * 60_000, 'ro')).toBe('3 ore 10 min.');
    expect(durationInWords(from, from + 61 * 60_000, 'ro')).toBe('1 oră 1 min.');
  });

  it('is no length at all when the end is before the start or either is not a moment', () => {
    expect(durationInWords(from, from - 1, 'en')).toBeNull();
    expect(durationInWords(Number.NaN, from, 'en')).toBeNull();
    expect(durationInWords(from, Number.NaN, 'en')).toBeNull();
  });
});

describe('since when a trip has been followed', () => {
  it('is the moment the watch was started', () => {
    expect(watchStartedAt({ armedAt: '2026-09-14T06:00:00Z' })).toBe(Date.parse('2026-09-14T06:00:00Z'));
  });

  it('is nothing where the trip says none, or says something that is not a moment', () => {
    expect(watchStartedAt({ armedAt: null })).toBeNull();
    expect(watchStartedAt({ armedAt: undefined })).toBeNull();
    expect(watchStartedAt({ armedAt: 'soon' })).toBeNull();
  });

  it('says a finished trip’s span once, with one date for a trip inside a day', () => {
    const trip = { armedAt: iso(local(14, 8, 40)), closedAt: iso(local(14, 14, 10)) };

    const today = followedSpanInWords(trip, local(14, 20), 'en');
    expect(today).toMatch(/8:40.*2:10/);
    expect(today).not.toMatch(/Sep/);

    const later = followedSpanInWords(trip, local(20, 9), 'en');
    expect(later).toMatch(/8:40.*2:10/);
    expect(later?.match(/Sep 14/g)).toHaveLength(1);
  });

  it('says both dates for a trip that ran across midnight', () => {
    const said = followedSpanInWords(
      { armedAt: iso(local(14, 22, 30)), closedAt: iso(local(15, 2)) },
      local(20, 9),
      'en',
    );

    expect(said).toMatch(/Sep 14.*Sep 15/);
  });

  it('says no span where either end is missing or they are the wrong way round', () => {
    const start = iso(local(14, 8, 40));
    expect(followedSpanInWords({ armedAt: start, closedAt: null }, local(14, 20), 'en')).toBeNull();
    expect(followedSpanInWords({ armedAt: null, closedAt: start }, local(14, 20), 'en')).toBeNull();
    expect(
      followedSpanInWords({ armedAt: start, closedAt: iso(local(14, 7)) }, local(14, 20), 'en'),
    ).toBeNull();
  });
});

const CAMP_2026 = { id: '44444444-4444-4444-4444-444444444444', name: 'Summer camp' };
const CAMP_2019 = { id: '55555555-5555-5555-5555-555555555555', name: 'Summer camp' };
const trip = (id: string, expedition: { id: string; name: string } | null) => ({ id, expedition });

/**
 * A cave's trips gathered by camp. The order is the server's and the matching is by identifier:
 * a list a club reads as its own history must not be rearranged, and must not fold two camps
 * into one because somebody gave them the same name.
 */
describe('a cave\'s trips arranged by camp', () => {
  it('gathers each camp\'s trips in the order they arrived, with the trips of no camp last', () => {
    const groups = tripsByCamp([
      trip('a', CAMP_2026),
      trip('b', null),
      trip('c', CAMP_2019),
      trip('d', CAMP_2026),
      trip('e', null),
    ]);

    expect(groups.map((group) => [group.camp?.id ?? null, group.trips.map((one) => one.id)])).toEqual([
      [CAMP_2026.id, ['a', 'd']],
      [CAMP_2019.id, ['c']],
      [null, ['b', 'e']],
    ]);
  });

  it('keeps two camps of one name apart', () => {
    const groups = tripsByCamp([trip('a', CAMP_2026), trip('b', CAMP_2019)]);

    expect(groups).toHaveLength(2);
    expect(groups.map((group) => group.camp?.name)).toEqual(['Summer camp', 'Summer camp']);
  });

  it('is one unnamed group where no trip names a camp, and nothing where there are no trips', () => {
    // What a caller draws as the plain list: no camp anywhere means no arrangement to show. A row
    // that arrives without the member at all is the same thing as one that says null.
    const arrived: { id: string; expedition?: { id: string; name: string } | null }[] = [
      trip('a', null),
      { id: 'b' },
    ];
    const groups = tripsByCamp(arrived);
    expect(groups).toHaveLength(1);
    expect(groups[0].camp).toBeNull();
    expect(groups[0].trips.map((one) => one.id)).toEqual(['a', 'b']);

    expect(tripsByCamp([])).toEqual([]);
  });
});
