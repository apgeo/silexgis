// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { frameBoxFor } from './flybyFrame.ts';

describe('the box a scene is put in to draw a frame', () => {
  it('is the largest of exactly the frame\'s shape that fits', () => {
    // Sixteen by nine into 900 by 600: 56 units of 16 by 9.
    expect(frameBoxFor(1920, 1080, { width: 900, height: 600 })).toEqual({ width: 896, height: 504, scale: 1920 / 896 });
    // A vertical frame is limited by the height.
    expect(frameBoxFor(1080, 1920, { width: 900, height: 600 })).toEqual({ width: 333, height: 592, scale: 1080 / 333 });
    expect(frameBoxFor(1080, 1350, { width: 900, height: 600 })).toEqual({ width: 480, height: 600, scale: 2.25 });
  });

  it('keeps the shape exact, so the picture is neither a pixel short nor refused', () => {
    for (const [width, height] of [
      [320, 180],
      [1280, 720],
      [1920, 1080],
      [3840, 2160],
      [1024, 768],
      [1080, 1080],
      [864, 1080],
      [810, 1080],
      [1080, 1440],
      [450, 800],
    ]) {
      const box = frameBoxFor(width, height, { width: 1173, height: 731 })!;
      expect(box.width * height).toBe(box.height * width);
      expect(Number.isInteger(box.width) && Number.isInteger(box.height)).toBe(true);
      expect(box.width).toBeLessThanOrEqual(1173);
      expect(box.height).toBeLessThanOrEqual(731);
      expect(Math.round(box.width * box.scale)).toBe(width);
      expect(Math.round(box.height * box.scale)).toBe(height);
    }
  });

  it('draws smaller than the page when the frame is smaller than the space', () => {
    expect(frameBoxFor(320, 180, { width: 1280, height: 720 })).toEqual({ width: 1280, height: 720, scale: 0.25 });
  });

  it('has no box when even the smallest of that shape does not fit, or the frame is not a size', () => {
    // 683 by 384 is the smallest box shaped like 1366 by 768.
    expect(frameBoxFor(1366, 768, { width: 600, height: 600 })).toBeUndefined();
    expect(frameBoxFor(1366, 768, { width: 700, height: 400 })).toEqual({ width: 683, height: 384, scale: 2 });
    expect(frameBoxFor(0, 720, { width: 600, height: 600 })).toBeUndefined();
    expect(frameBoxFor(1280.5, 720, { width: 600, height: 600 })).toBeUndefined();
  });
});
