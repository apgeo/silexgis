// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PublicPastTrack, PublicPastTrackFix } from '../../api/hooks.ts';
import { publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import {
  firstPlacedMoment,
  followedName,
  followedStation,
  momentAfter,
  momentBefore,
  pastEnvelopeAt,
  pastReplayWindow,
  pastReportMoments,
} from './pastTrackReplay.ts';

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';

const at = (iso: string) => Date.parse(iso);

function fix(overrides: Partial<PublicPastTrackFix> = {}): PublicPastTrackFix {
  return {
    recordedAt: '2019-07-06T09:00:00Z',
    teamId: null,
    stationName: null,
    depthM: null,
    positionOnOtherModel: false,
    in: false,
    out: false,
    ...overrides,
  };
}

function track(overrides: Partial<PublicPastTrack> = {}): PublicPastTrack {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
    title: 'Peștera Demo Mare, the 2019 push',
    tripDate: '2019-07-06',
    tripDateEnd: null,
    armedAt: '2019-07-06T08:00:00Z',
    closedAt: '2019-07-06T18:00:00Z',
    positionsWithheld: false,
    trackTruncated: false,
    model: null,
    teams: [
      { id: TEAM_A, title: 'Advance' },
      { id: TEAM_B, title: 'Survey' },
    ],
    participants: [],
    ...overrides,
  };
}

/** The party at a moment, as the drawings actually receive it. */
const partyAt = (source: PublicPastTrack, moment: number) =>
  publicTrackedCavers(pastEnvelopeAt(source, moment), (ordinal) => `Caver ${ordinal}`);

describe('winding a published past trip back to a moment', () => {
  it('folds it into the very envelope the live page draws, so nothing downstream knows the difference', () => {
    const folded = pastEnvelopeAt(
      track({
        participants: [
          {
            ordinal: 1,
            label: 'Ana',
            track: [
              fix({ recordedAt: '2019-07-06T09:00:00Z', in: true, teamId: TEAM_A }),
              fix({ recordedAt: '2019-07-06T10:00:00Z', stationName: 'p.g.7', in: true, teamId: TEAM_A }),
            ],
          },
        ],
      }),
      at('2019-07-06T11:00:00Z'),
    );

    expect(folded.state).toBe('closed');
    expect(folded.title).toBe('Peștera Demo Mare, the 2019 push');
    expect(folded.participants).toEqual([
      {
        ordinal: 1,
        label: 'Ana',
        teamId: TEAM_A,
        stationName: 'p.g.7',
        depthM: null,
        lastRecordedAt: '2019-07-06T10:00:00Z',
        positionRecordedAt: '2019-07-06T10:00:00Z',
        positionOnOtherModel: false,
        in: true,
        out: false,
      },
    ]);
  });

  it('never says a past trip is armed, whatever the trip did', () => {
    // The one value that would make the page poll a finished trip forever and print "underground
    // now" over a party who came out years ago.
    expect(pastEnvelopeAt(track({ closedAt: null }), at('2019-07-06T12:00:00Z')).state).toBe('closed');
  });

  it('shows nobody where nothing had been reported yet, rather than where they ended up', () => {
    const source = track({
      participants: [
        { ordinal: 1, label: 'Ana', track: [fix({ recordedAt: '2019-07-06T14:00:00Z', stationName: 'p.g.7', in: true })] },
      ],
    });

    // The positive twin of the absence below: an hour later the very same report is drawn.
    expect(partyAt(source, at('2019-07-06T15:00:00Z'))[0].position).toEqual({
      kind: 'station',
      station: 'p.g.7',
    });
    // And at a moment before the report, the person is listed and unplaced — never absent, and
    // never standing at a station nothing had yet said they reached.
    const early = partyAt(source, at('2019-07-06T13:00:00Z'));
    expect(early).toHaveLength(1);
    expect(early[0].position).toEqual({ kind: 'unreported' });
    expect(early[0].lastRecordedAt).toBeNull();
  });

  it('leaves the last place standing through reports that claim none', () => {
    const source = track({
      participants: [
        {
          ordinal: 1,
          label: 'Ana',
          track: [
            fix({ recordedAt: '2019-07-06T10:00:00Z', stationName: 'p.g.7', in: true }),
            // Coming out claims no place: her marker stays where she was last seen, marked out,
            // because taking it off the drawing would read as a caver who vanished.
            fix({ recordedAt: '2019-07-06T17:00:00Z', in: false, out: true }),
          ],
        },
      ],
    });

    const later = partyAt(source, at('2019-07-06T17:30:00Z'))[0];
    expect(later.position).toEqual({ kind: 'station', station: 'p.g.7' });
    expect(later.out).toBe(true);
    // The position keeps the moment of the report that made it, not of the report that came out.
    expect(later.positionAt).toBe('2019-07-06T10:00:00Z');
    expect(later.lastRecordedAt).toBe('2019-07-06T17:00:00Z');
  });

  it('says a place measured on another survey is a place, and never an absence', () => {
    const source = track({
      participants: [
        {
          ordinal: 1,
          label: 'Ana',
          track: [fix({ recordedAt: '2019-07-06T10:00:00Z', positionOnOtherModel: true, in: true })],
        },
      ],
    });

    // Not `unreported`: somebody reported where she was, and the drawing simply cannot show it.
    expect(partyAt(source, at('2019-07-06T11:00:00Z'))[0].position).toEqual({ kind: 'otherModel' });
  });

  it('says a withholding no more strongly than the response allows', () => {
    const withheld = track({
      positionsWithheld: true,
      participants: [
        { ordinal: 1, label: 'Ana', track: [fix({ recordedAt: '2019-07-06T10:00:00Z', in: true })] },
      ],
    });

    // The weaker claim, because the response carries no report kind: a row with no place is either
    // a withheld position or somebody going in, and guessing would tell a stranger that something
    // is being kept from them when a party had merely set off.
    expect(partyAt(withheld, at('2019-07-06T11:00:00Z'))[0].position).toEqual({
      kind: 'withheld',
      certain: false,
    });
    // The twin: the same row on a trip withholding nothing is an ordinary early-trip silence.
    const open = track({
      participants: [
        { ordinal: 1, label: 'Ana', track: [fix({ recordedAt: '2019-07-06T10:00:00Z', in: true })] },
      ],
    });
    expect(partyAt(open, at('2019-07-06T11:00:00Z'))[0].position).toEqual({ kind: 'unreported' });
  });

  it('draws somebody in the team they were in at the moment, not the one they ended in', () => {
    const source = track({
      participants: [
        {
          ordinal: 1,
          label: 'Ana',
          track: [
            fix({ recordedAt: '2019-07-06T09:00:00Z', teamId: TEAM_A, in: true }),
            fix({ recordedAt: '2019-07-06T13:00:00Z', teamId: TEAM_B, in: true }),
          ],
        },
      ],
    });

    expect(partyAt(source, at('2019-07-06T10:00:00Z'))[0].teamTitle).toBe('Advance');
    expect(partyAt(source, at('2019-07-06T14:00:00Z'))[0].teamTitle).toBe('Survey');
  });

  it('leaves a fix whose instant will not parse out of the fold entirely', () => {
    const source = track({
      participants: [
        {
          ordinal: 1,
          label: 'Ana',
          track: [
            fix({ recordedAt: 'not a time', stationName: 'nowhere.1', in: true }),
            fix({ recordedAt: '2019-07-06T10:00:00Z', stationName: 'p.g.7', in: true }),
          ],
        },
      ],
    });

    // There is no moment to place it at, so placing it anywhere would move somebody at a time
    // nothing says they moved.
    expect(partyAt(source, at('2019-07-06T11:00:00Z'))[0].position).toEqual({
      kind: 'station',
      station: 'p.g.7',
    });
  });

  it('follows somebody who came out and went back in', () => {
    const source = track({
      participants: [
        {
          ordinal: 1,
          label: 'Ana',
          track: [
            fix({ recordedAt: '2019-07-06T09:00:00Z', in: true }),
            fix({ recordedAt: '2019-07-06T12:00:00Z', out: true }),
            fix({ recordedAt: '2019-07-06T13:00:00Z', in: true }),
          ],
        },
      ],
    });

    // The standing is the server's, folded over the whole prefix, so the replay reads it off the
    // last report at or before the moment rather than folding it a second time.
    expect(partyAt(source, at('2019-07-06T11:00:00Z'))[0].out).toBe(false);
    expect(partyAt(source, at('2019-07-06T12:30:00Z'))[0].out).toBe(true);
    expect(partyAt(source, at('2019-07-06T14:00:00Z'))[0].out).toBe(false);
  });
});

describe('the stretch a past trip is played over', () => {
  it('runs from the armed instant to the closed one', () => {
    expect(pastReplayWindow(track())).toEqual({
      from: at('2019-07-06T08:00:00Z'),
      to: at('2019-07-06T18:00:00Z'),
    });
  });

  it('widens to hold a report made outside the watch, so the handle can reach it', () => {
    const widened = pastReplayWindow(
      track({
        participants: [
          { ordinal: 1, label: null, track: [fix({ recordedAt: '2019-07-06T19:30:00Z' })] },
        ],
      }),
    );
    expect(widened?.to).toBe(at('2019-07-06T19:30:00Z'));
  });

  it('ends an unclosed watch at its last report rather than at today', () => {
    // A watch switched off rather than closed carries no closing instant. Running the rail to the
    // present would make every report of a trip from 2019 land on the left-hand end of it.
    const window = pastReplayWindow(
      track({
        closedAt: null,
        participants: [
          { ordinal: 1, label: null, track: [fix({ recordedAt: '2019-07-06T11:00:00Z' })] },
        ],
      }),
    );
    expect(window).toEqual({ from: at('2019-07-06T08:00:00Z'), to: at('2019-07-06T11:00:00Z') });
  });

  it('has nothing to play for a watch that was never armed', () => {
    expect(pastReplayWindow(track({ armedAt: null }))).toBeNull();
  });

  it('ends a record the response could not carry whole where its reports end', () => {
    // A log longer than one reading of it holds arrives cut, oldest first, and says so. Ending the
    // rail at the trip's own close would hand the reader six hours of playback over which nothing
    // is reported and every caver stays pinned where the last delivered report left them, standing
    // underground and growing hours older — a party who stopped being heard from, which is exactly
    // the reading the flag exists to refuse.
    const reports = [
      { ordinal: 1, label: null, track: [fix({ recordedAt: '2019-07-06T11:00:00Z', in: true })] },
    ];
    const cut = pastReplayWindow(track({ trackTruncated: true, participants: reports }));
    expect(cut).toEqual({ from: at('2019-07-06T08:00:00Z'), to: at('2019-07-06T11:00:00Z') });

    // The twin: the same reports, whole, run to the close the trip actually has.
    const whole = pastReplayWindow(track({ trackTruncated: false, participants: reports }));
    expect(whole).toEqual({ from: at('2019-07-06T08:00:00Z'), to: at('2019-07-06T18:00:00Z') });
  });
});

describe('stepping between the reports themselves', () => {
  const source = track({
    participants: [
      {
        ordinal: 1,
        label: 'Ana',
        track: [fix({ recordedAt: '2019-07-06T09:00:00Z' }), fix({ recordedAt: '2019-07-06T11:00:00Z' })],
      },
      // The same instant reported for two people is one moment on the rail, not two.
      { ordinal: 2, label: 'Bogdan', track: [fix({ recordedAt: '2019-07-06T09:00:00Z' })] },
    ],
  });

  it('lists every distinct moment once, oldest first', () => {
    expect(pastReportMoments(source)).toEqual([
      at('2019-07-06T09:00:00Z'),
      at('2019-07-06T11:00:00Z'),
    ]);
  });

  it('steps strictly, so a handle resting on a report does not stay on it', () => {
    const moments = pastReportMoments(source);
    expect(momentAfter(moments, at('2019-07-06T09:00:00Z'))).toBe(at('2019-07-06T11:00:00Z'));
    expect(momentBefore(moments, at('2019-07-06T09:00:00Z'))).toBeNull();
    expect(momentAfter(moments, at('2019-07-06T11:00:00Z'))).toBeNull();
  });
});

describe('keeping the camera on a team or a caver', () => {
  const source = track({
    participants: [
      {
        ordinal: 1,
        label: 'Ana',
        track: [fix({ recordedAt: '2019-07-06T10:00:00Z', stationName: 'p.g.7', teamId: TEAM_A, in: true })],
      },
      {
        ordinal: 2,
        label: 'Bogdan',
        track: [fix({ recordedAt: '2019-07-06T10:30:00Z', stationName: 'p.g.9', teamId: TEAM_A, in: true })],
      },
      { ordinal: 3, label: 'Carmen', track: [fix({ recordedAt: '2019-07-06T10:00:00Z', teamId: TEAM_B, in: true })] },
    ],
  });
  const cavers = partyAt(source, at('2019-07-06T12:00:00Z'));
  const names = { unteamed: 'Not in a team', unnamed: (ordinal: number) => `Caver ${ordinal}` };

  it('follows one person to the station their own report named', () => {
    expect(followedStation(cavers, { kind: 'caver', id: '1' })).toBe('p.g.7');
  });

  it('follows a team to whoever of it was placed most recently', () => {
    expect(followedStation(cavers, { kind: 'team', id: TEAM_A })).toBe('p.g.9');
  });

  it('points nowhere rather than guessing, for a team nobody placed', () => {
    // Carmen was reported underground and never placed. There is no station that would be true,
    // and a camera flown at one anyway would present a guess with the confidence of a measurement.
    expect(followedStation(cavers, { kind: 'team', id: TEAM_B })).toBeNull();
  });

  it('names nobody for a follow this trip does not hold', () => {
    // A link in somebody's article can name a team of another trip, or a place in the party beyond
    // the end of this roster.
    expect(followedName(source, { kind: 'team', id: 'not-a-team-of-this-trip' }, names)).toBeNull();
    expect(followedName(source, { kind: 'caver', id: '99' }, names)).toBeNull();
    expect(followedStation(cavers, { kind: 'caver', id: '99' })).toBeNull();
  });

  it('names a team and a person from the trip’s own roster, not from the moment', () => {
    expect(followedName(source, { kind: 'team', id: TEAM_A }, names)).toBe('Advance');
    expect(followedName(source, { kind: 'caver', id: '2' }, names)).toBe('Bogdan');

    // The reason it is the roster: at the armed instant nobody has been reported into any team, and
    // a name folded from the moment would be absent for the first part of every trip — telling a
    // reader nothing was being followed at the moment they pressed a link asking for it.
    const opening = partyAt(source, at('2019-07-06T08:00:00Z'));
    expect(opening.every((caver) => caver.teamId === null)).toBe(true);
    expect(followedName(source, { kind: 'team', id: TEAM_A }, names)).toBe('Advance');
  });

  it('opens a follow where the followed party first has somewhere to be drawn', () => {
    // Opened at the trip's own start, "show me the survey team" is an empty rail the reader has to
    // think to drag; the start is still one drag away.
    expect(firstPlacedMoment(source, { kind: 'team', id: TEAM_A })).toBe(at('2019-07-06T10:00:00Z'));
    expect(firstPlacedMoment(source, { kind: 'caver', id: '2' })).toBe(at('2019-07-06T10:30:00Z'));
  });

  it('opens where the trip does when the followed party is never placed at all', () => {
    // Carmen was reported underground and never placed. There is no moment that would be better
    // than the beginning, and inventing one would be inventing a report.
    expect(firstPlacedMoment(source, { kind: 'team', id: TEAM_B })).toBeNull();
    expect(firstPlacedMoment(source, null)).toBeNull();
  });
});
