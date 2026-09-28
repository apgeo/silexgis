// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_MOVIE_SETTINGS,
  MOVIE_GIF_FRAME_RATES,
  MOVIE_GIF_MAX_FRAMES,
  MOVIE_GIF_MAX_WIDTH,
  MOVIE_SIZES,
  MOVIE_VIEW_LAYERS,
  movieSize,
  normaliseMovieSettings,
} from './movieSettings.ts';

describe('normaliseMovieSettings', () => {
  it('answers the defaults for nothing, and for anything that is not an object', () => {
    for (const raw of [undefined, null, 'gif', 12, [], true]) {
      expect(normaliseMovieSettings(raw)).toEqual(DEFAULT_MOVIE_SETTINGS);
    }
  });

  it('keeps the defaults intact through a round trip', () => {
    expect(normaliseMovieSettings(structuredClone(DEFAULT_MOVIE_SETTINGS))).toEqual(DEFAULT_MOVIE_SETTINGS);
  });

  it('starts private: no heads-up display, grid or note caption', () => {
    expect(DEFAULT_MOVIE_SETTINGS.view.HUD).toBe(false);
    expect(DEFAULT_MOVIE_SETTINGS.view.grid).toBe(false);
    expect(DEFAULT_MOVIE_SETTINGS.captions.note).toBe(false);
  });

  it('fills a partial object field by field, keeping what it does hold', () => {
    const settings = normaliseMovieSettings({ format: 'webm', cavers: { labels: 'initials' }, view: { grid: true } });
    expect(settings.format).toBe('webm');
    expect(settings.cavers.labels).toBe('initials');
    expect(settings.cavers.showOut).toBe(DEFAULT_MOVIE_SETTINGS.cavers.showOut);
    expect(settings.view.grid).toBe(true);
    expect(settings.view.legs).toBe(DEFAULT_MOVIE_SETTINGS.view.legs);
    expect(settings.captions).toEqual(DEFAULT_MOVIE_SETTINGS.captions);
  });

  it('replaces values nothing offers, and clamps numbers into range', () => {
    const settings = normaliseMovieSettings({
      format: 'avi',
      size: '7x7',
      quality: 'ultra',
      durationS: -5,
      holdEndS: 1e9,
      rotation: { mode: 'wobble', degreesPerSecond: 1000, enabled: 'yes' },
      timeline: { mode: 'sideways', quietGapMin: 0 },
      cavers: { labelSize: 400, colourBy: 'rainbow', transitionS: Number.NaN },
      view: { linewidth: 3, zScale: -1, shadingMode: 2.5, camera: 'fisheye' },
      captions: { size: 0, titleText: 7 },
    });
    expect(settings.format).toBe(DEFAULT_MOVIE_SETTINGS.format);
    expect(settings.size).toBe(DEFAULT_MOVIE_SETTINGS.size);
    expect(settings.quality).toBe(DEFAULT_MOVIE_SETTINGS.quality);
    expect(settings.durationS).toBe(1);
    expect(settings.holdEndS).toBe(10);
    expect(settings.rotation).toEqual({ ...DEFAULT_MOVIE_SETTINGS.rotation, degreesPerSecond: 90 });
    expect(settings.timeline.mode).toBe('calendar');
    expect(settings.timeline.quietGapMin).toBe(1);
    // The viewer's glyph atlas refuses labels above 45 pixels.
    expect(settings.cavers.labelSize).toBe(45);
    expect(settings.cavers.colourBy).toBe('auto');
    expect(settings.cavers.transitionS).toBe(DEFAULT_MOVIE_SETTINGS.cavers.transitionS);
    expect(settings.view.linewidth).toBe(1);
    expect(settings.view.zScale).toBe(0);
    expect(settings.view.shadingMode).toBeNull();
    expect(settings.view.camera).toBe('perspective');
    expect(settings.captions.size).toBe(0.5);
    expect(settings.captions.titleText).toBe('');
  });

  it('holds a GIF to the frame rates it can play, its widest size and its frame count', () => {
    const settings = normaliseMovieSettings({ format: 'gif', size: '1280x720', fps: 30, durationS: 300, holdEndS: 4 });
    expect(MOVIE_GIF_FRAME_RATES).toContain(settings.fps);
    expect(settings.fps).toBe(25);
    // The same shape, as large as a GIF is offered.
    expect(settings.size).toBe('800x450');
    expect(movieSize(settings).width).toBeLessThanOrEqual(MOVIE_GIF_MAX_WIDTH);
    // The still frames are kept; the replay part gives way.
    expect(settings.holdEndS).toBe(4);
    const frames = Math.round(settings.durationS * settings.fps) + Math.round(settings.holdEndS * settings.fps);
    expect(frames).toBe(MOVIE_GIF_MAX_FRAMES);
  });

  it('keeps a 4:3 frame 4:3 when a GIF needs it smaller', () => {
    expect(normaliseMovieSettings({ format: 'gif', size: '1024x768' }).size).toBe('800x600');
  });

  it('lets a video be large and keep its frame rate', () => {
    const settings = normaliseMovieSettings({ format: 'mp4', size: '1920x1080', fps: 30, durationS: 300 });
    expect(settings).toMatchObject({ size: '1920x1080', fps: 30, durationS: 300 });
  });

  it('offers only GIF sizes no wider than a GIF may be', () => {
    for (const size of MOVIE_SIZES) {
      expect(size.gif).toBe(size.width <= MOVIE_GIF_MAX_WIDTH);
      expect(size.id).toBe(`${size.width}x${size.height}`);
    }
  });

  it('lists every layer once, each gated by the getter the viewer names it by', () => {
    const keys = MOVIE_VIEW_LAYERS.map((layer) => layer.key);
    expect(new Set(keys).size).toBe(keys.length);
    for (const layer of MOVIE_VIEW_LAYERS) {
      expect(typeof DEFAULT_MOVIE_SETTINGS.view[layer.key]).toBe('boolean');
      if (layer.has !== null) {
        expect(layer.has).toBe(`has${layer.key[0].toUpperCase()}${layer.key.slice(1)}`);
      }
    }
  });
});
