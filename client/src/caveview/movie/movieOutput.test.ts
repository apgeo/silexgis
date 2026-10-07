// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { videoBitrate } from './encode/video/videoEncoder.ts';
import {
  localIsoDate,
  MOVIE_GIF_SIZE_BUDGET,
  movieEstimateIsCalibrated,
  movieExportName,
  movieFileName,
  movieFileSizeEstimate,
  movieGifCalibrationFrom,
  movieSlug,
  normaliseMovieGifCalibration,
} from './movieOutput.ts';
import { DEFAULT_MOVIE_SETTINGS, normaliseMovieSettings } from './movieSettings.ts';

describe('the estimated size of a movie', () => {
  it('is a video encoder’s target bitrate over the length', () => {
    const settings = normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm', size: '1280x720', fps: 25 });
    // 250 frames at 25 fps is ten seconds.
    expect(movieFileSizeEstimate(settings, 250)).toBe(Math.round((videoBitrate(1280, 720, 25, 'medium') * 10) / 8));
  });

  it('makes a turning GIF many times the size of a still one, and a poorer palette smaller', () => {
    const turning = normaliseMovieSettings(DEFAULT_MOVIE_SETTINGS);
    const still = normaliseMovieSettings({
      ...DEFAULT_MOVIE_SETTINGS,
      rotation: { ...DEFAULT_MOVIE_SETTINGS.rotation, enabled: false },
    });
    const poorer = normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, quality: 'low' });
    expect(movieFileSizeEstimate(turning, 220)).toBeGreaterThan(3 * movieFileSizeEstimate(still, 220));
    expect(movieFileSizeEstimate(poorer, 220)).toBeLessThan(movieFileSizeEstimate(turning, 220));
  });

  it('puts the longest, largest turning GIF past the budget and the default one within it', () => {
    const largest = normaliseMovieSettings({
      ...DEFAULT_MOVIE_SETTINGS,
      size: '800x600',
      fps: 25,
      durationS: 22,
      quality: 'high',
    });
    expect(movieFileSizeEstimate(largest, 600)).toBeGreaterThan(MOVIE_GIF_SIZE_BUDGET);
    expect(movieFileSizeEstimate(normaliseMovieSettings(DEFAULT_MOVIE_SETTINGS), 220)).toBeLessThan(
      MOVIE_GIF_SIZE_BUDGET,
    );
  });
});

describe('the GIF estimate corrected from the GIFs made here', () => {
  const turning = normaliseMovieSettings(DEFAULT_MOVIE_SETTINGS);
  const still = normaliseMovieSettings({
    ...DEFAULT_MOVIE_SETTINGS,
    rotation: { ...DEFAULT_MOVIE_SETTINGS.rotation, enabled: false },
  });

  it('estimates the next GIF of the same kind at what the last one came to', () => {
    // A file half the size the built-in figure reckons: the survey is a sparse one.
    const made = Math.round(movieFileSizeEstimate(turning, 220) / 2);
    const learnt = movieGifCalibrationFrom(made, turning, 220);
    expect(movieFileSizeEstimate(turning, 220, learnt)).toBeCloseTo(made, -2);
    expect(movieEstimateIsCalibrated(turning, learnt)).toBe(true);

    // Learnt per pixel and per frame at the richest palette, so it carries to another size, length
    // and palette instead of only repeating the one file.
    const other = normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, size: '320x180', quality: 'low' });
    const ratio = movieFileSizeEstimate(other, 100, learnt) / movieFileSizeEstimate(other, 100);
    expect(ratio).toBeGreaterThan(0.3);
    expect(ratio).toBeLessThan(0.7);
  });

  it('keeps a turning camera’s figure and a still one’s apart, and leaves video alone', () => {
    const learnt = movieGifCalibrationFrom(movieFileSizeEstimate(turning, 220) * 2, turning, 220);
    expect(learnt.still).toBeUndefined();
    expect(movieFileSizeEstimate(still, 220, learnt)).toBe(movieFileSizeEstimate(still, 220));
    expect(movieEstimateIsCalibrated(still, learnt)).toBe(false);

    const video = normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm' });
    expect(movieFileSizeEstimate(video, 220, learnt)).toBe(movieFileSizeEstimate(video, 220));
    expect(movieEstimateIsCalibrated(video, learnt)).toBe(false);
  });

  it('moves half-way towards each new file, so one unusual movie does not decide the next estimate', () => {
    const first = movieGifCalibrationFrom(movieFileSizeEstimate(turning, 220), turning, 220);
    const second = movieGifCalibrationFrom(movieFileSizeEstimate(turning, 220, { turning: 0.2 }), turning, 220, first);
    expect(second.turning).toBeCloseTo((first.turning! + 0.2) / 2, 3);
  });

  it('believes no figure outside its bounds, measured or stored', () => {
    // A file a thousand times the estimate is taken as the most a frame is believed to cost...
    const huge = movieGifCalibrationFrom(movieFileSizeEstimate(turning, 220) * 1000, turning, 220);
    expect(huge.turning).toBe(0.5);
    // ...and one whose frames come to less than the least teaches nothing at all, rather than the
    // least: a movie of next to nothing must not shrink every estimate after it.
    const before = { turning: 0.03 };
    const pixels = 640 * 360;
    const nearlyEmpty = 1024 + pixels * 0.25 + 2999 * pixels * 0.8 * 0.001;
    expect(movieGifCalibrationFrom(nearlyEmpty, turning, 3000, before)).toBe(before);
    expect(movieGifCalibrationFrom(nearlyEmpty, turning, 3000)).toEqual({});

    expect(normaliseMovieGifCalibration({ turning: 40, still: -1 })).toEqual({ turning: 0.5 });
    expect(normaliseMovieGifCalibration({ turning: 'a lot', still: Number.NaN })).toEqual({});
    expect(normaliseMovieGifCalibration('nonsense')).toEqual({});
    expect(normaliseMovieGifCalibration(undefined)).toEqual({});
    // A stored figure that was never a number is not estimated from.
    const bad = { turning: 'a lot' } as unknown as { turning: number };
    expect(movieFileSizeEstimate(turning, 220, bad)).toBe(movieFileSizeEstimate(turning, 220));
    expect(movieEstimateIsCalibrated(turning, bad)).toBe(false);
  });

  it('learns nothing from a file that says nothing: too few frames, or smaller than one frame', () => {
    const before = { turning: 0.03 };
    expect(movieGifCalibrationFrom(500_000, turning, 9, before)).toBe(before);
    expect(movieGifCalibrationFrom(6, turning, 220, before)).toBe(before);
    expect(movieGifCalibrationFrom(Number.NaN, turning, 220, before)).toBe(before);
    expect(movieGifCalibrationFrom(6, turning, 220)).toEqual({});
    // One byte past the first frame as it is reckoned: nothing is left to say what a frame costs.
    const firstFrameOnly = 1024 + 640 * 360 * 0.25;
    expect(movieGifCalibrationFrom(firstFrameOnly + 1, turning, 220, before)).toBe(before);
    expect(movieGifCalibrationFrom(firstFrameOnly + 1, turning, 220)).toEqual({});
  });

  /**
   * A GIF as one really comes out: its first frame costs about a fifth of what the estimate reckons
   * it at, and each later frame its own figure. The size is built from those two costs, so what is
   * learnt can be held against the cost that was put in.
   */
  const realGif = (frames: number, laterPerPixel: number, quality = 0.8) => {
    const pixels = 640 * 360;
    return Math.round(1024 + pixels * 0.0485 + (frames - 1) * pixels * laterPerPixel * quality);
  };

  it('learns nothing from a GIF too short for its first frame not to decide the figure', () => {
    // Two to four seconds of a turning survey — the first thing a reader makes. Learning from these
    // taught between a third and three quarters of what a frame cost.
    for (const frames of [10, 15, 20, 25]) {
      expect(movieGifCalibrationFrom(realGif(frames, 0.05), turning, frames)).toEqual({});
    }
    // And from a sparse survey, at lengths where the old rule taught a sixth to a half.
    for (const frames of [25, 40, 60]) {
      expect(movieGifCalibrationFrom(realGif(frames, 0.012), turning, frames)).toEqual({});
    }
  });

  it('learns a frame’s cost to within a fifth from any GIF it does learn from', () => {
    for (const [frames, cost] of [
      [35, 0.05],
      [40, 0.05],
      [130, 0.012],
      [220, 0.012],
      [40, 0.2],
    ] as const) {
      const learnt = movieGifCalibrationFrom(realGif(frames, cost), turning, frames).turning;
      expect(learnt, `${frames} frames at ${cost}`).toBeDefined();
      // Never above the truth by this route, and never more than a fifth below it.
      expect(learnt!).toBeLessThanOrEqual(cost);
      expect(learnt!).toBeGreaterThanOrEqual(cost * 0.8);
    }
  });
});

describe('the name a movie is saved under', () => {
  it('is made of the title with its accents dropped and its punctuation made hyphens', () => {
    expect(movieSlug('Peștera Țarina · Tură de explorare #2')).toBe('pestera-tarina-tura-de-explorare-2');
    expect(movieFileName('Peștera Țarina', '2026-09-29', 'gif')).toBe('silexgis-pestera-tarina-2026-09-29.gif');
  });

  it('is called a movie when nothing of the title is usable', () => {
    expect(movieFileName('· · ·', '2026-09-29', 'mp4')).toBe('silexgis-movie-2026-09-29.mp4');
    expect(movieFileName('Пещера', '2026-09-29', 'webm')).toBe('silexgis-movie-2026-09-29.webm');
  });

  it('is cut short without ending in a hyphen', () => {
    const slug = movieSlug(`${'a'.repeat(59)} b`);
    expect(slug.length).toBeLessThanOrEqual(60);
    expect(slug.endsWith('-')).toBe(false);
  });

  it('is dated by the reader’s own calendar day', () => {
    expect(localIsoDate(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
  });
});

describe('the name a movie’s file is given', () => {
  const DAY = '2026-09-29';
  const shown = (titleText = '') => ({ title: true, titleText });
  const hidden = (titleText = '') => ({ title: false, titleText });

  it('is the title written over the movie when the reader wrote one and it is shown', () => {
    expect(movieExportName(shown('  Tura de toamnă  '), ['Alpha'], 'Pestera 1', DAY, 'gif')).toBe(
      'silexgis-tura-de-toamna-2026-09-29.gif',
    );
  });

  it('is what the movie is of when the title is shown and left to the movie: one trip, or the cave', () => {
    expect(movieExportName(shown(), ['Alpha'], 'Pestera 1', DAY, 'webm')).toBe('silexgis-alpha-2026-09-29.webm');
    expect(movieExportName(shown(), ['Alpha', 'Bravo'], 'Pestera 1', DAY, 'mp4')).toBe(
      'silexgis-pestera-1-2026-09-29.mp4',
    );
    // A lone trip with a blank title is called by the cave, as its caption is.
    expect(movieExportName(shown(), ['  '], 'Pestera 1', DAY, 'gif')).toBe('silexgis-pestera-1-2026-09-29.gif');
  });

  it('names neither the trip nor the cave when the title caption is off, whatever was written', () => {
    for (const captions of [hidden(), hidden('Tura de toamnă')]) {
      for (const trips of [['Alpha'], ['Alpha', 'Bravo'], []]) {
        const name = movieExportName(captions, trips, 'Pestera 1', DAY, 'gif');
        expect(name).toBe('silexgis-movie-2026-09-29.gif');
      }
    }
    // The same trips and place do name the file once the caption is on: the neutral name above is
    // the switch's doing, not a title that happened to have nothing usable in it.
    expect(movieExportName(shown(), ['Alpha'], 'Pestera 1', DAY, 'gif')).toContain('alpha');
  });

  it('is called a movie when the shown title has nothing a file name can carry', () => {
    expect(movieExportName(shown('· · ·'), ['Alpha'], 'Pestera 1', DAY, 'gif')).toBe('silexgis-movie-2026-09-29.gif');
    expect(movieExportName(shown(), [], '', DAY, 'gif')).toBe('silexgis-movie-2026-09-29.gif');
  });

  it('leaves the days several trips span out of the name, which already ends in the day it was made', () => {
    expect(movieExportName(shown(), ['Alpha', 'Bravo'], 'Pestera 1', DAY, 'gif')).toMatch(
      /^silexgis-pestera-1-\d{4}-\d{2}-\d{2}\.gif$/,
    );
  });
});
