// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackingParticipant, TrackingState } from '../api/hooks.ts';
import { trackedCaversFrom } from './trackedCavers.ts';

const MODEL = 'model-1';

function participant(overrides: Partial<TrackingParticipant> = {}): TrackingParticipant {
  return {
    caverId: 'caver-1',
    teamId: 'team-1',
    lastKind: 'atStation',
    lastRecordedAt: '2026-09-12T09:00:00Z',
    stationName: 'p.g.7',
    depthM: null,
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
        teamTitle: 'Echipa 1',
        position: { kind: 'station', station: 'p.g.7' },
        lastRecordedAt: '2026-09-12T09:00:00Z',
        enteredAt: '2026-09-12T08:05:00Z',
        out: false,
      },
    ]);
  });

  it('says a withheld position was withheld, as strongly as it is actually known', () => {
    // A station report always carries a place, so one arriving without a place can only be a
    // withholding — that is the case said as a fact.
    const certain = trackedCaversFrom(
      state({
        positionsWithheld: true,
        participants: [participant({ stationName: null, lastKind: 'atStation' })],
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
        participants: [participant({ stationName: null, lastKind: 'note' })],
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
        participants: [participant({ stationName: null, lastKind: null, lastRecordedAt: null })],
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
  });
});
