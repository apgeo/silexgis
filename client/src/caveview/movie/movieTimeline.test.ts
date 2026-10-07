// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_MOVIE_SETTINGS, type MovieSettings } from './movieSettings.ts';
import {
  buildMovieTimeline,
  movieFrameCount,
  movieFrames,
  movieNewReports,
  movieSpansAt,
  movieTripIsLive,
  movieTripSpan,
  type MovieTripSpan,
} from './movieTimeline.ts';

const MIN = 60_000;
const HOUR = 60 * MIN;
const T0 = Date.parse('2026-09-12T08:00:00Z');

const span = (tripLogId: string, from: number, to: number, moments: number[]): MovieTripSpan => ({
  tripLogId,
  window: { from, to },
  moments,
});

const settings = (overrides: Partial<MovieSettings> = {}): MovieSettings => ({
  ...DEFAULT_MOVIE_SETTINGS,
  ...overrides,
});

describe('buildMovieTimeline — calendar', () => {
  it('plays real time across every trip when nothing is shortened', () => {
    const timeline = buildMovieTimeline(
      [span('a', T0, T0 + 2 * HOUR, [T0 + HOUR]), span('b', T0 + HOUR, T0 + 3 * HOUR, [])],
      { mode: 'calendar', quietGapMs: null },
    )!;
    expect(timeline.length).toBe(3 * HOUR);
    expect(timeline.instants(0)).toEqual([T0, T0]);
    expect(timeline.instants(90 * MIN)).toEqual([T0 + 90 * MIN, T0 + 90 * MIN]);
    expect(timeline.clock(3 * HOUR)).toEqual({ kind: 'calendar', at: T0 + 3 * HOUR });
    // Positions outside the timeline stay at its ends.
    expect(timeline.instants(-5)).toEqual([T0, T0]);
    expect(timeline.instants(10 * HOUR)).toEqual([T0 + 3 * HOUR, T0 + 3 * HOUR]);
  });

  it('shortens a stretch no trip reports in to the threshold, and the clock jumps across it', () => {
    // Reports at 08:10 and 08:20, then nothing until 14:00: a 5h40 stretch.
    const moments = [T0 + 10 * MIN, T0 + 20 * MIN, T0 + 6 * HOUR];
    const timeline = buildMovieTimeline([span('a', T0, T0 + 6 * HOUR + 10 * MIN, moments)], {
      mode: 'calendar',
      quietGapMs: 30 * MIN,
    })!;
    // 10 + 10 + (shortened to 30) + 10 minutes.
    expect(timeline.length).toBe(60 * MIN);
    // Real speed up to the quiet stretch and for the first half of what is kept of it…
    expect(timeline.instants(20 * MIN)[0]).toBe(T0 + 20 * MIN);
    expect(timeline.instants(35 * MIN)[0]).toBe(T0 + 35 * MIN);
    // …then the clock jumps, and the last half runs at real speed into the next report.
    expect(timeline.instants(36 * MIN)[0]).toBe(T0 + 6 * HOUR - 14 * MIN);
    expect(timeline.instants(50 * MIN)[0]).toBe(T0 + 6 * HOUR);
    expect(timeline.instants(60 * MIN)[0]).toBe(T0 + 6 * HOUR + 10 * MIN);
  });

  it('does not shorten a stretch another trip reports in', () => {
    const quietA = span('a', T0, T0 + 4 * HOUR, [T0 + 10 * MIN, T0 + 4 * HOUR]);
    const alone = buildMovieTimeline([quietA], { mode: 'calendar', quietGapMs: 30 * MIN })!;
    const withB = buildMovieTimeline(
      [quietA, span('b', T0, T0 + 4 * HOUR, [T0 + HOUR, T0 + 90 * MIN, T0 + 2 * HOUR, T0 + 150 * MIN, T0 + 3 * HOUR, T0 + 210 * MIN])],
      { mode: 'calendar', quietGapMs: 30 * MIN },
    )!;
    expect(alone.length).toBe(40 * MIN);
    expect(withB.length).toBe(4 * HOUR - 20 * MIN);
  });

  it('answers null for no trips and for a trip with no stretch to play', () => {
    expect(buildMovieTimeline([], { mode: 'calendar', quietGapMs: null })).toBeNull();
    expect(buildMovieTimeline([span('a', T0, T0, [])], { mode: 'calendar', quietGapMs: null })).toBeNull();
  });
});

describe('buildMovieTimeline — together', () => {
  it('starts every trip at the same moment and reads the clock as elapsed time', () => {
    const dayTwo = T0 + 24 * HOUR;
    const timeline = buildMovieTimeline(
      [span('a', T0, T0 + 2 * HOUR, []), span('b', dayTwo, dayTwo + HOUR, [])],
      { mode: 'together', quietGapMs: null },
    )!;
    expect(timeline.mode).toBe('together');
    expect(timeline.length).toBe(2 * HOUR);
    expect(timeline.instants(0)).toEqual([T0, dayTwo]);
    expect(timeline.instants(30 * MIN)).toEqual([T0 + 30 * MIN, dayTwo + 30 * MIN]);
    // The shorter trip's instant runs on past its own end, keeping the shared clock.
    expect(timeline.instants(2 * HOUR)).toEqual([T0 + 2 * HOUR, dayTwo + 2 * HOUR]);
    // The clock also says how far it will run: the longest trip's length.
    expect(timeline.clock(45 * MIN)).toEqual({ kind: 'elapsed', ms: 45 * MIN, totalMs: 2 * HOUR });
  });

  it('shortens quiet stretches on the elapsed axis, counting every trip’s reports', () => {
    const dayTwo = T0 + 24 * HOUR;
    const timeline = buildMovieTimeline(
      [
        span('a', T0, T0 + 3 * HOUR, [T0 + 10 * MIN]),
        span('b', dayTwo, dayTwo + 3 * HOUR, [dayTwo + 2 * HOUR]),
      ],
      { mode: 'together', quietGapMs: 30 * MIN },
    )!;
    // Anchors at 0, 10 min, 2 h and 3 h of elapsed time: 10 + 30 + 30 minutes.
    expect(timeline.length).toBe(70 * MIN);
    // How far the clock runs is the real time under way, not the shortened length it is played in:
    // a clock that reaches three hours is written in hours however quickly it gets there.
    expect(timeline.clock(70 * MIN)).toEqual({ kind: 'elapsed', ms: 3 * HOUR, totalMs: 3 * HOUR });
    expect(timeline.clock(0)).toEqual({ kind: 'elapsed', ms: 0, totalMs: 3 * HOUR });
  });
});

describe('movieFrames', () => {
  const timeline = buildMovieTimeline([span('a', T0, T0 + HOUR, [])], { mode: 'calendar', quietGapMs: null })!;

  it('is a pure function of the frame index', () => {
    const one = movieFrames(timeline, settings());
    const two = movieFrames(timeline, settings());
    const all = (schedule: typeof one) => Array.from({ length: schedule.count }, (_, index) => schedule.frame(index));
    expect(all(one)).toEqual(all(two));
    // Asked out of order, a frame is still the same frame.
    expect(one.frame(57)).toEqual(all(two)[57]);
  });

  it('spans the timeline over the replay part, then holds its end', () => {
    const schedule = movieFrames(timeline, settings({ fps: 10, durationS: 5, holdEndS: 1 }));
    expect(schedule.count).toBe(60);
    expect(schedule.fps).toBe(10);
    expect(schedule.frame(0)).toMatchObject({ position: 0, progress: 0, holding: false, advanceMs: 0 });
    expect(schedule.frame(49)).toMatchObject({ position: HOUR, progress: 1, holding: false, advanceMs: 100 });
    expect(schedule.frame(50)).toMatchObject({ position: HOUR, progress: 1, holding: true, advanceMs: 100 });
    // The camera stops where the replay part left it.
    expect(schedule.frame(59).azimuthOffset).toBe(schedule.frame(49).azimuthOffset);
  });

  it('turns the camera at the speed asked for, clockwise as a positive angle', () => {
    const clockwise = movieFrames(timeline, settings({
      fps: 10,
      rotation: { enabled: true, mode: 'speed', degreesPerSecond: 36, clockwise: true },
    }));
    // 36° a second is 3.6° a frame at 10 frames a second.
    expect(clockwise.frame(10).azimuthOffset).toBeCloseTo(Math.PI / 5);
    expect(clockwise.frame(1).azimuthOffset).toBeGreaterThan(0);
    const counter = movieFrames(timeline, settings({
      fps: 10,
      rotation: { enabled: true, mode: 'speed', degreesPerSecond: 36, clockwise: false },
    }));
    expect(counter.frame(10).azimuthOffset).toBeCloseTo(-Math.PI / 5);
    expect(counter.frame(0).azimuthOffset).toBe(0);
    for (let index = 1; index < 20; index++) {
      expect(clockwise.frame(index).azimuthOffset).toBeGreaterThan(clockwise.frame(index - 1).azimuthOffset);
    }
  });

  it('makes one full turn over the replay part, never showing the start angle twice', () => {
    const schedule = movieFrames(timeline, settings({
      fps: 10,
      durationS: 4,
      holdEndS: 0,
      rotation: { enabled: true, mode: 'fullTurn', degreesPerSecond: 1, clockwise: true },
    }));
    expect(schedule.count).toBe(40);
    expect(schedule.frame(20).azimuthOffset).toBeCloseTo(Math.PI);
    expect(schedule.frame(39).azimuthOffset).toBeCloseTo(2 * Math.PI * (39 / 40));
  });

  it('keeps the camera still when rotation is off', () => {
    const schedule = movieFrames(timeline, settings({ rotation: { ...DEFAULT_MOVIE_SETTINGS.rotation, enabled: false } }));
    expect(schedule.frame(30).azimuthOffset).toBe(0);
  });
});

describe('movieTripSpan', () => {
  const at = (ms: number) => new Date(ms).toISOString();
  const report = (ms: number, surveyModelId: string | null) => ({ recordedAt: at(ms), surveyModelId });

  it('spans the trip as its replay does, with the moments on this model in time order', () => {
    const tracking = { armedAt: at(T0), closedAt: at(T0 + 4 * HOUR) };
    // Newest first, as the log is read; one report on another survey, one with no place.
    const events = [
      report(T0 + 3 * HOUR, 'model-1'),
      report(T0 + 2 * HOUR, 'model-2'),
      report(T0 + HOUR, null),
      report(T0 + 30 * MIN, 'model-1'),
    ];

    const result = movieTripSpan('trip-a', tracking, events, T0 + 10 * HOUR, 'model-1');

    expect(result).toEqual({
      tripLogId: 'trip-a',
      window: { from: T0, to: T0 + 4 * HOUR },
      moments: [T0 + 30 * MIN, T0 + HOUR, T0 + 3 * HOUR],
    });
  });

  it('has nothing to replay for a watch that was never armed', () => {
    expect(movieTripSpan('trip-a', { armedAt: null, closedAt: null }, [], T0, 'model-1')).toBeNull();
  });

  it('ends a trip still under way where the movie was opened', () => {
    const result = movieTripSpan('trip-a', { armedAt: at(T0), closedAt: null }, [], T0 + 2 * HOUR, 'model-1');
    expect(result?.window).toEqual({ from: T0, to: T0 + 2 * HOUR });
  });

  it('moves only the trips still under way when the spans are asked for again later', () => {
    const live = { tripLogId: 'live', tracking: { armedAt: at(T0), closedAt: null }, events: [report(T0 + HOUR, 'model-1')] };
    const done = {
      tripLogId: 'done',
      tracking: { armedAt: at(T0), closedAt: at(T0 + 2 * HOUR) },
      events: [report(T0 + HOUR, 'model-1')],
    };
    expect(movieTripIsLive(live.tracking)).toBe(true);
    expect(movieTripIsLive(done.tracking)).toBe(false);
    expect(movieTripIsLive({ armedAt: null, closedAt: null })).toBe(false);

    const opened = movieSpansAt([live, done], T0 + 3 * HOUR, 'model-1');
    const exported = movieSpansAt([live, done], T0 + 5 * HOUR, 'model-1');
    expect(opened?.map((entry) => entry.window.to)).toEqual([T0 + 3 * HOUR, T0 + 2 * HOUR]);
    expect(exported?.map((entry) => entry.window.to)).toEqual([T0 + 5 * HOUR, T0 + 2 * HOUR]);
    expect(exported?.map((entry) => entry.tripLogId)).toEqual(['live', 'done']);

    // A trip with nothing to replay is not left out of the list, which would put every later
    // trip's span against the wrong trip: there is no list.
    const never = { tripLogId: 'never', tracking: { armedAt: null, closedAt: null }, events: [] };
    expect(movieSpansAt([live, never, done], T0 + 5 * HOUR, 'model-1')).toBeNull();
  });

  it('counts the reports that have arrived by which they are, not by the time they carry', () => {
    const known = new Set(['r1', 'r2']);
    expect(movieNewReports([{ id: 'r1' }, { id: 'r2' }], known)).toBe(0);
    // One entered since, about a moment before the two already known: still one new report.
    expect(movieNewReports([{ id: 'r3' }, { id: 'r1' }, { id: 'r2' }], known)).toBe(1);
    // One of the known ones taken back, another added.
    expect(movieNewReports([{ id: 'r2' }, { id: 'r4' }], known)).toBe(1);
  });
});

describe('movieFrameCount', () => {
  it('counts the replay part and the still frames, and is the count the schedule has', () => {
    const chosen = settings({ fps: 12.5, durationS: 4, holdEndS: 2 });
    expect(movieFrameCount(chosen)).toEqual({ replayFrames: 50, holdFrames: 25, count: 75 });
    const timeline = buildMovieTimeline([span('trip-a', T0, T0 + HOUR, [])], { mode: 'calendar', quietGapMs: null })!;
    expect(movieFrames(timeline, chosen).count).toBe(75);
  });

  it('never has an empty replay part', () => {
    expect(movieFrameCount(settings({ fps: 10, durationS: 0, holdEndS: 0 })).count).toBe(1);
  });
});
