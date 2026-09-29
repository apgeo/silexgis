// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { videoBitrate } from './encode/video/videoEncoder.ts';
import { localIsoDate, MOVIE_GIF_SIZE_BUDGET, movieFileName, movieFileSizeEstimate, movieSlug } from './movieOutput.ts';
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
