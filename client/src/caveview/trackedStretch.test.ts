// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { TrackingEvent, TrackingParticipant } from '../api/hooks.ts';
import type { CaveViewTrail } from './loadCaveView.ts';
import type { TrackedCaver } from './trackedCavers.ts';
import {
  STRETCH_TRAIL_PREFIX,
  logsNameAStretch,
  noStretchFaults,
  stretchFaultOf,
  stretchFaults,
  stretchKey,
  syncStretchTrails,
  wantedStretches,
  withStretches,
  withStretchesAt,
  type DrawnStretch,
} from './trackedStretch.ts';

const MODEL = 'model-1';
const COLORS = { underground: '#under', out: '#out' };

function caver(overrides: Partial<TrackedCaver> = {}): TrackedCaver {
  return {
    caverId: 'caver-1',
    name: 'Ana',
    teamId: null,
    teamTitle: null,
    position: { kind: 'station', station: 'cave.a' },
    lastRecordedAt: '2026-09-12T09:00:00Z',
    positionAt: '2026-09-12T09:00:00Z',
    enteredAt: null,
    out: false,
    ...overrides,
  };
}

/** Only what the stretch reads of a participant; the rest of the watch's row is not its business. */
function participant(overrides: Partial<TrackingParticipant> = {}) {
  return {
    caverId: 'caver-1',
    stationName: 'cave.a',
    toStationName: 'cave.b',
    positionSurveyModelId: MODEL,
    ...overrides,
  } as TrackingParticipant;
}

function report(overrides: Partial<TrackingEvent> = {}): TrackingEvent {
  return {
    id: 'event-1',
    caverId: 'caver-1',
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName: 'cave.a',
    toStationName: 'cave.b',
    depthEnteredM: null,
    note: null,
    recordedAt: '2026-09-12T09:00:00Z',
    corrected: false,
    outsideDeclaredParts: false,
    ...overrides,
  } as TrackingEvent;
}

describe('the far end of a stretch on the live watch', () => {
  it('is added to somebody the watch places between two stations of the drawing', () => {
    const [stretched] = withStretches([caver()], { participants: [participant()] }, MODEL);

    expect(stretched.position).toEqual({ kind: 'station', station: 'cave.a', toStation: 'cave.b' });
  });

  it('hands back the very list where nobody stands on a stretch', () => {
    const party = [caver()];

    expect(withStretches(party, { participants: [participant({ toStationName: null })] }, MODEL)).toBe(
      party,
    );
  });

  it('says no far end for a position the fold did not draw at a station', () => {
    // Withheld, on another survey, by depth: in each the watch's row may still be read, and none
    // of them is a marker a line could start from.
    for (const position of [
      { kind: 'withheld', certain: true },
      { kind: 'otherModel' },
      { kind: 'depth', depthM: 40 },
      { kind: 'unreported' },
    ] as const) {
      const party = [caver({ position })];
      expect(withStretches(party, { participants: [participant()] }, MODEL)).toBe(party);
    }
  });

  it('says no far end where the watch placed the person on another survey', () => {
    const party = [caver()];

    expect(
      withStretches(party, { participants: [participant({ positionSurveyModelId: 'model-2' })] }, MODEL),
    ).toBe(party);
    expect(withStretches(party, { participants: [participant()] }, undefined)).toBe(party);
  });

  it('says no far end where the watch names another first station than the one drawn', () => {
    const party = [caver()];

    expect(
      withStretches(party, { participants: [participant({ stationName: 'cave.z' })] }, MODEL),
    ).toBe(party);
  });
});

describe('the far end of a stretch at a replayed moment', () => {
  it('is read off the very report that placed the person', () => {
    const events = [
      report({ id: 'later', recordedAt: '2026-09-12T10:00:00Z', toStationName: 'cave.late' }),
      report({ id: 'placing' }),
      report({ id: 'other-person', caverId: 'caver-2', toStationName: 'cave.other' }),
    ];

    const [stretched] = withStretchesAt([caver()], events, MODEL);

    expect(stretched.position).toEqual({ kind: 'station', station: 'cave.a', toStation: 'cave.b' });
  });

  it('says none for a report at one station, and hands back the very list', () => {
    const party = [caver()];

    expect(withStretchesAt(party, [report({ toStationName: null })], MODEL)).toBe(party);
  });

  it('says none where the report at that moment was made on another survey', () => {
    const party = [caver()];

    expect(withStretchesAt(party, [report({ surveyModelId: 'model-2' })], MODEL)).toBe(party);
  });

  it('says none where two reports of one moment disagree about the far end', () => {
    const party = [caver()];

    expect(
      withStretchesAt(
        party,
        [report({ id: 'one' }), report({ id: 'two', toStationName: 'cave.c' })],
        MODEL,
      ),
    ).toBe(party);
    expect(
      withStretchesAt(party, [report({ id: 'one', toStationName: null }), report({ id: 'two' })], MODEL),
    ).toBe(party);
  });

  it('leaves alone somebody the fold did not place at a station', () => {
    const party = [caver({ position: { kind: 'withheld', certain: true } })];

    expect(withStretchesAt(party, [report()], MODEL)).toBe(party);
  });
});

describe('the stretch lines a party asks for', () => {
  const onStretch = (overrides: Partial<TrackedCaver> = {}) =>
    caver({ position: { kind: 'station', station: 'cave.a', toStation: 'cave.b' }, ...overrides });

  it('is one line per stretch however many people stand on it', () => {
    const wanted = wantedStretches(
      [onStretch(), onStretch({ caverId: 'caver-2' }), caver({ caverId: 'caver-3' })],
      COLORS,
      new Set(),
    );

    expect([...wanted]).toEqual([
      [stretchKey('cave.a', 'cave.b'), { from: 'cave.a', to: 'cave.b', color: '#under' }],
    ]);
  });

  it('is muted only where everybody on the stretch has come out', () => {
    const key = stretchKey('cave.a', 'cave.b');

    expect(wantedStretches([onStretch({ out: true })], COLORS, new Set()).get(key)?.color).toBe('#out');
    expect(
      wantedStretches([onStretch({ out: true }), onStretch({ caverId: 'caver-2' })], COLORS, new Set())
        .get(key)?.color,
    ).toBe('#under');
  });

  it('asks for no line from a first station the drawing does not hold', () => {
    expect(wantedStretches([onStretch()], COLORS, new Set(['cave.a'])).size).toBe(0);
  });

  it('keeps two stations apart whatever characters their names hold', () => {
    expect(stretchKey('a', 'b.c')).not.toBe(stretchKey('a.b', 'c'));
    expect(stretchKey('a","b', 'c')).not.toBe(stretchKey('a', 'b","c'));
  });
});

describe('bringing the stretch lines on the model into line with the party', () => {
  const viewer = () => ({ addTrail: vi.fn(), updateTrail: vi.fn(), removeTrail: vi.fn() });
  const key = stretchKey('cave.a', 'cave.b');
  const line: DrawnStretch = { from: 'cave.a', to: 'cave.b', color: '#under' };

  it('routes a dashed line from the first station to the far one', () => {
    const fake = viewer();

    const drawn = syncStretchTrails(fake, new Map(), new Map([[key, line]]));

    expect(fake.addTrail).toHaveBeenCalledExactlyOnceWith(
      STRETCH_TRAIL_PREFIX + key,
      ['cave.a', 'cave.b'],
      { color: '#under', style: 'dashed' },
    );
    expect(drawn.get(key)).toEqual(line);
  });

  it('touches nothing when nothing changed', () => {
    const fake = viewer();

    syncStretchTrails(fake, new Map([[key, line]]), new Map([[key, line]]));

    expect(fake.addTrail).not.toHaveBeenCalled();
    expect(fake.updateTrail).not.toHaveBeenCalled();
    expect(fake.removeTrail).not.toHaveBeenCalled();
  });

  it('recolours a line without routing it again', () => {
    const fake = viewer();

    syncStretchTrails(fake, new Map([[key, line]]), new Map([[key, { ...line, color: '#out' }]]));

    expect(fake.updateTrail).toHaveBeenCalledExactlyOnceWith(STRETCH_TRAIL_PREFIX + key, null, {
      color: '#out',
    });
  });

  it('takes the line off once nobody stands on the stretch', () => {
    const fake = viewer();

    const drawn = syncStretchTrails(fake, new Map([[key, line]]), new Map());

    expect(fake.removeTrail).toHaveBeenCalledExactlyOnceWith(STRETCH_TRAIL_PREFIX + key);
    expect(drawn.size).toBe(0);
  });
});

describe('what the viewer says became of a stretch line', () => {
  const key = stretchKey('cave.a', 'cave.b');
  const trail = (overrides: Partial<CaveViewTrail> = {}) => ({
    id: STRETCH_TRAIL_PREFIX + key,
    resolved: true,
    points: [
      { ref: 'cave.a', resolved: true, atLength: 0 },
      { ref: 'cave.b', resolved: true, atLength: 12 },
    ],
    gaps: [] as unknown[],
    ...overrides,
  });

  it('learns nothing from a line that was routed, and answers the very map', () => {
    expect(stretchFaults(noStretchFaults, [trail()])).toBe(noStretchFaults);
  });

  it('learns that the drawing does not hold the far station', () => {
    const faults = stretchFaults(noStretchFaults, [
      trail({
        resolved: false,
        points: [
          { ref: 'cave.a', resolved: true, atLength: 0 },
          { ref: 'cave.b', resolved: false, atLength: null },
        ],
      }),
    ]);

    expect(faults.get(key)).toBe('toNotOnModel');
  });

  it('learns that the survey does not join the two stations', () => {
    expect(stretchFaults(noStretchFaults, [trail({ gaps: [{}] })]).get(key)).toBe('unroutable');
  });

  it('records nothing of a stretch whose first station is the one missing', () => {
    expect(
      stretchFaults(noStretchFaults, [
        trail({
          resolved: false,
          points: [
            { ref: 'cave.a', resolved: false, atLength: null },
            { ref: 'cave.b', resolved: true, atLength: null },
          ],
        }),
      ]),
    ).toBe(noStretchFaults);
  });

  it('reads no trail that is not a stretch line, such as a route an export draws', () => {
    expect(stretchFaults(noStretchFaults, [trail({ id: 'movie:caver-1', gaps: [{}] })])).toBe(
      noStretchFaults,
    );
  });

  it('keeps what it learned after the line is gone', () => {
    const known = stretchFaults(noStretchFaults, [trail({ gaps: [{}] })]);

    expect(stretchFaults(known, [])).toBe(known);
    expect(stretchFaults(known, [trail()])).toBe(known);
  });

  it('is asked about a person through the stretch they stand on', () => {
    const known = stretchFaults(noStretchFaults, [trail({ gaps: [{}] })]);

    expect(
      stretchFaultOf(known, caver({ position: { kind: 'station', station: 'cave.a', toStation: 'cave.b' } })),
    ).toBe('unroutable');
    expect(stretchFaultOf(known, caver())).toBeNull();
    expect(
      stretchFaultOf(known, caver({ position: { kind: 'station', station: 'cave.b', toStation: 'cave.a' } })),
    ).toBeNull();
  });
});

describe('whether any log names a stretch', () => {
  it('is true only where some report carries a far end', () => {
    expect(logsNameAStretch([])).toBe(false);
    expect(logsNameAStretch([[report({ toStationName: null })], []])).toBe(false);
    expect(logsNameAStretch([[report({ toStationName: null })], [report()]])).toBe(true);
  });
});
