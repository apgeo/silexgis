// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GifFrameResult } from './gifFrame.ts';
import { lzwEncode } from './lzw.ts';
import type { GifColorMap } from './quantize.ts';

/**
 * The GIF89a container around compressed frames.
 *
 * Layout: the header and logical screen descriptor, the one global colour table, a NETSCAPE2.0
 * application extension asking for an endless loop, then per image a graphic control extension
 * (delay, disposal "do not dispose", the transparent index) and an image descriptor with its LZW
 * data, and the trailer.
 *
 * <b>Delays are whole centiseconds, and they add up exactly.</b> A frame that starts at movie
 * frame `a` and lasts until frame `b` is given `round(b·100/fps) − round(a·100/fps)`, so however
 * many frames there are, the delays sum to `round(n·100/fps)` — at 12.5 fps every delay is 8, at a
 * rate that does not divide 100 they alternate, and the movie never drifts from its own clock.
 *
 * <b>Identical frames become one longer frame.</b> A frame that changes nothing is not written; the
 * image before it is held for longer instead. Because the delay is only known once the next frame
 * has been seen, one image is always held back until the next one (or the end) arrives.
 *
 * Frames must be handed over in order, each exactly once.
 */

/** GIF delays are 16-bit centiseconds. */
const MAX_DELAY = 0xffff;

/** When frame `index` of a movie at `fps` starts, in whole centiseconds. */
export function gifCentisecondsAt(index: number, fps: number): number {
  return Math.round((index * 100) / fps);
}

interface Pending {
  start: number;
  end: number;
  frame: GifFrameResult;
  transparent: boolean;
}

export class GifWriter {
  private readonly parts: Uint8Array[] = [];
  private pending: Pending | null = null;
  private nextIndex = 0;
  private images = 0;
  private readonly map: GifColorMap;
  private readonly fps: number;

  constructor(options: { width: number; height: number; fps: number; map: GifColorMap }) {
    const { width, height, fps, map } = options;
    this.map = map;
    this.fps = fps;
    const bits = Math.log2(map.tableSize);
    const head = new ByteBuffer(13 + map.tableSize * 3 + 19);
    head.ascii('GIF89a');
    head.u16(width);
    head.u16(height);
    // Global table present, 8 bits of colour resolution, unsorted, and the table's size.
    head.u8(0x80 | 0x70 | (bits - 1));
    head.u8(0); // background colour index — nothing is ever disposed to it
    head.u8(0); // no pixel aspect ratio
    head.bytes(map.table);
    // NETSCAPE2.0: one sub-block of three bytes, id 1, loop count 0 = for ever.
    head.bytes([0x21, 0xff, 0x0b]);
    head.ascii('NETSCAPE2.0');
    head.bytes([0x03, 0x01, 0x00, 0x00, 0x00]);
    this.parts.push(head.done());
  }

  /** Images written so far (merged frames count once). */
  get imageCount(): number {
    return this.images + (this.pending ? 1 : 0);
  }

  /** Takes the next frame, `result.seq` being its index in the movie. */
  add(result: GifFrameResult): void {
    if (result.seq !== this.nextIndex) {
      throw new Error(`frame ${result.seq} arrived where frame ${this.nextIndex} was expected`);
    }
    this.nextIndex++;
    const p = this.pending;
    if (p && !result.changed) {
      if (this.delay(p.start, p.end + 1) <= MAX_DELAY) {
        p.end++;
        return;
      }
      // Too long a hold for one delay: carry on with a single transparent pixel, which shows
      // nothing new and restarts the count.
      this.flush();
      this.pending = {
        start: result.seq,
        end: result.seq + 1,
        frame: { ...result, changed: true, x: 0, y: 0, width: 1, height: 1, data: lzwEncode(Uint8Array.of(this.map.transparentIndex), this.map.minCodeSize) },
        transparent: true,
      };
      return;
    }
    if (!p && !result.changed) throw new Error('the first frame cannot be empty');
    this.flush();
    // The first image covers the whole screen and needs no transparency; every later one is drawn
    // over its predecessor.
    this.pending = { start: result.seq, end: result.seq + 1, frame: result, transparent: result.seq > 0 };
  }

  /** The finished file's parts, in order. */
  finish(): Uint8Array[] {
    if (!this.pending) throw new Error('there are no frames');
    this.flush();
    this.parts.push(Uint8Array.of(0x3b));
    return this.parts;
  }

  private delay(start: number, end: number): number {
    return gifCentisecondsAt(end, this.fps) - gifCentisecondsAt(start, this.fps);
  }

  private flush(): void {
    const p = this.pending;
    if (!p) return;
    this.pending = null;
    const { frame } = p;
    const data = frame.data;
    if (!data) throw new Error(`frame ${frame.seq} has no image data`);
    const b = new ByteBuffer(8 + 10);
    // Graphic control extension: disposal 1 ("do not dispose"), no user input, transparency flag.
    b.bytes([0x21, 0xf9, 0x04, (1 << 2) | (p.transparent ? 1 : 0)]);
    b.u16(this.delay(p.start, p.end));
    b.u8(p.transparent ? this.map.transparentIndex : 0);
    b.u8(0);
    // Image descriptor: position, size, no local table, not interlaced.
    b.u8(0x2c);
    b.u16(frame.x);
    b.u16(frame.y);
    b.u16(frame.width);
    b.u16(frame.height);
    b.u8(0);
    this.parts.push(b.done(), data);
    this.images++;
  }
}

class ByteBuffer {
  private readonly buf: Uint8Array;
  private pos = 0;
  constructor(size: number) {
    this.buf = new Uint8Array(size);
  }
  u8(v: number) {
    this.buf[this.pos++] = v;
  }
  u16(v: number) {
    this.buf[this.pos++] = v & 0xff;
    this.buf[this.pos++] = (v >> 8) & 0xff;
  }
  ascii(s: string) {
    for (let i = 0; i < s.length; i++) this.buf[this.pos++] = s.charCodeAt(i);
  }
  bytes(a: ArrayLike<number>) {
    this.buf.set(a, this.pos);
    this.pos += a.length;
  }
  done(): Uint8Array {
    return this.buf.subarray(0, this.pos);
  }
}
