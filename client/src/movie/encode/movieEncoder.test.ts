// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GIF_FRAME_RATES, MOVIE_EXTENSION, MOVIE_MIME, openMovieEncoder, probeMovieFormats } from './movieEncoder.ts';

describe('movie encoders', () => {
  it('names each format’s type and extension, and the GIF rates whose frames last whole centiseconds', () => {
    expect(MOVIE_MIME).toEqual({ gif: 'image/gif', webm: 'video/webm', mp4: 'video/mp4' });
    expect(MOVIE_EXTENSION).toEqual({ gif: 'gif', webm: 'webm', mp4: 'mp4' });
    expect(GIF_FRAME_RATES).toEqual([10, 12.5, 20, 25]);
    for (const fps of GIF_FRAME_RATES) expect(Number.isInteger(100 / fps)).toBe(true);
  });

  it('always offers GIF', async () => {
    const formats = await probeMovieFormats({ width: 480, height: 360 }, 10);
    expect(formats.find((f) => f.format === 'gif')).toEqual({ format: 'gif', supported: true, codec: null });
    expect(formats.map((f) => f.format).sort()).toEqual(['gif', 'mp4', 'webm']);
  });

  it('opens a GIF encoder that reads a canvas and writes a GIF', async () => {
    const encoder = await openMovieEncoder('gif', { width: 16, height: 12, fps: 10, quality: 'low' });
    expect(encoder.format).toBe('gif');
    expect(encoder.samplesWanted).toBeGreaterThan(0);
    const canvas = document.createElement('canvas');
    canvas.width = 16;
    canvas.height = 12;
    await encoder.addFrame(canvas, 0);
    await encoder.addFrame(canvas, 1);
    const blob = await encoder.finish();
    expect(blob.type).toBe('image/gif');
    const bytes = new Uint8Array(await blob.arrayBuffer());
    expect(String.fromCharCode(...bytes.subarray(0, 6))).toBe('GIF89a');
    expect(bytes.at(-1)).toBe(0x3b);
  });

  it('says plainly that a video format cannot be opened where the browser has no video encoder', async () => {
    // The test environment has no WebCodecs, like a page outside a secure context.
    expect(typeof globalThis.VideoEncoder).toBe('undefined');
    const options = { width: 640, height: 360, fps: 25, quality: 'medium' } as const;
    await expect(openMovieEncoder('webm', options)).rejects.toThrow(/^WEBM encoder, open: this browser cannot encode it/);
    await expect(openMovieEncoder('mp4', options)).rejects.toThrow(/^MP4 encoder, open: this browser cannot encode it/);
  });
});
