// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import {
  drawMovieCaptions,
  movieAutoTitle,
  movieCaptionColors,
  movieCaptionsAt,
  movieClockText,
  movieClockWithSpeed,
  movieSpeedText,
  movieTitle,
  type MovieCaptions,
} from './movieCaptions.ts';
import { MOVIE_MARKER_PALETTE } from './movieParty.ts';
import { DEFAULT_MOVIE_SETTINGS, type MovieSettings } from './movieSettings.ts';
import { buildMovieTimeline } from './movieTimeline.ts';

/** The test environment's 2D stub, with the calls this module makes recorded. */
function recordingContext() {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')! as CanvasRenderingContext2D & Record<string, unknown>;
  const fills: string[] = [];
  let fillStyle = '';
  Object.defineProperty(ctx, 'fillStyle', {
    get: () => fillStyle,
    set: (value: string) => {
      fillStyle = value;
    },
  });
  const fillText = vi.fn();
  const fillRect = vi.fn(() => {
    fills.push(fillStyle);
  });
  Object.assign(ctx, { fillText, fillRect, textBaseline: 'alphabetic', textAlign: 'start', globalAlpha: 1 });
  return { ctx, fillText, fillRect, fills };
}

const captions = (overrides: Partial<MovieCaptions> = {}): MovieCaptions => ({
  title: 'Peștera Urșilor · 12 Sep',
  clock: '12 Sep 2026, 10:30',
  legend: [
    { color: MOVIE_MARKER_PALETTE[0], label: 'Echipa 1' },
    { color: trackedCaverPalette.out, label: 'Out' },
  ],
  progress: 0.5,
  note: 'Turning back at the sump',
  size: 1,
  ...overrides,
});

describe('drawMovieCaptions', () => {
  it('draws every caption it is given, each over a plate, with the legend in its colours', () => {
    const { ctx, fillText, fills } = recordingContext();
    drawMovieCaptions(ctx, 640, 360, captions());
    const texts = fillText.mock.calls.map((call) => call[0]);
    expect(texts).toEqual(['12 Sep 2026, 10:30', 'Peștera Urșilor · 12 Sep', 'Echipa 1', 'Out', 'Turning back at the sump']);
    expect(fills).toContain('#000000');
    expect(fills).toContain(MOVIE_MARKER_PALETTE[0]);
    expect(fills).toContain(trackedCaverPalette.out);
    // Every colour drawn is one the GIF palette is told to reserve.
    const reserved = new Set(movieCaptionColors());
    for (const colour of fills) {
      expect(reserved.has(colour)).toBe(true);
    }
    // The font is the system's own sans-serif.
    expect(ctx.font).toMatch(/sans-serif$/);
  });

  it('says how many legend entries it had no room for, and keeps the lines that explain the legend', () => {
    const { ctx, fillText, fills } = recordingContext();
    // Twenty trips' worth of entries, the line saying their colours repeat and the out key after
    // them, as a movie of twenty trips has.
    const legend = [
      ...Array.from({ length: 20 }, (_, index) => ({
        color: MOVIE_MARKER_PALETTE[index % MOVIE_MARKER_PALETTE.length],
        label: `Trip ${index + 1}`,
      })),
      { color: null, label: 'Trip colours repeat', pinned: true },
      { color: trackedCaverPalette.out, label: 'Out', pinned: true },
    ];
    drawMovieCaptions(
      ctx,
      640,
      360,
      captions({ clock: null, note: null, legend, legendMore: (hidden) => `and ${hidden} more` }),
    );
    const texts = fillText.mock.calls.map((call) => call[0] as string);
    const trips = texts.filter((text) => /^Trip \d+$/.test(text));
    // Fewer than all of them fit into a 360-pixel frame...
    expect(trips.length).toBeGreaterThan(0);
    expect(trips.length).toBeLessThan(20);
    // ...and the ones left out are counted, not dropped without a word.
    expect(texts).toContain(`and ${20 - trips.length} more`);
    // That two trips share a colour is still said, on a line with no swatch of its own: every
    // solid colour drawn is one the legend's other lines or the captions already use.
    expect(texts[texts.length - 2]).toBe('Trip colours repeat');
    const reserved = new Set(movieCaptionColors());
    expect(fills.every((colour) => reserved.has(colour))).toBe(true);
    // The grey is still explained, with its swatch.
    expect(texts[texts.length - 1]).toBe('Out');
    expect(fills).toContain(trackedCaverPalette.out);
  });

  it('names the legend entries it left out in the reader’s language', () => {
    const party = { legend: [], note: null };
    const timeline = { length: 0, clock: () => ({ kind: 'elapsed' as const, ms: 0, totalMs: 0 }) };
    const made = movieCaptionsAt(DEFAULT_MOVIE_SETTINGS, null, party, timeline, { position: 0, progress: 0 }, {
      t: i18n.t.bind(i18n),
      language: 'en',
    });
    expect(made.legendMore?.(7)).toBe('+ 7 more');
  });

  it('draws nothing it is not given', () => {
    const { ctx, fillText, fillRect } = recordingContext();
    drawMovieCaptions(ctx, 640, 360, { title: null, clock: null, legend: [], progress: null, note: null, size: 1 });
    expect(fillText).not.toHaveBeenCalled();
    expect(fillRect).not.toHaveBeenCalled();
  });

  it('scales its text with the frame height and the caption size', () => {
    const fontOf = (height: number, size: number) => {
      const { ctx } = recordingContext();
      drawMovieCaptions(ctx, height * 2, height, captions({ legend: [], title: null, note: null, progress: null, size }));
      return Number.parseFloat(/(\d+)px/.exec(ctx.font)![1]);
    };
    expect(fontOf(720, 1)).toBeGreaterThan(fontOf(360, 1));
    expect(fontOf(360, 2)).toBeGreaterThan(fontOf(360, 1));
  });

  it('cuts a title that does not fit, ending it in an ellipsis', () => {
    const { ctx, fillText } = recordingContext();
    drawMovieCaptions(ctx, 320, 180, captions({ title: 'A very long title '.repeat(20), clock: null, legend: [], note: null }));
    const title = fillText.mock.calls[0][0] as string;
    expect(title.endsWith('…')).toBe(true);
    expect(title.length).toBeLessThan(100);
  });

  it('wraps a long note into at most three lines', () => {
    const { ctx, fillText } = recordingContext();
    drawMovieCaptions(ctx, 320, 180, captions({ title: null, clock: null, legend: [], note: 'word '.repeat(200) }));
    expect(fillText.mock.calls.length).toBeLessThanOrEqual(3);
    expect(fillText.mock.calls.length).toBeGreaterThan(1);
  });
});

describe('movieClockText', () => {
  const HOUR = 3_600_000;
  const elapsed = (ms: number, totalMs: number) => movieClockText({ kind: 'elapsed', ms, totalMs }, i18n.t, 'en');

  it('reads elapsed time as hours and minutes when the movie’s clock runs to an hour or more', () => {
    expect(elapsed((2 * 60 + 5) * 60_000 + 59_000, 3 * HOUR)).toBe('Elapsed 2:05');
    // Exactly an hour is already counted in hours, and reads 1:00 at its end rather than 60:00.
    expect(elapsed(HOUR, HOUR)).toBe('Elapsed 1:00');
  });

  it('reads elapsed time as minutes and seconds when all of it is under an hour', () => {
    expect(elapsed(12 * 60_000 + 34_900, 40 * 60_000)).toBe('Elapsed 12:34');
    expect(elapsed(0, 40 * 60_000)).toBe('Elapsed 0:00');
    expect(elapsed(59_999, HOUR - 1)).toBe('Elapsed 0:59');
    expect(elapsed(HOUR - 1, HOUR - 1)).toBe('Elapsed 59:59');
  });

  it('keeps one unit for the whole of a movie: its first minutes are not counted in seconds when it runs past an hour', () => {
    expect(elapsed(5 * 60_000 + 30_000, 2 * HOUR)).toBe('Elapsed 0:05');
    expect(elapsed(5 * 60_000 + 30_000, HOUR - 1)).toBe('Elapsed 5:30');
  });

  it('says the same thing in Romanian, around the same figures', () => {
    expect(movieClockText({ kind: 'elapsed', ms: 90_000, totalMs: 10 * 60_000 }, i18n.getFixedT('ro'), 'ro')).toBe(
      'Timp scurs 1:30',
    );
  });

  it('reads a calendar moment in the reader’s language, with no coordinate or altitude in it', () => {
    const text = movieClockText({ kind: 'calendar', at: Date.parse('2026-09-12T10:30:00Z') }, i18n.t, 'en');
    expect(text).toMatch(/2026/);
  });
});

describe('movieSpeedText', () => {
  it('rounds to what a reader can use: tens from a hundred, whole from ten, one decimal below', () => {
    expect(movieSpeedText(237, 'en')).toBe('240');
    expect(movieSpeedText(104.9, 'en')).toBe('100');
    expect(movieSpeedText(86.4, 'en')).toBe('86');
    expect(movieSpeedText(12.5, 'en')).toBe('13');
    expect(movieSpeedText(2.54, 'en')).toBe('2.5');
    expect(movieSpeedText(4, 'en')).toBe('4');
  });

  it('cuts between the forms on the figure shown, so none reads 99.7 or 10.0', () => {
    expect(movieSpeedText(99.7, 'en')).toBe('100');
    expect(movieSpeedText(9.96, 'en')).toBe('10');
    expect(movieSpeedText(9.94, 'en')).toBe('9.9');
  });

  it('keeps a movie slower than life readable instead of rounding it to nothing', () => {
    expect(movieSpeedText(0.333, 'en')).toBe('0.33');
    expect(movieSpeedText(0.042, 'en')).toBe('0.042');
  });

  it('says nothing within a twentieth of life speed, and nothing for no figure at all', () => {
    for (const about of [0.95, 1, 1.04, 1.05]) {
      expect(movieSpeedText(about, 'en')).toBeNull();
    }
    expect(movieSpeedText(0.94, 'en')).toBe('0.94');
    expect(movieSpeedText(1.06, 'en')).toBe('1.1');
    for (const none of [null, 0, -3, Number.NaN, Number.POSITIVE_INFINITY]) {
      expect(movieSpeedText(none, 'en')).toBeNull();
    }
  });

  it('writes the decimal mark and the thousands the reader’s language uses', () => {
    expect(movieSpeedText(2.54, 'ro')).toBe('2,5');
    expect(movieSpeedText(14_403, 'en')).toBe('14,400');
    expect(movieSpeedText(14_403, 'ro')).toBe('14.400');
  });
});

describe('the time-lapse figure in the clock caption', () => {
  const HOUR = 3_600_000;
  const T0 = Date.parse('2026-09-12T08:00:00Z');
  const words = { t: i18n.t.bind(i18n), language: 'en' };
  const party = { legend: [], note: null };
  const twoHours = (mode: 'calendar' | 'together') =>
    buildMovieTimeline([{ tripLogId: 'trip-a', window: { from: T0, to: T0 + 2 * HOUR }, moments: [] }], {
      mode,
      quietGapMs: null,
    })!;
  /** 30 s at 25 frames a second: two hours go by 240 times faster than life, near enough. */
  const chosen = (captions: Partial<MovieSettings['captions']> = {}, output: Partial<MovieSettings> = {}): MovieSettings => ({
    ...DEFAULT_MOVIE_SETTINGS,
    fps: 25,
    durationS: 30,
    ...output,
    captions: { ...DEFAULT_MOVIE_SETTINGS.captions, ...captions },
  });
  const clockOf = (settings: MovieSettings, mode: 'calendar' | 'together' = 'together', position = HOUR) =>
    movieCaptionsAt(settings, null, party, twoHours(mode), { position, progress: 0.5 }, words).clock;

  it('follows the clock, on by default', () => {
    expect(DEFAULT_MOVIE_SETTINGS.captions.speed).toBe(true);
    expect(clockOf(chosen())).toBe('Elapsed 1:00 · ×240');
    const calendar = clockOf(chosen(), 'calendar')!;
    expect(calendar).toMatch(/2026.* · ×240$/u);
  });

  it('is left off when its switch is, and the clock stands as it was', () => {
    expect(clockOf(chosen({ speed: false }))).toBe('Elapsed 1:00');
  });

  it('is never drawn without the clock', () => {
    expect(clockOf(chosen({ clock: false, speed: true }))).toBeNull();
  });

  it('is left out of a movie that runs at about life speed', () => {
    // Two hours of trip in two hours of movie.
    expect(clockOf(chosen({}, { fps: 5, durationS: 7200 }))).toBe('Elapsed 1:00');
  });

  it('is the same on every frame of a movie, the still frames at the end included', () => {
    const settings = chosen();
    const speeds = [0, 0.25 * HOUR, HOUR, 2 * HOUR].map((position) => clockOf(settings, 'together', position)!.split(' · ')[1]);
    expect(new Set(speeds)).toEqual(new Set(['×240']));
  });

  it('is written in Romanian around the same figure, with the Romanian decimal mark', () => {
    const t = i18n.getFixedT('ro');
    expect(movieClockWithSpeed('Timp scurs 1:30', 2.54, t, 'ro')).toBe('Timp scurs 1:30 · ×2,5');
    expect(movieClockWithSpeed('Timp scurs 1:30', 1, t, 'ro')).toBe('Timp scurs 1:30');
    expect(movieClockWithSpeed('Timp scurs 1:30', null, t, 'ro')).toBe('Timp scurs 1:30');
  });
});

describe('movieAutoTitle', () => {
  it('calls one trip by its own title', () => {
    expect(movieAutoTitle([' Alpha '], 'Pestera 1', '12.09.2026')).toBe('Alpha');
  });

  it('calls several trips by the place and the days they span', () => {
    expect(movieAutoTitle(['Alpha', 'Bravo'], 'Pestera 1', '12.09.2026 – 14.09.2026')).toBe(
      'Pestera 1 · 12.09.2026 – 14.09.2026',
    );
    expect(movieAutoTitle(['Alpha', 'Bravo'], 'Pestera 1', null)).toBe('Pestera 1');
  });

  it('falls back to the place for a lone trip with a blank title', () => {
    expect(movieAutoTitle(['  '], 'Pestera 1', '12.09.2026')).toBe('Pestera 1 · 12.09.2026');
  });
});

describe('movieTitle', () => {
  it('prefers the reader’s own words, and says nothing with the caption off', () => {
    expect(movieTitle({ title: true, titleText: '  Own  ' }, 'Auto')).toBe('Own');
    expect(movieTitle({ title: true, titleText: ' ' }, 'Auto')).toBe('Auto');
    expect(movieTitle({ title: false, titleText: 'Own' }, 'Auto')).toBeNull();
  });
});
