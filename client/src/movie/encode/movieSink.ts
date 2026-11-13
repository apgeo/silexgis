// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Somewhere a movie's file can be written as it is encoded, instead of being held whole in memory
 * until it is finished.
 *
 * A video file cannot simply be appended to: both containers written here keep an index in front of
 * the frames, and the index is only known once the last frame is. So the frames are written where
 * they will end up, past a front whose size is worked out beforehand, and the front is written into
 * the gap at the very end. That is why a sink writes at a position rather than at its end.
 *
 * Whoever opened the sink ends it: `close` once the whole file is there, `abort` in every other
 * case. An encoder only ever writes to it.
 */
export interface MovieSink {
  /** Writes `data` at `position`, counted in bytes from the start of the file. */
  write(position: number, data: Uint8Array): Promise<void>;
  /** Makes what was written the file. Nothing may be written afterwards. */
  close(): Promise<void>;
  /**
   * Gives the file up and removes what was written — also when it had already been closed, since an
   * export can be cancelled in the moment after its last byte. Safe to call more than once.
   */
  abort(): Promise<void>;
}

/**
 * What an encoder is told of the movie beforehand, which is what lets it work out the size of a
 * file's front before the frames exist: how many frames there will be, when each is shown (in
 * microseconds; `timestamp(count)` is where the last one ends) and which it asks to be keyframes.
 */
export interface MovieFramePlan {
  count: number;
  timestamp(index: number): number;
  key(index: number): boolean;
}

const MISMATCH = 'MovieSinkMismatch';

/**
 * The file being written to a sink turned out not to have the layout its front was sized for — the
 * browser's encoder made a keyframe nobody asked for, say, or the file outgrew a size field.
 *
 * Nothing can be moved once it is on disk, and a file padded to fit would not be the file the
 * in-memory writer makes. So the writer stops instead, and the movie is made again in memory.
 */
export class MovieSinkMismatch extends Error {
  constructor(detail: string) {
    super(`the file could not be written as it was encoded: ${detail}`);
    this.name = MISMATCH;
  }
}

export function isMovieSinkMismatch(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === MISMATCH;
}

/** The most bytes gathered into one write, so that a sink is not called once for every frame. */
export const SINK_WRITE_BYTES = 1024 * 1024;

/**
 * Writes `parts` one after another from `position`, small ones gathered into writes of about
 * `SINK_WRITE_BYTES`. Answers the position after the last byte.
 */
export async function writeParts(sink: MovieSink, position: number, parts: readonly Uint8Array[]): Promise<number> {
  let at = position;
  let gathered: Uint8Array[] = [];
  let size = 0;
  const flush = async () => {
    if (size === 0) return;
    await sink.write(at, gathered.length === 1 ? gathered[0] : join(gathered, size));
    at += size;
    gathered = [];
    size = 0;
  };
  for (const part of parts) {
    if (part.byteLength === 0) continue;
    if (size > 0 && size + part.byteLength > SINK_WRITE_BYTES) await flush();
    gathered.push(part);
    size += part.byteLength;
    if (size >= SINK_WRITE_BYTES) await flush();
  }
  await flush();
  return at;
}

function join(parts: readonly Uint8Array[], size: number): Uint8Array {
  const out = new Uint8Array(size);
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.byteLength;
  }
  return out;
}
