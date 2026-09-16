// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackingParticipant, TrackingState } from '../api/hooks.ts';
import {
  sharedTeamTitle,
  teamStation,
  trackedCaverTeams,
  trackedCaversFrom,
  undergroundFirst,
  type TrackedCaver,
} from './trackedCavers.ts';

const MODEL = 'model-1';

function participant(overrides: Partial<TrackingParticipant> = {}): TrackingParticipant {
  return {
    caverId: 'caver-1',
    teamId: 'team-1',
    lastKind: 'atStation',
    lastRecordedAt: '2026-09-12T09:00:00Z',
    // The ordinary case: the last thing this person said was where they were, so the two moments
    // agree. Every test that needs them to disagree says both.
    positionRecordedAt: '2026-09-12T09:00:00Z',
    stationName: 'p.g.7',
    depthM: null,
    in: true,
    out: false,
    // What a published page would caption this person with. Nothing on a signed-in surface reads
    // it — those show the roster's own name — so null here, which is also its ordinary value.
    label: null,
    ...overrides,
  };
}

function state(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: MODEL,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T08:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    publishesRealNames: true,
    teams: [{ id: 'team-1', title: 'Echipa 1' }],
    participants: [participant()],
    ...overrides,
  };
}

const roster = (caverId: string) =>
  caverId === 'caver-1'
    ? { name: 'Ana', enteredAt: '2026-09-12T08:05:00Z' }
    : { name: 'Somebody not on the roster' };

describe('trackedCaversFrom', () => {
  it('places a reported station, named from the roster and titled from the teams', () => {
    expect(trackedCaversFrom(state(), roster, MODEL)).toEqual([
      {
        caverId: 'caver-1',
        name: 'Ana',
        // Both, because they answer different questions: the title is what is printed over a
        // gathering of the party, and the id is what the gathering is keyed on — two teams of one
        // trip may be called the same thing, and a team may be called nothing at all.
        teamId: 'team-1',
        teamTitle: 'Echipa 1',
        position: { kind: 'station', station: 'p.g.7' },
        lastRecordedAt: '2026-09-12T09:00:00Z',
        // The latest report is itself the station report, so the two times are the same one.
        positionAt: '2026-09-12T09:00:00Z',
        enteredAt: '2026-09-12T08:05:00Z',
        out: false,
      },
    ]);
  });

  it('dates a position from the report that placed somebody, whatever came after it', () => {
    // The watch carries both moments now: the place from the last report that named one, and that
    // report's own time beside the time of the last word of any kind. So a station reported at
    // 09:00 keeps 09:00 after a radio note at 11:00 — where the fold used to answer "unknown"
    // for every kind that carries no place, which left the comparison next door ranking people
    // by when they last spoke.
    const dated = (lastKind: TrackingParticipant['lastKind']) =>
      trackedCaversFrom(
        state({
          participants: [
            participant({
              lastKind,
              lastRecordedAt: '2026-09-12T11:00:00Z',
              positionRecordedAt: '2026-09-12T09:00:00Z',
            }),
          ],
        }),
        roster,
        MODEL,
      )[0];

    for (const kind of ['atStation', 'atDepth', 'note', 'entered', 'exited'] as const) {
      expect(dated(kind).positionAt, kind).toBe('2026-09-12T09:00:00Z');
      // The twin, on the same row: the other moment is still the other moment. A fold that had
      // simply started copying the last word into both would pass the line above and fail here.
      expect(dated(kind).lastRecordedAt, kind).toBe('2026-09-12T11:00:00Z');
    }
  });

  it('leaves an undated position undated rather than borrowing the last word', () => {
    // Nothing has placed this person, or a position exists and this reader may not be told it —
    // the read sends no moment for either, deliberately, and the two are not to be told apart.
    // Filling that gap from `lastRecordedAt` is the defect this field exists to prevent: it would
    // date a station from a report that named no station.
    const undated = trackedCaversFrom(
      state({
        participants: [
          participant({
            lastKind: 'note',
            lastRecordedAt: '2026-09-12T11:00:00Z',
            positionRecordedAt: null,
          }),
        ],
      }),
      roster,
      MODEL,
    )[0];

    expect(undated.positionAt).toBeNull();
    // And the twin: the last word is still carried, so the null above is a refusal to substitute
    // rather than a fold that lost both moments.
    expect(undated.lastRecordedAt).toBe('2026-09-12T11:00:00Z');
  });

  it('says a withheld position was withheld, as strongly as it is actually known', () => {
    // A station report always carries a place, so one arriving without a place can only be a
    // withholding — that is the case said as a fact.
    const certain = trackedCaversFrom(
      state({
        positionsWithheld: true,
        participants: [participant({ stationName: null, lastKind: 'atStation', positionRecordedAt: null })],
      }),
      roster,
      MODEL,
    );
    expect(certain[0].position).toEqual({ kind: 'withheld', certain: true });

    // A note carries no place at all, so the absence after one could be either. Claiming a
    // withholding there would be a statement about the world made out of a gap.
    const maybe = trackedCaversFrom(
      state({
        positionsWithheld: true,
        participants: [participant({ stationName: null, lastKind: 'note', positionRecordedAt: null })],
      }),
      roster,
      MODEL,
    );
    expect(maybe[0].position).toEqual({ kind: 'withheld', certain: false });
  });

  it('invents no withholding where nothing is being withheld', () => {
    // Nobody has reported this person at all: there is no position to keep from anybody, and
    // saying "withheld" would invent a secret.
    const nobodyReported = trackedCaversFrom(
      state({
        positionsWithheld: true,
        participants: [
          participant({
            stationName: null,
            lastKind: null,
            lastRecordedAt: null,
            positionRecordedAt: null,
            in: false,
          }),
        ],
      }),
      roster,
      MODEL,
    );
    expect(nobodyReported[0].position).toEqual({ kind: 'unreported' });

    const nothingWithheld = trackedCaversFrom(
      state({ participants: [participant({ stationName: null, lastKind: 'note' })] }),
      roster,
      MODEL,
    );
    expect(nothingWithheld[0].position).toEqual({ kind: 'unreported' });
  });

  it('reports a depth as a depth rather than placing it at a station', () => {
    const cavers = trackedCaversFrom(
      state({ participants: [participant({ stationName: null, lastKind: 'atDepth', depthM: 35 })] }),
      roster,
      MODEL,
    );
    expect(cavers[0].position).toEqual({ kind: 'depth', depthM: 35 });
  });

  it('draws nobody when the watch is resolved against another model', () => {
    // Station names belong to the model they were measured in. Drawing them on a different cave
    // would be a confident claim about where somebody is, made from a name that happens to
    // collide with one in the model on screen.
    expect(trackedCaversFrom(state({ surveyModelId: 'model-2' }), roster, MODEL)).toEqual([]);
    expect(trackedCaversFrom(state({ surveyModelId: null }), roster, MODEL)).toEqual([]);
    expect(trackedCaversFrom(state(), roster, undefined)).toEqual([]);
  });

  it('leaves a team it cannot name, and a caver on no team, without one', () => {
    const cavers = trackedCaversFrom(
      state({
        participants: [
          participant({ teamId: null }),
          participant({ caverId: 'caver-2', teamId: 'team-gone' }),
        ],
      }),
      roster,
      MODEL,
    );
    expect(cavers.map((caver) => caver.teamTitle)).toEqual([null, null]);
    expect(cavers[1].name).toBe('Somebody not on the roster');
    // The id survives a title that could not be resolved: the team is still a team, and the
    // gathering it belongs in is still its own, headed by whatever the reader's language calls
    // a team nobody named.
    expect(cavers.map((caver) => caver.teamId)).toEqual([null, 'team-gone']);
  });
});

const caver = (overrides: Partial<TrackedCaver> = {}): TrackedCaver => {
  const merged = {
    caverId: 'caver-1',
    name: 'Ana',
    teamId: 'team-1',
    teamTitle: 'Echipa 1',
    position: { kind: 'station', station: 'p.g.7' } as TrackedCaver['position'],
    lastRecordedAt: '2026-09-12T09:00:00Z' as string | null,
    enteredAt: '2026-09-12T08:00:00Z' as string | null,
    out: false,
    ...overrides,
  };
  // The ordinary case, so it is the default: somebody's position is as old as their latest report,
  // because the last thing they said was where they were. A test that wants the two to disagree —
  // which is the whole of what the comparison below is about — says so, and `null` stays `null`.
  return {
    ...merged,
    positionAt: 'positionAt' in overrides ? (overrides.positionAt ?? null) : merged.lastRecordedAt,
  };
};

/**
 * The word over a group of people standing at one station — and, mostly, when there is not one.
 *
 * A heading is a claim about who the names under it are, so every case that would make the claim
 * false or empty answers with nothing and leaves the names to speak for themselves.
 */
describe('sharedTeamTitle', () => {
  it('names the team where every one of them is on it', () => {
    expect(
      sharedTeamTitle([
        caver({ caverId: 'a', teamId: 'team-1', teamTitle: 'Echipa 1' }),
        caver({ caverId: 'b', teamId: 'team-1', teamTitle: 'Echipa 1' }),
      ]),
    ).toBe('Echipa 1');
  });

  it('names nothing for a mixture of teams, even one of teams with the same title', () => {
    // Keyed on the id like everything else here: two teams of one trip may be called the same
    // thing, and a heading taken from the title would call a chance meeting of both a team.
    expect(
      sharedTeamTitle([
        caver({ caverId: 'a', teamId: 'team-1', teamTitle: 'Echipa' }),
        caver({ caverId: 'b', teamId: 'team-2', teamTitle: 'Echipa' }),
      ]),
    ).toBeNull();
  });

  it('names nothing where anybody is on no team, and nothing for a team nobody named', () => {
    expect(
      sharedTeamTitle([
        caver({ caverId: 'a', teamId: 'team-1', teamTitle: 'Echipa 1' }),
        caver({ caverId: 'b', teamId: null, teamTitle: null }),
      ]),
    ).toBeNull();
    expect(sharedTeamTitle([caver({ teamId: null, teamTitle: null })])).toBeNull();
    expect(sharedTeamTitle([caver({ teamId: 'team-1', teamTitle: null })])).toBeNull();
    expect(sharedTeamTitle([])).toBeNull();
  });

});

/**
 * The order the names of one collapsed marker are drawn in.
 *
 * A marker standing for a party says where those people were last reported, and somebody who has
 * come out is among them — the station is where they were, not where they are. Putting them below
 * whoever is still underground makes "is this party still down there" a question the shape of the
 * block answers.
 */
describe('undergroundFirst', () => {
  it('sets whoever has come out below whoever has not', () => {
    expect(
      undergroundFirst([
        caver({ caverId: 'a', out: true }),
        caver({ caverId: 'b' }),
        caver({ caverId: 'c', out: true }),
        caver({ caverId: 'd' }),
      ]).map((member) => member.caverId),
    ).toEqual(['b', 'd', 'a', 'c']);
  });

  it('leaves the watch’s own order alone within each of the two', () => {
    // The watch lists the trip's roster, which is a club's arrangement of its own party. Sorting
    // it by anything the radio said a minute ago would rearrange somebody's trip to suit the last
    // report; the only thing that moves a name here is that person coming out.
    expect(
      undergroundFirst([
        caver({ caverId: 'a' }),
        caver({ caverId: 'b' }),
        caver({ caverId: 'c' }),
      ]).map((member) => member.caverId),
    ).toEqual(['a', 'b', 'c']);
    expect(undergroundFirst([])).toEqual([]);
  });
});

describe('trackedCaverTeams', () => {
  it('gathers the party by the team’s id and never by its title', () => {
    // The case a gathering by title gets wrong, and gets wrong silently: two teams of one trip
    // called the same thing would be merged into one heading, and pressing that heading would
    // show one of the two places as if it were both.
    const groups = trackedCaverTeams([
      caver({ caverId: 'a', teamId: 'team-1', teamTitle: 'Echipa' }),
      caver({ caverId: 'b', teamId: 'team-2', teamTitle: 'Echipa' }),
    ]);

    expect(groups).toHaveLength(2);
    expect(groups.map((group) => group.teamId)).toEqual(['team-1', 'team-2']);
    expect(groups.map((group) => group.members.map((member) => member.caverId))).toEqual([
      ['a'],
      ['b'],
    ]);
  });

  it('keeps the order the watch lists the party in, and puts everybody on no team last', () => {
    // A club's own arrangement of its party, not a ranking. Re-ordering it by size or by whoever
    // spoke last would rearrange somebody's trip to suit what the radio happened to say.
    const groups = trackedCaverTeams([
      caver({ caverId: 'a', teamId: null, teamTitle: null }),
      caver({ caverId: 'b', teamId: 'team-2', teamTitle: 'Two' }),
      caver({ caverId: 'c', teamId: 'team-1', teamTitle: 'One' }),
      caver({ caverId: 'd', teamId: 'team-2', teamTitle: 'Two' }),
    ]);

    expect(groups.map((group) => group.teamId)).toEqual(['team-2', 'team-1', null]);
    expect(groups[0].members.map((member) => member.caverId)).toEqual(['b', 'd']);
    expect(groups[2].title).toBeNull();
  });

  it('draws no group for everybody-on-no-team when nobody is on one', () => {
    const groups = trackedCaverTeams([caver({ teamId: 'team-1' })]);
    expect(groups.map((group) => group.teamId)).toEqual(['team-1']);
  });
});

describe('teamStation', () => {
  it('takes the station of whoever spoke most recently', () => {
    // A team underground moves together, so where it is is whatever it last said over the radio.
    expect(
      teamStation([
        caver({
          caverId: 'a',
          position: { kind: 'station', station: 'p.g.3' },
          lastRecordedAt: '2026-09-12T09:00:00Z',
        }),
        caver({
          caverId: 'b',
          position: { kind: 'station', station: 'p.g.9' },
          lastRecordedAt: '2026-09-12T09:40:00Z',
        }),
      ]),
    ).toBe('p.g.9');
  });

  it('passes over members with nowhere to stand rather than counting them against the rest', () => {
    // Somebody whose position was withheld, or who reported a depth, says nothing about where the
    // team is standing — and a team of three with one placed member is still a team a reader can
    // be shown. The withheld member is the one that matters: treating them as "no position known"
    // for the team would be the withholding turning into an absence one level up.
    expect(
      teamStation([
        caver({
          caverId: 'a',
          position: { kind: 'withheld', certain: true },
          lastRecordedAt: '2026-09-12T10:00:00Z',
        }),
        caver({
          caverId: 'b',
          position: { kind: 'depth', depthM: 40 },
          lastRecordedAt: '2026-09-12T09:50:00Z',
        }),
        caver({
          caverId: 'c',
          position: { kind: 'station', station: 'p.g.4' },
          lastRecordedAt: '2026-09-12T08:00:00Z',
        }),
      ]),
    ).toBe('p.g.4');
  });

  it('answers nothing for a team none of which has been placed', () => {
    expect(teamStation([caver({ position: { kind: 'unreported' }, lastRecordedAt: null })])).toBeNull();
    expect(teamStation([])).toBeNull();
  });

  it('does not let somebody who has come out say where the team is standing', () => {
    // The defect this guards, in the shape it happens: a team of four reaches the far end of the
    // cave, one of them walks out, and the surface records that they are out. That report carries
    // no place, so the watch keeps their old station near the entrance — and it is now the newest
    // report anybody on the team has made. Read naively, the team is shown at the pitch head, named
    // by the one member who is already above ground, on a surface a rescue co-ordinator reads.
    expect(
      teamStation([
        caver({
          caverId: 'gone-out',
          position: { kind: 'station', station: 'p.42' },
          lastRecordedAt: '2026-09-12T14:30:00Z',
          positionAt: '2026-09-12T13:00:00Z',
          out: true,
        }),
        caver({
          caverId: 'still-in',
          position: { kind: 'station', station: 'p.90' },
          lastRecordedAt: '2026-09-12T13:50:00Z',
        }),
      ]),
    ).toBe('p.90');
  });

  it('still answers for a team every one of which is out', () => {
    // An order of preference, not a filter. Where they came from is the last thing known about
    // them and is what a search would start from, so it is said rather than withheld.
    expect(
      teamStation([
        caver({
          caverId: 'a',
          position: { kind: 'station', station: 'p.10' },
          lastRecordedAt: '2026-09-12T14:00:00Z',
          out: true,
        }),
        caver({
          caverId: 'b',
          position: { kind: 'station', station: 'p.20' },
          lastRecordedAt: '2026-09-12T14:30:00Z',
          out: true,
        }),
      ]),
    ).toBe('p.20');
  });

  it('compares when a position was reported, not when somebody last spoke', () => {
    // A radio note moves the latest report and moves nobody. The member who said "we are fine" at
    // 14:30 was last placed at 13:00; their colleague's station is genuinely newer at 13:50, and
    // that is the team's place — read off the moments the positions themselves carry, so the
    // talker's newer word cannot speak for where the team is standing.
    expect(
      teamStation([
        caver({
          caverId: 'talker',
          position: { kind: 'station', station: 'p.42' },
          lastRecordedAt: '2026-09-12T14:30:00Z',
          positionAt: '2026-09-12T13:00:00Z',
        }),
        caver({
          caverId: 'mover',
          position: { kind: 'station', station: 'p.90' },
          lastRecordedAt: '2026-09-12T13:50:00Z',
        }),
      ]),
    ).toBe('p.90');
  });

  it('falls back to the latest report where no position on the team can be dated', () => {
    // A last resort, and it is about ranking members against each other rather than about drawing
    // an age: where no position on the team carries a moment at all — a place sent without one, or
    // one stamped with an instant that will not parse — giving up would leave the team with no
    // place, when the times beside them are the best answer available and are usually the right
    // one. Nothing here reaches a reader as the age of a station; the surfaces draw an undated
    // position as undated.
    expect(
      teamStation([
        caver({
          caverId: 'a',
          position: { kind: 'station', station: 'p.3' },
          lastRecordedAt: '2026-09-12T09:00:00Z',
          positionAt: null,
        }),
        caver({
          caverId: 'b',
          position: { kind: 'station', station: 'p.9' },
          lastRecordedAt: '2026-09-12T09:40:00Z',
          positionAt: null,
        }),
      ]),
    ).toBe('p.9');
  });

  it('places a member whose report has no readable time, but never lets one win', () => {
    // A report whose instant cannot be read still places somebody — it is used where nothing
    // better has been said, and loses to anything that can be compared.
    const unreadable = caver({
      caverId: 'a',
      position: { kind: 'station', station: 'p.g.1' },
      lastRecordedAt: 'not a time',
    });
    expect(teamStation([unreadable])).toBe('p.g.1');
    expect(
      teamStation([
        unreadable,
        caver({
          caverId: 'b',
          position: { kind: 'station', station: 'p.g.2' },
          lastRecordedAt: '2026-09-12T09:00:00Z',
        }),
      ]),
    ).toBe('p.g.2');
  });
});
