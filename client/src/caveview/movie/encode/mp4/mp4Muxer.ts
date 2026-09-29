// SPDX-License-Identifier: AGPL-3.0-or-later
import { checkFrameOrder, type EncodedVideoFrame } from '../video/encodedFrame.ts';

/**
 * An MP4 (ISO base media) file for one H.264 track, written whole once every frame is known.
 *
 * The index (`moov`) comes before the frames (`mdat`) — a "fast start" file, which a player or a
 * messaging app can begin showing before it has the whole file. That means the frame offsets the
 * index holds depend on the index's own size; they are written after it is measured, and the size
 * does not change because the offset table's width is decided from the data's end first.
 *
 * Each frame is its own chunk, so the chunk offset table points at every frame and the sample-to-
 * chunk table is a single "one sample per chunk" entry. Frames are stored in the order they are
 * shown, which is also decoding order for an encoder that makes no B-frames; the composition
 * offset table is therefore not written, and frames that arrive reordered are refused.
 */

export interface Mp4Track {
  width: number;
  height: number;
  fps: number;
  /** The avcC record the encoder reported (its decoder configuration's description). */
  avcC: Uint8Array;
}

// The movie's own clock counts milliseconds; the track's counts in thousandths of a frame, so
// that a frame of any rate given to three decimals lasts a whole number of ticks.
const MOVIE_TIMESCALE = 1000;
const TICKS_PER_FRAME = 1000;

const UINT32_MAX = 0xffffffff;

export function mp4MediaTimescale(fps: number): number {
  return Math.round(fps * TICKS_PER_FRAME);
}

/** The whole MP4 file, as parts to be joined in order. */
export function muxMp4(track: Mp4Track, frames: readonly EncodedVideoFrame[]): Uint8Array[] {
  checkFrameOrder(frames);
  if (track.avcC.byteLength < 7) throw new Error('the H.264 decoder configuration is missing or too short');
  const timescale = mp4MediaTimescale(track.fps);
  const ticks = (us: number) => Math.round((us * timescale) / 1e6);

  // A frame's duration is the time to the next frame's start, so rounding never adds up; the last
  // frame keeps its own.
  const durations = frames.map((f, i) =>
    i + 1 < frames.length ? ticks(frames[i + 1].timestamp) - ticks(f.timestamp) : ticks(f.duration),
  );
  const mediaDuration = durations.reduce((a, b) => a + b, 0);
  const movieDuration = Math.round((mediaDuration * MOVIE_TIMESCALE) / timescale);

  const dataSize = frames.reduce((n, f) => n + f.data.byteLength, 0);
  const ftyp = box('ftyp', [fourCC('isom'), u32(0x200), fourCC('isom'), fourCC('iso2'), fourCC('avc1'), fourCC('mp41')]);
  const mdatHeader = mdatHeaderFor(dataSize);

  // Measure the index with placeholder offsets, then decide the offset width from where the data
  // would end with the narrower table; a wider table only moves the data further, never back.
  const build = (offsets: readonly number[], wide: boolean) =>
    moov(track, frames, durations, timescale, mediaDuration, movieDuration, offsets, wide);
  const zeros = frames.map(() => 0);
  let wide = false;
  let moovSize = byteLength(build(zeros, false));
  if (ftyp.byteLength + moovSize + mdatHeader.byteLength + dataSize > UINT32_MAX) {
    wide = true;
    moovSize = byteLength(build(zeros, true));
  }
  const offsets: number[] = [];
  let at = ftyp.byteLength + moovSize + mdatHeader.byteLength;
  for (const f of frames) {
    offsets.push(at);
    at += f.data.byteLength;
  }
  const index = build(offsets, wide);
  if (byteLength(index) !== moovSize) throw new Error('the index changed size once its offsets were written');
  return [ftyp, ...index, mdatHeader, ...frames.map((f) => f.data)];
}

function moov(
  track: Mp4Track,
  frames: readonly EncodedVideoFrame[],
  durations: readonly number[],
  timescale: number,
  mediaDuration: number,
  movieDuration: number,
  offsets: readonly number[],
  wide: boolean,
): Uint8Array[] {
  const stbl = container('stbl', [
    box('stsd', [fullHeader(0, 0), u32(1), avc1(track)]),
    box('stts', [fullHeader(0, 0), ...timeToSample(durations)]),
    box('stss', [fullHeader(0, 0), u32(frames.filter((f) => f.key).length), ...syncSamples(frames)]),
    box('stsc', [fullHeader(0, 0), u32(1), u32(1), u32(1), u32(1)]),
    box('stsz', [fullHeader(0, 0), u32(0), u32(frames.length), ...frames.map((f) => u32(f.data.byteLength))]),
    wide
      ? box('co64', [fullHeader(0, 0), u32(offsets.length), ...offsets.map(u64)])
      : box('stco', [fullHeader(0, 0), u32(offsets.length), ...offsets.map(u32)]),
  ]);
  const minf = container('minf', [
    box('vmhd', [fullHeader(0, 1), u16(0), u16(0), u16(0), u16(0)]),
    ...container('dinf', [box('dref', [fullHeader(0, 0), u32(1), box('url ', [fullHeader(0, 1)])])]),
    ...stbl,
  ]);
  const mdia = container('mdia', [
    box('mdhd', [fullHeader(0, 0), u32(0), u32(0), u32(timescale), u32(mediaDuration), u16(0x55c4), u16(0)]), // 'und'
    box('hdlr', [fullHeader(0, 0), u32(0), fourCC('vide'), u32(0), u32(0), u32(0), cString('VideoHandler')]),
    ...minf,
  ]);
  const trak = container('trak', [
    box('tkhd', [
      fullHeader(0, 0x3), // enabled, in the movie
      u32(0),
      u32(0),
      u32(1), // track id
      u32(0),
      u32(movieDuration),
      u32(0),
      u32(0),
      u16(0), // layer
      u16(0), // alternate group
      u16(0), // volume: a video track has none
      u16(0),
      matrix(),
      u32(track.width * 0x10000),
      u32(track.height * 0x10000),
    ]),
    ...mdia,
  ]);
  return container('moov', [
    box('mvhd', [
      fullHeader(0, 0),
      u32(0),
      u32(0),
      u32(MOVIE_TIMESCALE),
      u32(movieDuration),
      u32(0x00010000), // rate 1.0
      u16(0x0100), // volume 1.0
      new Uint8Array(10),
      matrix(),
      new Uint8Array(24),
      u32(2), // next track id
    ]),
    ...trak,
  ]);
}

function avc1(track: Mp4Track): Uint8Array {
  const compressorName = new Uint8Array(32); // a length-prefixed name, left empty
  return box('avc1', [
    new Uint8Array(6),
    u16(1), // data reference index
    new Uint8Array(16),
    u16(track.width),
    u16(track.height),
    u32(0x00480000), // 72 dpi
    u32(0x00480000),
    u32(0),
    u16(1), // frames per sample
    compressorName,
    u16(0x0018), // colour, no alpha
    u16(0xffff),
    box('avcC', [track.avcC]),
  ]);
}

// Consecutive frames of the same length share one entry, so a constant rate is one entry.
function timeToSample(durations: readonly number[]): Uint8Array[] {
  const runs: [number, number][] = [];
  for (const d of durations) {
    const last = runs[runs.length - 1];
    if (last && last[1] === d) last[0]++;
    else runs.push([1, d]);
  }
  return [u32(runs.length), ...runs.flatMap(([count, delta]) => [u32(count), u32(delta)])];
}

function syncSamples(frames: readonly EncodedVideoFrame[]): Uint8Array[] {
  const out: Uint8Array[] = [];
  frames.forEach((f, i) => {
    if (f.key) out.push(u32(i + 1)); // sample numbers count from one
  });
  return out;
}

function mdatHeaderFor(dataSize: number): Uint8Array {
  if (8 + dataSize <= UINT32_MAX) return concat([u32(8 + dataSize), fourCC('mdat')]);
  // Past four gigabytes the size moves to a 64-bit field after the type, flagged by a size of 1.
  return concat([u32(1), fourCC('mdat'), u64(16 + dataSize)]);
}

function matrix(): Uint8Array {
  return concat([0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000].map(u32));
}

function fullHeader(version: number, flags: number): Uint8Array {
  return u32(((version & 0xff) << 24) | (flags & 0xffffff));
}

function box(type: string, body: readonly Uint8Array[]): Uint8Array {
  const size = 8 + byteLength(body);
  return concat([u32(size), fourCC(type), ...body]);
}

// A box of boxes, kept as parts so its children are not copied again.
function container(type: string, children: readonly Uint8Array[]): Uint8Array[] {
  return [concat([u32(8 + byteLength(children)), fourCC(type)]), ...children];
}

function u16(v: number): Uint8Array {
  const b = new Uint8Array(2);
  new DataView(b.buffer).setUint16(0, v);
  return b;
}

function u32(v: number): Uint8Array {
  if (!Number.isInteger(v) || v < 0 || v > UINT32_MAX) throw new Error(`${v} does not fit in 32 bits`);
  const b = new Uint8Array(4);
  new DataView(b.buffer).setUint32(0, v);
  return b;
}

function u64(v: number): Uint8Array {
  const b = new Uint8Array(8);
  new DataView(b.buffer).setBigUint64(0, BigInt(v));
  return b;
}

function fourCC(type: string): Uint8Array {
  if (type.length !== 4) throw new Error(`'${type}' is not a box type`);
  return Uint8Array.from(type, (c) => c.charCodeAt(0));
}

function cString(s: string): Uint8Array {
  return concat([new TextEncoder().encode(s), new Uint8Array(1)]);
}

function byteLength(parts: readonly Uint8Array[]): number {
  let n = 0;
  for (const p of parts) n += p.byteLength;
  return n;
}

function concat(parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(byteLength(parts));
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.byteLength;
  }
  return out;
}
