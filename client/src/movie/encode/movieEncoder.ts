// SPDX-License-Identifier: AGPL-3.0-or-later
import { openGifEncoder } from './gif/gifEncoder.ts';
import { chooseVideoCodec, openVideoEncoder } from './video/videoEncoder.ts';
import type { MovieEncoder, MovieEncoderOptions, MovieFormat, MovieFormatSupport } from './movieFormats.ts';

/**
 * The one door to the movie encoders: which formats this browser can write at a given size, and an
 * encoder for one of them.
 *
 * GIF is written entirely by this application's own code and is always available. The video
 * formats depend on the browser's WebCodecs encoder, which is asked rather than assumed.
 */

export {
  GIF_FRAME_RATES,
  MOVIE_EXTENSION,
  MOVIE_MIME,
  type MovieEncoder,
  type MovieEncoderOptions,
  type MovieFormat,
  type MovieFormatSupport,
  type MovieQuality,
} from './movieFormats.ts';

/** GIF is always supported; WebM/MP4 are probed with VideoEncoder.isConfigSupported at this size/fps. */
export async function probeMovieFormats(
  size: { width: number; height: number },
  fps: number,
): Promise<MovieFormatSupport[]> {
  const video = async (format: 'webm' | 'mp4'): Promise<MovieFormatSupport> => {
    const codec = await chooseVideoCodec(format, size.width, size.height, fps);
    return { format, supported: codec !== null, codec };
  };
  const [webm, mp4] = await Promise.all([video('webm'), video('mp4')]);
  return [{ format: 'gif', supported: true, codec: null }, webm, mp4];
}

export async function openMovieEncoder(format: MovieFormat, options: MovieEncoderOptions): Promise<MovieEncoder> {
  switch (format) {
    case 'gif':
      return openGifEncoder(options);
    case 'webm':
    case 'mp4':
      return openVideoEncoder(format, options);
    default:
      throw new Error(`unknown movie format '${String(format)}'`);
  }
}
