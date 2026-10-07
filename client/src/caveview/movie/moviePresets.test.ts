// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { GIF_FRAME_RATES, type MovieFormatSupport } from './encode/movieEncoder.ts';
import {
  MOVIE_CHAT_OUTPUT,
  MOVIE_HD_OUTPUT,
  MOVIE_PRESETS,
  movieHdFormat,
  moviePresetOutput,
  withMovieOutput,
} from './moviePresets.ts';
import {
  DEFAULT_MOVIE_SETTINGS,
  MOVIE_GIF_MAX_FRAMES,
  movieSize,
  normaliseMovieSettings,
  type MovieSettings,
} from './movieSettings.ts';

/** Settings a reader has made their own in every group a preset has no business in. */
function chosen(output: Partial<MovieSettings> = {}): MovieSettings {
  return {
    ...DEFAULT_MOVIE_SETTINGS,
    format: 'webm',
    size: '1920x1080',
    fps: 25,
    durationS: 120,
    holdEndS: 4,
    quality: 'low',
    ...output,
    rotation: { enabled: false, mode: 'fullTurn', degreesPerSecond: 11, clockwise: false },
    timeline: { mode: 'together', shortenQuiet: false, quietGapMin: 45 },
    cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, labels: 'initials', labelPlate: false, showOut: false, trails: true },
    view: { ...DEFAULT_MOVIE_SETTINGS.view, HUD: true, grid: true, stationLabels: true, direction: 'plan' },
    captions: { ...DEFAULT_MOVIE_SETTINGS.captions, title: false, titleText: 'Ours', clock: false, note: true },
  };
}

function probeAnswering(webm: boolean, mp4: boolean) {
  return vi.fn(
    async (_size: { width: number; height: number }, _fps: number): Promise<MovieFormatSupport[]> => [
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: webm, codec: webm ? 'vp09' : null },
      { format: 'mp4', supported: mp4, codec: mp4 ? 'avc1' : null },
    ],
  );
}

describe('withMovieOutput', () => {
  it('sets the file and hands every other group back as the object it was given', async () => {
    const before = chosen();
    // Proved on settings that differ from the defaults in each of those groups: against the
    // defaults, a preset that reset a group would look the same as one that left it alone.
    for (const group of ['rotation', 'timeline', 'cavers', 'view', 'captions'] as const) {
      expect(before[group], group).not.toEqual(DEFAULT_MOVIE_SETTINGS[group]);
    }
    const outputs = [MOVIE_CHAT_OUTPUT, await moviePresetOutput('hd', probeAnswering(true, true))];
    expect(outputs.length).toBe(MOVIE_PRESETS.length);

    for (const output of outputs) {
      const after = withMovieOutput(before, output!);
      for (const group of ['rotation', 'timeline', 'cavers', 'view', 'captions'] as const) {
        expect(after[group], group).toBe(before[group]);
      }
      expect(after).toMatchObject(output!);
      // What comes out is settings the dialog would have written itself.
      expect(normaliseMovieSettings(after)).toEqual(after);
    }
  });

  it('leaves what the preset does not name as the reader had it', () => {
    const after = withMovieOutput(chosen(), MOVIE_HD_OUTPUT);
    expect(after).toMatchObject({ format: 'webm', durationS: 120, holdEndS: 4, size: '1280x720', fps: 30, quality: 'high' });
    expect(withMovieOutput(chosen(), MOVIE_CHAT_OUTPUT).holdEndS).toBe(4);
  });

  it('is repaired as any change is: a GIF asked for past its limits is held to them', () => {
    const after = withMovieOutput(chosen({ fps: 30 }), { format: 'gif' });
    expect(after.format).toBe('gif');
    // A GIF is not made at 1920 pixels wide, at 30 frames a second or of 3,700 frames.
    expect(movieSize(after).width).toBeLessThanOrEqual(800);
    expect(GIF_FRAME_RATES).toContain(after.fps);
    expect(GIF_FRAME_RATES).not.toContain(30);
    expect(Math.round(after.durationS * after.fps) + Math.round(after.holdEndS * after.fps)).toBeLessThanOrEqual(
      MOVIE_GIF_MAX_FRAMES,
    );
  });

  it('makes the chat preset a small GIF', () => {
    const after = withMovieOutput(chosen(), MOVIE_CHAT_OUTPUT);
    expect(after).toMatchObject({ format: 'gif', size: '480x270', fps: 10, durationS: 15, quality: 'medium' });
  });
});

describe('movieHdFormat', () => {
  it('asks at the preset’s own size and rate, not at the one in use', async () => {
    const probe = probeAnswering(true, true);
    await movieHdFormat(probe);
    expect(probe).toHaveBeenCalledExactlyOnceWith({ width: 1280, height: 720 }, 30);
  });

  it('takes MP4 where the browser writes it', async () => {
    expect(await movieHdFormat(probeAnswering(true, true))).toBe('mp4');
    expect(await movieHdFormat(probeAnswering(false, true))).toBe('mp4');
    expect(await moviePresetOutput('hd', probeAnswering(true, true))).toEqual({ ...MOVIE_HD_OUTPUT, format: 'mp4' });
  });

  it('falls back to WebM where MP4 is refused', async () => {
    expect(await movieHdFormat(probeAnswering(true, false))).toBe('webm');
    expect(await moviePresetOutput('hd', probeAnswering(true, false))).toEqual({ ...MOVIE_HD_OUTPUT, format: 'webm' });
  });

  it('answers nothing where neither is written, and where the browser cannot be asked', async () => {
    expect(await movieHdFormat(probeAnswering(false, false))).toBeNull();
    expect(await moviePresetOutput('hd', probeAnswering(false, false))).toBeNull();
    const unaskable = vi.fn(async (): Promise<MovieFormatSupport[]> => {
      throw new Error('no encoder to ask');
    });
    expect(await movieHdFormat(unaskable)).toBeNull();
    expect(unaskable).toHaveBeenCalled();
  });
});

describe('moviePresetOutput', () => {
  it('asks nothing of the browser for a GIF', async () => {
    const probe = probeAnswering(false, false);
    expect(await moviePresetOutput('chat', probe)).toBe(MOVIE_CHAT_OUTPUT);
    expect(probe).not.toHaveBeenCalled();
  });
});
