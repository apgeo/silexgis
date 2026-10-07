// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { videoBitrate } from './encode/video/videoEncoder.ts';
import {
  localIsoDate,
  MOVIE_GIF_SIZE_BUDGET,
  movieExportName,
  movieFileName,
  movieFileSizeEstimate,
  movieSlug,
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
