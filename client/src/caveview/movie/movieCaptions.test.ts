// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import {
  drawMovieCaptions,
  movieAutoTitle,
  movieCaptionColors,
  movieClockText,
  movieTitle,
  type MovieCaptions,
} from './movieCaptions.ts';
import { MOVIE_MARKER_PALETTE } from './movieParty.ts';

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
  it('reads elapsed time as hours and minutes', () => {
    expect(movieClockText({ kind: 'elapsed', ms: (2 * 60 + 5) * 60_000 + 59_000 }, i18n.t, 'en')).toBe('Elapsed 2:05');
  });

  it('reads a calendar moment in the reader’s language, with no coordinate or altitude in it', () => {
    const text = movieClockText({ kind: 'calendar', at: Date.parse('2026-09-12T10:30:00Z') }, i18n.t, 'en');
    expect(text).toMatch(/2026/);
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
