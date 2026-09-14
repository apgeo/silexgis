// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PublicTripParticipant } from '../../api/hooks.ts';
import { partyByTeam, partyStandings, sinceInWords, standingOf } from './publicTripParty.ts';

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
