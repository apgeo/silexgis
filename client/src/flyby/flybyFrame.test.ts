// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { frameArea, frameBoxFor, placeFrame } from './flybyFrame.ts';

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

describe('where a framed scene is shown', () => {
  const view = { width: 1200, height: 700 };

  it('is the view less the panel down its left side and the bar across its foot', () => {
    const panel = { left: 8, top: 8, width: 320, height: 612 };
    const bar = { left: 320, top: 620, width: 560, height: 48 };
    expect(frameArea(view, [panel, bar])).toEqual({ left: 336, top: 0, width: 864, height: 612 });
  });

  it('is above a sheet across the foot of a narrow view', () => {
    const sheet = { left: 8, top: 300, width: 384, height: 320 };
    expect(frameArea({ width: 400, height: 700 }, [sheet])).toEqual({ left: 0, top: 0, width: 400, height: 292 });
  });

  it('ignores what is not there, and keeps the whole view when nothing would be left', () => {
    expect(frameArea(view, [{ left: 0, top: 0, width: 0, height: 0 }])).toEqual({ left: 0, top: 0, width: 1200, height: 700 });
    expect(frameArea(view, [{ left: 0, top: 240, width: 1200, height: 460 }, { left: 0, top: 0, width: 590, height: 700 }])).toMatchObject({
      left: 598,
    });
    expect(frameArea(view, [{ left: 0, top: 239, width: 100, height: 100 }, { left: 0, top: 250, width: 10, height: 10 }]).height).toBe(231);
    expect(frameArea({ width: 100, height: 100 }, [{ left: 0, top: 34, width: 100, height: 66 }, { left: 0, top: 40, width: 100, height: 60 }])).toEqual({
      left: 0,
      top: 0,
      width: 100,
      height: 26,
    });
  });

  it('is the largest box of the frame\'s shape, centred, at whole pixels of the page', () => {
    const area = { left: 336, top: 0, width: 864, height: 612 };
    // Nine by sixteen, limited by the height: 38 units of 9 by 16.
    expect(placeFrame({ width: 1080, height: 1920 }, area)).toEqual({
      left: 597,
      top: 2,
      width: 342,
      height: 608,
      scale: 1080 / 342,
    });
    const wide = placeFrame({ width: 1280, height: 720 }, area)!;
    expect(wide.width * 720).toBe(wide.height * 1280);
    expect(wide.left).toBe(336);
    expect(placeFrame({ width: 1279, height: 721 }, { left: 0, top: 0, width: 300, height: 300 })).toBeUndefined();
  });
});
