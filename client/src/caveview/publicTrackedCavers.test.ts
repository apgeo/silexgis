// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PublicTripEnvelope, PublicTripParticipant } from '../api/hooks.ts';
import { envelopeCrsLookup, publicTrackedCavers } from './publicTrackedCavers.ts';

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';

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

function envelope(
  overrides: Partial<Pick<PublicTripEnvelope, 'participants' | 'teams' | 'positionsWithheld'>> = {},
) {
  return {
    participants: [participant()],
    teams: [],
    positionsWithheld: false,
    ...overrides,
  };
}

const unnamed = (ordinal: number) => `Caver ${ordinal}`;

describe('a published trip, folded into people a model can draw', () => {
  it('keys everybody by their place in the party, never by an identity', () => {
    const cavers = publicTrackedCavers(
      envelope({
        participants: [participant({ ordinal: 1 }), participant({ ordinal: 7 })],
      }),
      unnamed,
    );

    expect(cavers.map((caver) => caver.caverId)).toEqual(['1', '7']);
  });

  // Whatever name the envelope carries — a caption an administrator typed, or the roster's own
  // name where the installation publishes names — and a number for a place in the party it carries
  // no name for. The fold does not know which of the two it was handed, and nothing here should
  // make it able to tell.
  it('uses the name the envelope carries, and a number for everybody else', () => {
    const cavers = publicTrackedCavers(
      envelope({
        participants: [
          participant({ ordinal: 1, label: 'Ana' }),
          participant({ ordinal: 2, label: null }),
        ],
      }),
      unnamed,
    );

    expect(cavers.map((caver) => caver.name)).toEqual(['Ana', 'Caver 2']);
  });

  it('names the reader is not the server: the words for an unnamed place come from the caller', () => {
    const cavers = publicTrackedCavers(
      envelope({ participants: [participant({ ordinal: 3 })] }),
      (ordinal) => `Speolog ${ordinal}`,
    );

    expect(cavers[0].name).toBe('Speolog 3');
  });

  it('carries the title of the team somebody was put on, and nothing for a team that is gone', () => {
    const cavers = publicTrackedCavers(
      envelope({
        teams: [{ id: TEAM_A, title: 'Advance' }],
        participants: [
          participant({ ordinal: 1, teamId: TEAM_A }),
          participant({ ordinal: 2, teamId: TEAM_B }),
          participant({ ordinal: 3, teamId: null }),
        ],
      }),
      unnamed,
    );

    expect(cavers.map((caver) => caver.teamTitle)).toEqual(['Advance', null, null]);
  });

  it('places somebody at a station, and says a depth in words rather than placing it', () => {
    const cavers = publicTrackedCavers(
      envelope({
        participants: [
          participant({ ordinal: 1, stationName: 'p.g.7' }),
          participant({ ordinal: 2, depthM: 84 }),
        ],
      }),
      unnamed,
    );

    expect(cavers[0].position).toEqual({ kind: 'station', station: 'p.g.7' });
    expect(cavers[1].position).toEqual({ kind: 'depth', depthM: 84 });
  });

  it('says a place the envelope measured in another survey, never as an absence', () => {
    // The server strips the station and the depth off such a row and sends one bit instead, so
    // this fold has nothing but that bit to tell "known, not shown here" from "nobody reported
    // one" — and a follower is the reader least able to work the difference out for themselves.
    const cavers = publicTrackedCavers(
      envelope({
        participants: [
          participant({
            ordinal: 1,
            lastRecordedAt: '2026-09-12T09:00:00Z',
            positionOnOtherModel: true,
          }),
          // The twin, on the survey this page draws: still placed, still drawn.
          participant({ ordinal: 2, stationName: 'p.g.7', positionOnOtherModel: false }),
          participant({ ordinal: 3, lastRecordedAt: '2026-09-12T09:00:00Z' }),
        ],
      }),
      unnamed,
    );

    expect(cavers[0].position).toEqual({ kind: 'otherModel' });
    expect(cavers[1].position).toEqual({ kind: 'station', station: 'p.g.7' });
    expect(cavers[2].position).toEqual({ kind: 'unreported' });
  });

  it('tells an absence nobody reported apart from one that was withheld', () => {
    // Nobody has said anything about this person at all: there is no position to keep back, so
    // saying one was kept back would invent a secret.
    const unheard = publicTrackedCavers(
      envelope({ positionsWithheld: true, participants: [participant({ lastRecordedAt: null })] }),
      unnamed,
    );
    expect(unheard[0].position).toEqual({ kind: 'unreported' });

    // Reported, no position, and the envelope says something was withheld.
    const withheld = publicTrackedCavers(
      envelope({
        positionsWithheld: true,
        participants: [participant({ lastRecordedAt: '2026-09-14T09:00:00Z' })],
      }),
      unnamed,
    );
    expect(withheld[0].position).toEqual({ kind: 'withheld', certain: false });

    // And with nothing withheld anywhere on the trip, the same row is simply unplaced.
    const plain = publicTrackedCavers(
      envelope({
        positionsWithheld: false,
        participants: [participant({ lastRecordedAt: '2026-09-14T09:00:00Z' })],
      }),
      unnamed,
    );
    expect(plain[0].position).toEqual({ kind: 'unreported' });
  });

  it('never claims a withholding as certain, because this surface cannot know that', () => {
    // The signed-in fold can say "this position exists and you may not be told it" when the last
    // report was one that always carries a place. The published envelope carries no report kind at
    // all, so the strong claim is unreachable here — and must not be reached for.
    const cavers = publicTrackedCavers(
      envelope({
        positionsWithheld: true,
        participants: [
          participant({ ordinal: 1, lastRecordedAt: '2026-09-14T09:00:00Z' }),
          participant({ ordinal: 2, lastRecordedAt: '2026-09-14T09:30:00Z' }),
        ],
      }),
      unnamed,
    );

    for (const caver of cavers) {
      expect(caver.position).toEqual({ kind: 'withheld', certain: false });
    }
  });

  it('dates a followed position from the report that placed somebody', () => {
    // <b>The envelope carries the position's own moment now, and this page reads it.</b> It used
    // to carry only the last word, so every followed position was as old as the last thing anybody
    // said about that person — a station reported at 09:00 under a radio check at 11:00 was drawn
    // as an 11:00 position, on the page a family reads while deciding whether a party is overdue.
    const cavers = publicTrackedCavers(
      envelope({
        participants: [
          participant({
            stationName: 'p.g.7',
            lastRecordedAt: '2026-09-14T11:00:00Z',
            positionRecordedAt: '2026-09-14T09:00:00Z',
          }),
        ],
      }),
      unnamed,
    );

    expect(cavers[0].positionAt).toBe('2026-09-14T09:00:00Z');
    // The twin, on the same row: the two moments stay two. A fold that had started copying one
    // into the other would pass the line above and fail here.
    expect(cavers[0].lastRecordedAt).toBe('2026-09-14T11:00:00Z');
  });

  it('leaves a followed position undated where the envelope dated none', () => {
    // A position nobody reported and one that was kept back arrive identically — with no moment —
    // and neither may borrow the last word's. The row below has a last word an hour old and no
    // position at all: an age drawn from it would say a station was reported when none was.
    const cavers = publicTrackedCavers(
      envelope({
        positionsWithheld: true,
        participants: [participant({ lastRecordedAt: '2026-09-14T11:00:00Z' })],
      }),
      unnamed,
    );

    expect(cavers[0].positionAt).toBeNull();
    expect(cavers[0].position).toEqual({ kind: 'withheld', certain: false });
    // The twin: a row the envelope did date is dated, so the null above is a refusal to invent
    // rather than a fold that never reads the field.
    expect(
      publicTrackedCavers(
        envelope({
          participants: [
            participant({ stationName: 'p.g.7', positionRecordedAt: '2026-09-14T09:00:00Z' }),
          ],
        }),
        unnamed,
      )[0].positionAt,
    ).toBe('2026-09-14T09:00:00Z');
  });

  it('reports no entry time, rather than guessing one', () => {
    const cavers = publicTrackedCavers(
      envelope({ participants: [participant({ in: true, lastRecordedAt: '2026-09-14T09:00:00Z' })] }),
      unnamed,
    );

    expect(cavers[0].enteredAt).toBeNull();
    expect(cavers[0].lastRecordedAt).toBe('2026-09-14T09:00:00Z');
  });
});

describe('the coordinate system a followed page resolves from its own answer', () => {
  const model = { proj4: '+proj=sterea +lat_0=46', sourceEpsg: 31700 };

  it('answers the definition the envelope carried, for the code it describes', async () => {
    const lookup = envelopeCrsLookup(model);

    await expect(lookup('31700')).resolves.toBe(model.proj4);
    // The viewer asks with whatever the survey file wrote, which may name its register.
    await expect(lookup('EPSG:31700')).resolves.toBe(model.proj4);
  });

  it('answers nothing for any other code, rather than the wrong datum', async () => {
    const lookup = envelopeCrsLookup(model);

    await expect(lookup('4326')).resolves.toBeNull();
    await expect(lookup('not-a-code')).resolves.toBeNull();
  });

  it('answers nothing at all when the envelope carried no definition', async () => {
    await expect(envelopeCrsLookup(null)('31700')).resolves.toBeNull();
    await expect(
      envelopeCrsLookup({ proj4: null, sourceEpsg: 31700 })('31700'),
    ).resolves.toBeNull();
    await expect(
      envelopeCrsLookup({ proj4: '+proj=sterea', sourceEpsg: null })('31700'),
    ).resolves.toBeNull();
  });
});
