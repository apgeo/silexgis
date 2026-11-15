// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_FLYBY_MOVIE_SETTINGS,
  flybyDraftSize,
  flybyFrameSchedule,
  flybyMovieOutput,
  flybyMovieRefusal,
  normaliseFlybyMovieSettings,
} from './flybyMovie.ts';

describe('the settings a flyby movie is made with', () => {
  it('start as a high-definition video, taken carefully', () => {
    expect(normaliseFlybyMovieSettings(undefined)).toEqual(DEFAULT_FLYBY_MOVIE_SETTINGS);
  });

  it('keep what can be used of what was stored, field by field', () => {
    expect(
      normaliseFlybyMovieSettings({ format: 'webm', size: 'huge', fps: 29, quality: 'best', level: 'fineLarge', draft: true, holdEndS: 99 }),
    ).toEqual({ format: 'webm', size: '1280x720', fps: 30, quality: 'high', level: 'fineLarge', draft: true, holdEndS: 10 });
  });

  it('hold a GIF to the sizes and rates a GIF may have, keeping the frame\'s shape', () => {
    expect(normaliseFlybyMovieSettings({ format: 'gif', size: '1080x1920', fps: 30 })).toMatchObject({
      size: '720x1280',
      fps: 25,
    });
  });
});

describe('a draft', () => {
  it('is about half the size, of exactly the same shape, with both sides even', () => {
    expect(flybyDraftSize(1280, 720)).toEqual({ width: 640, height: 360 });
    expect(flybyDraftSize(1080, 1920)).toEqual({ width: 540, height: 960 });
    expect(flybyDraftSize(1080, 1080)).toEqual({ width: 540, height: 540 });
    // Half would be 240 × 135, which no video encoder takes.
    expect(flybyDraftSize(480, 270)).toEqual({ width: 224, height: 126 });
    for (const [width, height] of [
      [1080, 1350],
      [800, 450],
      [320, 180],
      [600, 800],
    ]) {
      const draft = flybyDraftSize(width, height);
      expect(draft.width % 2, `${width}x${height}`).toBe(0);
      expect(draft.height % 2, `${width}x${height}`).toBe(0);
      expect(draft.width * height).toBe(draft.height * width);
      expect(draft.width).toBeLessThanOrEqual(width / 2);
    }
  });

  it('is taken quickly at eight frames a second, or at a GIF\'s slowest rate', () => {
    const settings = { ...DEFAULT_FLYBY_MOVIE_SETTINGS, draft: true };
    expect(flybyMovieOutput(settings)).toEqual({
      format: 'mp4',
      width: 640,
      height: 360,
      fps: 8,
      quality: 'high',
      level: 'quick',
    });
    expect(flybyMovieOutput({ ...settings, format: 'gif', size: '640x360', fps: 25 })).toMatchObject({ fps: 10, width: 320 });
  });
});

describe('the frames of a flyby movie', () => {
  it('are a frame for every tick of the clock, and the end held for as long as asked', () => {
    const schedule = flybyFrameSchedule(10, 4, undefined, 1);
    expect(schedule).toMatchObject({ from: 0, to: 10, moving: 40, held: 4, count: 44 });
    expect(schedule.at(0)).toBe(0);
    expect(schedule.at(1)).toBe(0.25);
    expect(schedule.at(39)).toBe(9.75);
    expect(schedule.at(40)).toBe(10);
    expect(schedule.at(43)).toBe(10);
  });

  it('are of the stretch alone when one is asked for, held within the flyby', () => {
    const schedule = flybyFrameSchedule(10, 2, { from: 4, to: 99 });
    expect(schedule).toMatchObject({ from: 4, to: 10, count: 12 });
    expect(schedule.at(0)).toBe(4);
    expect(flybyFrameSchedule(10, 2, { from: 6, to: 3 })).toMatchObject({ from: 6, to: 6, count: 1 });
  });

  it('are refused as a GIF past the most frames a GIF is made of', () => {
    const output = flybyMovieOutput({ ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'gif', size: '480x270', fps: 25 });
    expect(flybyMovieRefusal(output, flybyFrameSchedule(20, 25))).toBeNull();
    expect(flybyMovieRefusal(output, flybyFrameSchedule(30, 25))).toEqual({ kind: 'gifTooLong', frames: 750, max: 600 });
  });
});
