// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_MOVIE_SETTINGS, type MovieSettings } from './movieSettings.ts';
import {
  buildMovieTimeline,
  movieFrameCount,
  movieFrames,
  movieNewReports,
  moviePlayedMs,
  movieQuietGapMs,
  MOVIE_QUIET_TRIP_MS,
  movieSpansAt,
  movieSpansShowing,
  movieSpeedFactor,
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

  it('plays a written-up trip over its reports, not over the watch opened to write it up', () => {
    const events = [report(T0 + 3 * HOUR, 'model-1'), report(T0 + HOUR, 'model-1')];
    // A journal imported two weeks later: armed and closed after every report it holds.
    const imported = { armedAt: at(T0 + 14 * 24 * HOUR), closedAt: at(T0 + 14 * 24 * HOUR + MIN) };
    expect(movieTripSpan('trip-a', imported, events, T0, 'model-1')?.window).toEqual({ from: T0 + HOUR, to: T0 + 3 * HOUR });
    // Armed in time but closed the next evening, when somebody entered the last reports from paper.
    const closedLate = { armedAt: at(T0), closedAt: at(T0 + 30 * HOUR) };
    expect(movieTripSpan('trip-a', closedLate, events, T0, 'model-1')?.window).toEqual({ from: T0 + HOUR, to: T0 + 3 * HOUR });
    // A watch kept by the party itself, closed a slow walk out after the last report, is the trip.
    const kept = { armedAt: at(T0), closedAt: at(T0 + 8 * HOUR) };
    expect(movieTripSpan('trip-a', kept, events, T0, 'model-1')?.window).toEqual({ from: T0, to: T0 + 8 * HOUR });
    // One report written up is still a minute to stand at.
    expect(movieTripSpan('trip-a', imported, [report(T0, 'model-1')], T0, 'model-1')?.window).toEqual({ from: T0, to: T0 + MIN });
  });

  it('keeps a trip still under way running to where the movie ends, however its watch began', () => {
    const lateArmed = { armedAt: at(T0 + 2 * HOUR), closedAt: null };
    expect(movieTripSpan('trip-a', lateArmed, [report(T0 + HOUR, 'model-1')], T0 + 5 * HOUR, 'model-1')?.window).toEqual({
      from: T0 + HOUR,
      to: T0 + 5 * HOUR,
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

  it('paces a movie by the reports of the people who appear, and of nobody else', () => {
    const by = (caverId: string, ms: number, surveyModelId: string | null = 'model-1') => ({
      ...report(ms, surveyModelId),
      caverId,
    });
    const tracking = { armedAt: at(T0), closedAt: at(T0 + 8 * HOUR) };
    // Ana reports at the start and the end; Bogdan alone in the middle, and once on another survey.
    const events = [by('ana', T0), by('bogdan', T0 + 4 * HOUR), by('bogdan', T0 + 5 * HOUR, 'model-2'), by('ana', T0 + 8 * HOUR)];
    const trips = [{ tripLogId: 'trip-a', tracking, events }];
    const spans = movieSpansAt(trips, T0 + 9 * HOUR, 'model-1')!;
    expect(spans[0].moments).toEqual([T0, T0 + 4 * HOUR, T0 + 8 * HOUR]);

    // Everybody appears: the very spans, untouched.
    expect(movieSpansShowing(spans, trips, 'model-1', () => true)).toEqual(spans);
    expect(movieSpansShowing(spans, trips, 'model-1', () => true)[0]).toBe(spans[0]);

    const without = movieSpansShowing(spans, trips, 'model-1', (trip, caver) => !(trip === 'trip-a' && caver === 'bogdan'));
    expect(without).toEqual([{ tripLogId: 'trip-a', window: spans[0].window, moments: [T0, T0 + 8 * HOUR] }]);

    // And so the movie does not stop at the moment somebody it does not show was reported: with
    // him the clock dwells around the fourth hour, without him it goes straight across.
    const quiet = { mode: 'calendar' as const, quietGapMs: 30 * MIN };
    const withHim = buildMovieTimeline(spans, quiet)!;
    const withoutHim = buildMovieTimeline(without, quiet)!;
    expect(withHim.length).toBeGreaterThan(withoutHim.length);
    const shownAt = (timeline: typeof withHim) =>
      Array.from({ length: 201 }, (_, step) => timeline.instants((step / 200) * timeline.length)[0]);
    const near = (instant: number) => Math.abs(instant - (T0 + 4 * HOUR)) < 10 * MIN;
    expect(shownAt(withHim).filter(near).length).toBeGreaterThan(shownAt(withoutHim).filter(near).length + 10);

    // A span of a trip that is not among the trips given is handed back as it is.
    expect(movieSpansShowing(spans, [], 'model-1', () => false)).toEqual(spans);
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

describe('movieSpeedFactor', () => {
  const twoHours = buildMovieTimeline([span('trip-a', T0, T0 + 2 * HOUR, [])], { mode: 'calendar', quietGapMs: null })!;

  it('is how much of the timeline goes by in a second of the movie, between its first and last replay frames', () => {
    // 30 s at 25 frames a second is 750 frames, the last of them showing the end: the two hours are
    // crossed in 749 frame times.
    const chosen = settings({ fps: 25, durationS: 30, holdEndS: 2 });
    expect(movieSpeedFactor(twoHours, chosen)).toBeCloseTo((2 * HOUR) / ((749 * 1000) / 25), 9);
    // Which is what the schedule's own frames say: one frame's step of the timeline over one frame's time.
    const frames = movieFrames(twoHours, chosen);
    const step = frames.frame(1).position - frames.frame(0).position;
    expect(movieSpeedFactor(twoHours, chosen)).toBeCloseTo(step / (1000 / 25), 9);
  });

  it('is the same on every frame: it reads nothing but the timeline’s length and the settings', () => {
    const chosen = settings({ fps: 10, durationS: 10, holdEndS: 5 });
    expect(movieSpeedFactor({ length: twoHours.length }, chosen)).toBe(movieSpeedFactor(twoHours, chosen));
    // The still frames at the end add nothing to it.
    expect(movieSpeedFactor(twoHours, { ...chosen, holdEndS: 0 })).toBe(movieSpeedFactor(twoHours, chosen));
  });

  it('is the rate of what is shown, not of what was skipped: a shortened quiet stretch lowers it', () => {
    // Twenty minutes of reports at each end of ten hours, and nothing between them.
    const quiet = [span('trip-a', T0, T0 + 10 * HOUR, [T0 + 20 * MIN, T0 + 10 * HOUR - 20 * MIN])];
    const whole = buildMovieTimeline(quiet, { mode: 'calendar', quietGapMs: null })!;
    const shortened = buildMovieTimeline(quiet, { mode: 'calendar', quietGapMs: 30 * MIN })!;
    const chosen = settings({ fps: 10, durationS: 10 });
    expect(shortened.length).toBe(70 * MIN);
    expect(movieSpeedFactor(shortened, chosen)! / movieSpeedFactor(whole, chosen)!).toBeCloseTo(70 / 600, 9);
  });

  it('has no figure for a movie of one replay frame, or a timeline with no length', () => {
    expect(movieSpeedFactor(twoHours, settings({ fps: 10, durationS: 0 }))).toBeNull();
    expect(movieSpeedFactor({ length: 0 }, settings({ fps: 10, durationS: 10 }))).toBeNull();
    expect(movieSpeedFactor({ length: Number.NaN }, settings({ fps: 10, durationS: 10 }))).toBeNull();
  });
});

describe('movieQuietGapMs', () => {
  const DAY = 24 * HOUR;
  /** How long, on screen, the longest stretch between two cuts of a calendar timeline lasts. */
  function longestWaitMs(trips: MovieTripSpan[], quietGapMs: number | null, playedMs: number): number {
    const timeline = buildMovieTimeline(trips, { mode: 'calendar', quietGapMs })!;
    const cuts = [...new Set(trips.flatMap((trip) => [trip.window.from, trip.window.to, ...trip.moments]))].sort((a, b) => a - b);
    let longest = 0;
    for (let index = 0; index + 1 < cuts.length; index++) {
      const real = cuts[index + 1] - cuts[index];
      longest = Math.max(longest, quietGapMs === null ? real : Math.min(real, quietGapMs));
    }
    return (longest / timeline.length) * playedMs;
  }

  /** An expedition: one trip a day for `days` days, each eight hours with a report every half hour. */
  function expedition(days: number): MovieTripSpan[] {
    return Array.from({ length: days }, (_, day) => {
      const from = T0 + day * DAY;
      const moments = Array.from({ length: 17 }, (_, step) => from + step * 30 * MIN);
      return span(`trip-${day}`, from, from + 8 * HOUR, moments);
    });
  }

  it('keeps the picture from standing still longer than asked, however long the trips run', () => {
    const playedMs = moviePlayedMs(settings({ fps: 10, durationS: 30 }))!;
    // A single day of half-hour steps is too few to fill thirty seconds with one-second waits;
    // that case is played at an even pace, below.
    for (const days of [4, 28]) {
      const trips = expedition(days);
      const gap = movieQuietGapMs(trips, 'calendar', 1000, playedMs);
      expect(longestWaitMs(trips, gap, playedMs), `${days} days`).toBeLessThanOrEqual(1000 + 1e-6);
    }
  });

  it('cuts a quiet stretch to half an hour of the trips’ time at most, however short the screen allows', () => {
    const trips = expedition(28);
    const playedMs = moviePlayedMs(settings({ fps: 10, durationS: 60 }))!;
    // Each night is about a second at the movie's speed: the half hour cuts it shorter still, so
    // most of the movie is spent underground.
    expect(movieQuietGapMs(trips, 'calendar', 1000, playedMs)).toBe(MOVIE_QUIET_TRIP_MS);
  });

  it('holds an afternoon’s quiet hours for a second, not for half an hour of the trip', () => {
    // Reports two minutes apart for eighty minutes, then nearly three hours of nothing, in a
    // twenty-second movie.
    const moments = Array.from({ length: 40 }, (_, step) => T0 + (step + 1) * 2 * MIN);
    const trips = [span('a', T0, T0 + 4 * HOUR, moments)];
    const playedMs = moviePlayedMs(settings({ fps: 10, durationS: 20 }))!;
    const gap = movieQuietGapMs(trips, 'calendar', 1000, playedMs)!;
    expect(gap).toBeLessThan(MOVIE_QUIET_TRIP_MS);
    expect(longestWaitMs(trips, gap, playedMs)).toBeCloseTo(1000, 3);
    // Half an hour, as the movie had alone, held the picture still for over five seconds.
    expect(longestWaitMs(trips, MOVIE_QUIET_TRIP_MS, playedMs)).toBeGreaterThan(5000);
  });

  it('shortens nothing where no stretch would hold the picture that long', () => {
    // Four stretches of twenty minutes in a three-second movie: each holds the picture for 750 ms,
    // and none is longer than the half hour either.
    const trips = [span('a', T0, T0 + 80 * MIN, [T0 + 20 * MIN, T0 + 40 * MIN, T0 + 60 * MIN])];
    expect(movieQuietGapMs(trips, 'calendar', 1000, 3000)).toBe(MOVIE_QUIET_TRIP_MS);
    expect(buildMovieTimeline(trips, { mode: 'calendar', quietGapMs: MOVIE_QUIET_TRIP_MS })!.length).toBe(80 * MIN);
  });

  it('plays a few reports at an even pace where no threshold could keep the wait that short', () => {
    // Two stretches in a twenty-second movie: whatever is cut, each takes about ten seconds.
    const trips = [span('a', T0, T0 + 4 * HOUR, [T0 + 10 * MIN])];
    expect(movieQuietGapMs(trips, 'calendar', 1000, 20_000)).toBe(10 * MIN);
  });

  it('measures side by side along the elapsed time, as that is what is played', () => {
    const trips = [span('a', T0, T0 + 4 * HOUR, [T0 + 10 * MIN, T0 + 20 * MIN]), span('b', T0 + DAY, T0 + DAY + 4 * HOUR, [T0 + DAY + 10 * MIN, T0 + DAY + 20 * MIN])];
    // Together the two trips report at the same elapsed moments: 10, 10, then 3 h 40 of nothing.
    expect(movieQuietGapMs(trips, 'together', 1000, 20_000)).toBe(10 * MIN);
    expect(movieQuietGapMs([], 'calendar', 1000, 20_000)).toBeNull();
    // Nothing on screen to measure against: the half hour alone.
    expect(movieQuietGapMs(trips, 'calendar', 1000, 0)).toBe(MOVIE_QUIET_TRIP_MS);
  });
});
