// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MovieSink } from './movieSink.ts';

/**
 * A sink that keeps what is written to it in memory, at the positions it was written to.
 *
 * It is what the writers are checked against: a file written through a sink has to be, byte for
 * byte, the file the in-memory writer makes of the same frames, and this is where the first of the
 * two is read back from. It also keeps count of what was done to it, so a test can say that a
 * cancelled export left its sink aborted.
 */
export class MemoryMovieSink implements MovieSink {
  state: 'open' | 'closed' | 'aborted' = 'open';
  /** Every write, in the order it was made. */
  readonly writes: { position: number; size: number }[] = [];
  private buffer = new Uint8Array(0);
  private length = 0;

  write(position: number, data: Uint8Array): Promise<void> {
    if (this.state !== 'open') return Promise.reject(new Error(`the sink is ${this.state}`));
    const end = position + data.byteLength;
    if (end > this.buffer.byteLength) {
      const grown = new Uint8Array(Math.max(end, this.buffer.byteLength * 2));
      grown.set(this.buffer.subarray(0, this.length));
      this.buffer = grown;
    }
    this.buffer.set(data, position);
    this.length = Math.max(this.length, end);
    this.writes.push({ position, size: data.byteLength });
    return Promise.resolve();
  }

  close(): Promise<void> {
    if (this.state !== 'open') return Promise.reject(new Error(`the sink is ${this.state}`));
    this.state = 'closed';
    return Promise.resolve();
  }

  abort(): Promise<void> {
    this.state = 'aborted';
    this.buffer = new Uint8Array(0);
    this.length = 0;
    return Promise.resolve();
  }

  /** The file as it stands. A stretch nothing was written to reads as zeros, as it would on disk. */
  bytes(): Uint8Array {
    return this.buffer.slice(0, this.length);
  }
}
