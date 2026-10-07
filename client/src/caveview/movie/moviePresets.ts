// SPDX-License-Identifier: AGPL-3.0-or-later
import { probeMovieFormats } from './encode/movieEncoder.ts';
import { MOVIE_SIZES, normaliseMovieSettings, type MovieSettings } from './movieSettings.ts';

/**
 * The movie's presets: a file for a purpose, in one press.
 *
 * <b>A preset is about the file and nothing else.</b> It sets the format, the frame, the rate, the
 * length and the quality. Who is labelled and how, what of the model shows, and what is written
 * over it are the reader's choices about what the file discloses, made once and on purpose — a
 * preset that quietly turned a caption or a layer back on would undo them. So a preset cannot reach
 * those groups at all: {@link withMovieOutput} is the only way one is applied, and it hands every
 * other group back as the very object it was given.
 */

/** The settings that say what file is written, and nothing about what is drawn in it. */
export type MovieOutput = Pick<MovieSettings, 'format' | 'size' | 'fps' | 'durationS' | 'holdEndS' | 'quality'>;

export type MoviePresetId = 'chat' | 'hd';

/** The presets, in the order they are offered. */
export const MOVIE_PRESETS: readonly MoviePresetId[] = ['chat', 'hd'];

/** A GIF small enough to be sent in a chat: a few megabytes, played in place by whatever shows it. */
export const MOVIE_CHAT_OUTPUT: Partial<MovieOutput> = {
  format: 'gif',
  size: '480x270',
  fps: 10,
  durationS: 15,
  quality: 'medium',
};

/**
 * A high-definition video, less its format: which of the two video formats is written depends on
 * what the browser can encode, and is asked when the preset is pressed.
 */
export const MOVIE_HD_OUTPUT = { size: '1280x720', fps: 30, quality: 'high' } as const satisfies Partial<MovieOutput>;

/**
 * The settings with this output, repaired the way every change to them is — a GIF held to its
 * rates, its widest frame and its frame count — and with every group that is not about the file
 * left exactly as it was.
 */
export function withMovieOutput(settings: MovieSettings, output: Partial<MovieOutput>): MovieSettings {
  const { format, size, fps, durationS, holdEndS, quality } = normaliseMovieSettings({ ...settings, ...output });
  return { ...settings, format, size, fps, durationS, holdEndS, quality };
}

/**
 * The video format a high-definition movie is written in by this browser: MP4, which plays
 * everywhere, where it can be encoded at that size and rate; WebM where only that can; null where
 * neither can — asked of the encoder at the preset's own size and rate, since a browser that
 * encodes a small video may still refuse a large one. A browser that cannot even be asked writes
 * no video.
 */
export async function movieHdFormat(probe: typeof probeMovieFormats = probeMovieFormats): Promise<'mp4' | 'webm' | null> {
  const size = MOVIE_SIZES.find((entry) => entry.id === MOVIE_HD_OUTPUT.size);
  if (size === undefined) {
    return null;
  }
  try {
    const answer = await probe({ width: size.width, height: size.height }, MOVIE_HD_OUTPUT.fps);
    const writes = (format: 'mp4' | 'webm') => answer.some((entry) => entry.format === format && entry.supported);
    return writes('mp4') ? 'mp4' : writes('webm') ? 'webm' : null;
  } catch {
    return null;
  }
}

/**
 * What a preset sets, in this browser; null when the browser can write nothing of the kind, and
 * the settings are then to be left as they are rather than moved half-way to a file it cannot make.
 */
export async function moviePresetOutput(
  id: MoviePresetId,
  probe: typeof probeMovieFormats = probeMovieFormats,
): Promise<Partial<MovieOutput> | null> {
  if (id === 'chat') {
    return MOVIE_CHAT_OUTPUT;
  }
  const format = await movieHdFormat(probe);
  return format === null ? null : { ...MOVIE_HD_OUTPUT, format };
}
