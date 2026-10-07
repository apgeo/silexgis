// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { DEFAULT_MOVIE_SETTINGS, type MovieSettings } from './movieSettings.ts';
import {
  MOVIE_MARKER_PALETTE,
  movieMarkerId,
  movieParty,
  type MovieTripData,
} from './movieParty.ts';
import { buildMovieTimeline } from './movieTimeline.ts';

const MODEL = 'model-1';
const OTHER_MODEL = 'model-2';
const ANA = 'caver-ana';
const BOGDAN = 'caver-bogdan';
const CORA = 'caver-cora';
const LATE = Date.parse('2026-09-12T12:00:00Z');

const NAMES: Record<string, string> = { [ANA]: 'Ana Popescu', [BOGDAN]: 'Bogdan Ionescu', [CORA]: 'Cora-Maria Dan' };

function participant(caverId: string): TrackingState['participants'][number] {
  return {
    caverId,
    teamId: null,
    lastKind: null,
    lastRecordedAt: null,
    positionRecordedAt: null,
    stationName: null,
    depthM: null,
    positionSurveyModelId: null,
    label: null,
    in: false,
    out: false,
    publishedAs: null,
  } as TrackingState['participants'][number];
}

function state(caverIds: string[], teams: TrackingState['teams'] = [], positionsWithheld = false): TrackingState {
  return {
    state: 'closed',
    surveyModelId: MODEL,
    surveyModelMissing: false,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T08:00:00Z',
    closedAt: '2026-09-12T12:00:00Z',
    positionsWithheld,
    publishesRealNames: true,
    publishedAt: null,
    publishedUntil: null,
    teams,
    participants: caverIds.map(participant),
  };
}

let sequence = 0;
function event(overrides: Partial<TrackingEvent> & { recordedAt: string }): TrackingEvent {
  return {
    id: `event-${sequence++}`,
    caverId: ANA,
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName: null,
    depthEnteredM: null,
    note: null,
    ...overrides,
  } as TrackingEvent;
}

const newestFirst = (events: TrackingEvent[]) =>
  [...events].sort((left, right) => Date.parse(right.recordedAt) - Date.parse(left.recordedAt));

function trip(tripLogId: string, title: string, tracking: TrackingState, events: TrackingEvent[]): MovieTripData {
  return { tripLogId, title, tracking, events: newestFirst(events), nameOf: (id) => NAMES[id] ?? '?' };
}

function settings(cavers: Partial<MovieSettings['cavers']> = {}, captions: Partial<MovieSettings['captions']> = {}): MovieSettings {
  return {
    ...DEFAULT_MOVIE_SETTINGS,
    cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, ...cavers },
    captions: { ...DEFAULT_MOVIE_SETTINGS.captions, ...captions },
  };
}

const partyOf = (trips: MovieTripData[], s: MovieSettings = settings(), instants = trips.map(() => LATE)) =>
  movieParty(trips, instants, MODEL, { settings: s, t: i18n.t, language: 'en', today: 'never' });

describe('movieParty', () => {
  it('draws only cavers placed at a station of this model', () => {
    const one = trip('trip-1', 'Trip one', state([ANA, BOGDAN, CORA, 'caver-dan'], [], true), [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      // Withheld: a station report with its place removed.
      event({ caverId: BOGDAN, stationName: null, surveyModelId: null, recordedAt: '2026-09-12T09:00:00Z' }),
      // Measured in another survey.
      event({ caverId: CORA, stationName: 'x.1', surveyModelId: OTHER_MODEL, recordedAt: '2026-09-12T09:00:00Z' }),
      // The fourth caver has reported nothing.
    ]);
    const party = partyOf([one]);
    expect([...party.markers.keys()]).toEqual([movieMarkerId('trip-1', ANA)]);
    expect(party.markers.get('trip-1:caver-ana')).toEqual({
      station: 'p.1',
      label: 'Ana',
      color: MOVIE_MARKER_PALETTE[0],
    });
  });

  it('draws nobody before their first report', () => {
    const one = trip('trip-1', 'Trip one', state([ANA]), [
      event({ stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ]);
    expect(partyOf([one], settings(), [Date.parse('2026-09-12T08:30:00Z')]).markers.size).toBe(0);
  });

  it('keeps the same caver on two trips as two markers', () => {
    const log = [event({ stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' })];
    const party = partyOf([trip('trip-1', 'One', state([ANA]), log), trip('trip-2', 'Two', state([ANA]), log)]);
    expect([...party.markers.keys()]).toEqual(['trip-1:caver-ana', 'trip-2:caver-ana']);
    // Several trips colour by trip, each its own colour.
    expect(party.markers.get('trip-1:caver-ana')!.color).toBe(MOVIE_MARKER_PALETTE[0]);
    expect(party.markers.get('trip-2:caver-ana')!.color).toBe(MOVIE_MARKER_PALETTE[1]);
    expect(party.legend).toEqual([
      { color: MOVIE_MARKER_PALETTE[0], label: 'One' },
      { color: MOVIE_MARKER_PALETTE[1], label: 'Two' },
    ]);
  });

  it('keeps every earlier trip’s colour when another is added', () => {
    const log = [event({ stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' })];
    const one = trip('trip-1', 'One', state([ANA]), log);
    const two = trip('trip-2', 'Two', state([ANA]), log);
    const three = trip('trip-3', 'Three', state([ANA]), log);
    const before = partyOf([one, two]);
    const after = partyOf([one, two, three]);
    for (const id of before.markers.keys()) {
      expect(after.markers.get(id)!.color).toBe(before.markers.get(id)!.color);
    }
  });

  it('colours by team on a single trip, and keeps team colours as the replay plays', () => {
    const teams = [{ id: 'team-1', title: 'Echipa 1' }, { id: 'team-2', title: 'Echipa 2' }];
    const one = trip('trip-1', 'One', state([ANA, BOGDAN, CORA], teams), [
      event({ caverId: ANA, teamId: 'team-2', stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: BOGDAN, teamId: 'team-1', stationName: 'p.2', recordedAt: '2026-09-12T10:00:00Z' }),
      event({ caverId: CORA, stationName: 'p.3', recordedAt: '2026-09-12T10:00:00Z' }),
    ]);
    const early = partyOf([one], settings(), [Date.parse('2026-09-12T09:30:00Z')]);
    const late = partyOf([one]);
    expect(early.markers.get('trip-1:caver-ana')!.color).toBe(late.markers.get('trip-1:caver-ana')!.color);
    expect(late.markers.get('trip-1:caver-ana')!.color).not.toBe(late.markers.get('trip-1:caver-bogdan')!.color);
    expect(late.markers.get('trip-1:caver-cora')!.color).toBe(MOVIE_MARKER_PALETTE[0]);
    expect(late.legend.map((entry) => entry.label)).toEqual(['Echipa 1', 'Echipa 2', 'No team']);
  });

  it('tells a dozen trips apart, and says in the legend when there are more trips than colours', () => {
    const log = [event({ stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' })];
    const trips = (count: number) =>
      Array.from({ length: count }, (_, index) => trip(`trip-${index + 1}`, `Trip ${index + 1}`, state([ANA]), log));

    const dozen = partyOf(trips(12));
    expect(new Set([...dozen.markers.values()].map((marker) => marker.color)).size).toBe(12);
    expect(dozen.legend).toHaveLength(12);
    expect(dozen.legend.every((entry) => entry.color !== null && entry.pinned !== true)).toBe(true);

    const thirteen = partyOf(trips(13));
    // The thirteenth is drawn in the first colour again: there is no thirteenth to give it.
    expect(thirteen.markers.get('trip-13:caver-ana')!.color).toBe(thirteen.markers.get('trip-1:caver-ana')!.color);
    // Said by a line of its own with no swatch, kept when the legend is short of room.
    expect(thirteen.legend.at(-1)).toEqual({ color: null, label: 'Trip colours repeat', pinned: true });
    // Colouring that does not go by trip repeats nothing, so it says nothing.
    expect(partyOf(trips(13), settings({ colourBy: 'single' })).legend).toEqual([]);
    expect(partyOf(trips(13), settings({ colourBy: 'team' })).legend.some((entry) => entry.color === null)).toBe(false);
  });

  it('says when there are more teams than colours, on every frame and not only once the last team walks in', () => {
    const teams = (count: number) =>
      Array.from({ length: count }, (_, index) => ({ id: `team-${index + 1}`, title: `Echipa ${index + 1}` }));
    const log = [event({ caverId: ANA, teamId: 'team-1', stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' })];

    // The first colour is kept for people in no team, so eleven teams are told apart.
    const eleven = partyOf([trip('trip-1', 'One', state([ANA], teams(11)), log)]);
    expect(new Set(eleven.legend.map((entry) => entry.color)).size).toBe(11);
    expect(eleven.legend.some((entry) => entry.color === null)).toBe(false);

    const twelve = partyOf([trip('trip-1', 'One', state([ANA], teams(12)), log)]);
    expect(twelve.legend.at(-1)).toEqual({ color: null, label: 'Team colours repeat', pinned: true });
    expect(twelve.legend[11].color).toBe(twelve.legend[0].color);
    // Before anybody of the twelfth team has reported, the line is already there.
    const early = partyOf([trip('trip-1', 'One', state([ANA], teams(12)), log)], settings(), [
      Date.parse('2026-09-12T08:30:00Z'),
    ]);
    expect(early.legend.at(-1)).toEqual({ color: null, label: 'Team colours repeat', pinned: true });
  });

  it('draws everybody in one colour when asked, with no legend of trips', () => {
    const log = [event({ stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' })];
    const party = partyOf(
      [trip('trip-1', 'One', state([ANA]), log), trip('trip-2', 'Two', state([ANA]), log)],
      settings({ colourBy: 'single' }),
    );
    expect(new Set([...party.markers.values()].map((marker) => marker.color))).toEqual(new Set([MOVIE_MARKER_PALETTE[0]]));
    expect(party.legend).toEqual([]);
  });

  it('draws whoever is out in the muted colour, and not at all when asked', () => {
    const one = trip('trip-1', 'One', state([ANA, BOGDAN]), [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: ANA, kind: 'exited', surveyModelId: null, recordedAt: '2026-09-12T10:00:00Z' }),
      event({ caverId: BOGDAN, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ]);
    const shown = partyOf([one]);
    expect(shown.markers.get('trip-1:caver-ana')).toEqual({
      station: 'p.1',
      label: 'Ana (out)',
      color: trackedCaverPalette.out,
    });
    // Pinned: it explains a kind of marker, so a legend short of room keeps it and cuts trips first.
    expect(shown.legend).toContainEqual({ color: trackedCaverPalette.out, label: 'Out', pinned: true });
    expect(shown.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-bogdan'])).toEqual(['Bogdan', 'Ana (out)']);

    const hidden = partyOf([one], settings({ showOut: false }));
    expect([...hidden.markers.keys()]).toEqual(['trip-1:caver-bogdan']);
    expect(hidden.legend.some((entry) => entry.color === trackedCaverPalette.out)).toBe(false);
  });

  it('labels by initials derived from the same name, or not at all', () => {
    const one = trip('trip-1', 'One', state([ANA, CORA]), [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: CORA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ]);
    const initials = partyOf([one], settings({ labels: 'initials' }));
    expect(initials.markers.get('trip-1:caver-ana')!.label).toBe('AP');
    expect(initials.markers.get('trip-1:caver-cora')!.label).toBe('CMD');
    expect(initials.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-cora'])).toEqual(['AP', 'CMD']);

    const off = partyOf([one], settings({ labels: 'off' }));
    expect(off.markers.get('trip-1:caver-ana')!.label).toBe('');
    expect(off.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-cora'])).toBeNull();
  });

  it('names people by first name, lengthened where two people of any selected trip would read the same', () => {
    const log = [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: BOGDAN, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ];
    const one = trip('trip-1', 'One', state([ANA, BOGDAN]), log);
    // A second trip brings another Ana, who is not on the model at this instant — the names are
    // settled over the rosters, so hers still lengthens the first Ana's.
    const two: MovieTripData = {
      ...trip('trip-2', 'Two', state(['caver-ana-dima']), []),
      nameOf: () => 'Ana Dima',
    };
    const alone = partyOf([one]);
    expect(alone.markers.get('trip-1:caver-ana')!.label).toBe('Ana');
    expect(alone.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-bogdan'])).toEqual(['Ana', 'Bogdan']);

    const together = partyOf([one, two]);
    expect(together.markers.has('trip-2:caver-ana-dima')).toBe(false);
    expect(together.markers.get('trip-1:caver-ana')!.label).toBe('Ana P.');
    expect(together.markers.get('trip-1:caver-bogdan')!.label).toBe('Bogdan');
    expect(together.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-bogdan'])).toEqual(['Ana P.', 'Bogdan']);

    const full = partyOf([one], settings({ labels: 'full' }));
    expect(full.markers.get('trip-1:caver-ana')!.label).toBe('Ana Popescu');
  });

  it('keeps the out suffix and the time on a first-name label', () => {
    const one = trip('trip-1', 'One', state([ANA]), [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: ANA, kind: 'exited', surveyModelId: null, recordedAt: '2026-09-12T10:00:00Z' }),
    ]);
    const label = partyOf([one], settings({ showTimes: true })).markers.get('trip-1:caver-ana')!.label as string;
    expect(label.startsWith('Ana ')).toBe(true);
    expect(label).not.toContain('Popescu');
    expect(label.endsWith('(out)')).toBe(true);
    expect(label.length).toBeGreaterThan('Ana (out)'.length);
  });

  it('heads a cluster with a team title only when everybody in it is one team of one trip', () => {
    const teams = [{ id: 'team-1', title: 'Echipa 1' }];
    const log = [
      event({ caverId: ANA, teamId: 'team-1', stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: BOGDAN, teamId: 'team-1', stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ];
    const one = trip('trip-1', 'One', state([ANA, BOGDAN], teams), log);
    // Another trip whose team happens to carry the same title is still another team.
    const two = trip('trip-2', 'Two', state([ANA], [{ id: 'team-9', title: 'Echipa 1' }]), [
      event({ caverId: ANA, teamId: 'team-9', stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ]);
    const party = partyOf([one, two]);
    expect(party.clusterLabel(['trip-1:caver-ana', 'trip-1:caver-bogdan'])).toEqual(['Echipa 1', 'Ana', 'Bogdan']);
    // The same person on two trips is one name, not two people who happen to share one.
    expect(party.clusterLabel(['trip-1:caver-ana', 'trip-2:caver-ana'])).toEqual(['Ana', 'Ana']);
    expect(party.clusterLabel(['nobody'])).toBeNull();
  });

  it('draws a trail of two or more stations behind a marker, in its colour', () => {
    const one = trip('trip-1', 'One', state([ANA, BOGDAN]), [
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ caverId: ANA, stationName: 'p.1', recordedAt: '2026-09-12T09:10:00Z' }),
      event({ caverId: ANA, stationName: 'p.2', recordedAt: '2026-09-12T09:20:00Z' }),
      event({ caverId: BOGDAN, stationName: 'p.1', recordedAt: '2026-09-12T09:00:00Z' }),
    ]);
    expect(partyOf([one]).trails.size).toBe(0);
    const trails = partyOf([one], settings({ trails: true })).trails;
    expect([...trails.keys()]).toEqual(['trip-1:caver-ana']);
    expect(trails.get('trip-1:caver-ana')).toEqual({ stations: ['p.1', 'p.2'], color: MOVIE_MARKER_PALETTE[0] });
  });

  it('in together mode, lets a running trip’s newer note outrank a finished trip’s last one', () => {
    const short = trip('trip-short', 'Short', { ...state([ANA]), armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T10:00:00Z' }, [
      event({ caverId: ANA, kind: 'note', surveyModelId: null, note: 'Out soon', recordedAt: '2026-09-12T09:55:00Z' }),
    ]);
    const long = trip('trip-long', 'Long', { ...state([BOGDAN]), armedAt: '2026-09-13T08:00:00Z', closedAt: '2026-09-13T18:00:00Z' }, [
      event({ caverId: BOGDAN, kind: 'note', surveyModelId: null, note: 'At the sump', recordedAt: '2026-09-13T11:00:00Z' }),
    ]);
    const timeline = buildMovieTimeline(
      [
        { tripLogId: 'trip-short', window: { from: Date.parse('2026-09-12T08:00:00Z'), to: Date.parse('2026-09-12T10:00:00Z') }, moments: [] },
        { tripLogId: 'trip-long', window: { from: Date.parse('2026-09-13T08:00:00Z'), to: Date.parse('2026-09-13T18:00:00Z') }, moments: [] },
      ],
      { mode: 'together', quietGapMs: null },
    )!;
    const noteAtElapsed = (hours: number) =>
      partyOf([short, long], settings({}, { note: true }), timeline.instants(hours * 60 * 60_000)).note;
    // Two hours in, the short trip's note is five minutes old and the long trip has said nothing.
    expect(noteAtElapsed(2)).toBe('Ana: Out soon');
    // Five hours in, the short trip ended long ago; the long trip spoke two hours ago.
    expect(noteAtElapsed(5)).toBe('Bogdan: At the sump');
  });

  it('carries the latest note only when the note caption is on, naming as the markers do', () => {
    const one = trip('trip-1', 'One', state([ANA]), [
      event({ kind: 'note', surveyModelId: null, note: 'All well', recordedAt: '2026-09-12T09:00:00Z' }),
      event({ kind: 'note', surveyModelId: null, note: 'Turning back', recordedAt: '2026-09-12T11:00:00Z' }),
    ]);
    expect(partyOf([one]).note).toBeNull();
    expect(partyOf([one], settings({}, { note: true })).note).toBe('Ana: Turning back');
    expect(partyOf([one], settings({}, { note: true }), [Date.parse('2026-09-12T10:00:00Z')]).note).toBe('Ana: All well');
    expect(partyOf([one], settings({ labels: 'full' }, { note: true })).note).toBe('Ana Popescu: Turning back');
    expect(partyOf([one], settings({ labels: 'initials' }, { note: true })).note).toBe('AP: Turning back');
    expect(partyOf([one], settings({ labels: 'off' }, { note: true })).note).toBe('Turning back');
  });
});

describe('the marker palette', () => {
  const channels = (hex: string) => [1, 3, 5].map((at) => Number.parseInt(hex.slice(at, at + 2), 16));
  // How far apart two colours look, as the CIE 1976 colour difference: about 2 is just noticeable
  // side by side, and markers are small, far apart and move.
  const lab = (hex: string) => {
    const [r, g, b] = channels(hex).map((value) => {
      const unit = value / 255;
      return unit <= 0.04045 ? unit / 12.92 : ((unit + 0.055) / 1.055) ** 2.4;
    });
    const f = (value: number) => (value > 0.008856 ? Math.cbrt(value) : 7.787 * value + 16 / 116);
    const x = f((r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047);
    const y = f(r * 0.2126 + g * 0.7152 + b * 0.0722);
    const z = f((r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883);
    return [116 * y - 16, 500 * (x - y), 200 * (y - z)];
  };
  const apart = (left: string, right: string) => {
    const [a, b] = [lab(left), lab(right)];
    return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
  };

  it('holds twelve opaque colours, the first being the one a single party is drawn in everywhere', () => {
    expect(MOVIE_MARKER_PALETTE).toHaveLength(12);
    expect(MOVIE_MARKER_PALETTE[0]).toBe(trackedCaverPalette.underground);
    // Six hex digits and no alpha: the viewer drops an alpha channel.
    expect(MOVIE_MARKER_PALETTE.every((color) => /^#[0-9a-f]{6}$/.test(color))).toBe(true);
  });

  it('keeps every colour apart from every other, and from the colours that already mean something', () => {
    // The viewer's stations, junctions and entrances, the scene behind them, and somebody out.
    const taken = ['#ff0000', '#ffff00', '#ffffff', '#000000', trackedCaverPalette.out];
    for (const [index, color] of MOVIE_MARKER_PALETTE.entries()) {
      for (const other of MOVIE_MARKER_PALETTE.slice(index + 1)) {
        expect(apart(color, other), `${color} and ${other}`).toBeGreaterThan(20);
      }
      for (const other of taken) {
        expect(apart(color, other), `${color} and ${other}`).toBeGreaterThan(30);
      }
    }
  });
});
