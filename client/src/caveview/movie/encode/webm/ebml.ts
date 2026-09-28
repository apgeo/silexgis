// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The few pieces of EBML — the binary layout Matroska and WebM are written in — that a WebM writer
 * needs. An element is its id, the size of its body as a variable-length integer, and the body.
 *
 * Elements are returned as lists of byte arrays rather than one joined array, so that a frame's
 * bytes are referenced, never copied, on their way into the file: the lists are only joined once,
 * by the Blob the finished movie becomes.
 */

export type EbmlParts = Uint8Array[];

/** The element ids this writer uses. An id's leading bits are part of it, as the format writes it. */
export const EBML_ID = {
  EBML: 0x1a45dfa3,
  EBMLVersion: 0x4286,
  EBMLReadVersion: 0x42f7,
  EBMLMaxIDLength: 0x42f2,
  EBMLMaxSizeLength: 0x42f3,
  DocType: 0x4282,
  DocTypeVersion: 0x4287,
  DocTypeReadVersion: 0x4285,
  Segment: 0x18538067,
  SeekHead: 0x114d9b74,
  Seek: 0x4dbb,
  SeekID: 0x53ab,
  SeekPosition: 0x53ac,
  Info: 0x1549a966,
  TimecodeScale: 0x2ad7b1,
  Duration: 0x4489,
  MuxingApp: 0x4d80,
  WritingApp: 0x5741,
  Tracks: 0x1654ae6b,
  TrackEntry: 0xae,
  TrackNumber: 0xd7,
  TrackUID: 0x73c5,
  TrackType: 0x83,
  FlagLacing: 0x9c,
  DefaultDuration: 0x23e383,
  CodecID: 0x86,
  CodecPrivate: 0x63a2,
  Video: 0xe0,
  PixelWidth: 0xb0,
  PixelHeight: 0xba,
  Cluster: 0x1f43b675,
  Timecode: 0xe7,
  SimpleBlock: 0xa3,
  Cues: 0x1c53bb6b,
  CuePoint: 0xbb,
  CueTime: 0xb3,
  CueTrackPositions: 0xb7,
  CueTrack: 0xf7,
  CueClusterPosition: 0xf1,
} as const;

// A size field of n bytes carries 7·n bits; the value with every one of them set means "unknown
// size", so the largest size it can say is one less than that.
const MAX_SIZE_BYTES = 8;

function maxSizeIn(bytes: number): number {
  return 2 ** (7 * bytes) - 2;
}

/** The bytes of an element id, which already carries its own length marker. */
export function ebmlId(id: number): Uint8Array {
  if (!Number.isInteger(id) || id < 0x81 || id > 0x1fffffff) throw new Error(`0x${id.toString(16)} is not an EBML id`);
  const length = id > 0xffffff ? 4 : id > 0xffff ? 3 : id > 0xff ? 2 : 1;
  return bigEndian(id, length);
}

/**
 * A size as an EBML variable-length integer: the fewest bytes that hold it, or exactly `width`
 * bytes when asked (so a field whose value is written later keeps the length it was measured with).
 */
export function ebmlSize(size: number, width?: number): Uint8Array {
  if (!Number.isSafeInteger(size) || size < 0) throw new Error(`${size} is not an EBML size`);
  let length = width ?? 1;
  if (width === undefined) while (size > maxSizeIn(length)) length++;
  if (length < 1 || length > MAX_SIZE_BYTES || size > maxSizeIn(length)) {
    throw new Error(`the size ${size} does not fit in ${length} byte${length === 1 ? '' : 's'}`);
  }
  const bytes = bigEndian(size, length);
  bytes[0] |= 0x80 >> (length - 1);
  return bytes;
}

/** An unsigned integer body: the fewest big-endian bytes (at least one), or exactly `width`. */
export function ebmlUint(value: number, width?: number): Uint8Array {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error(`${value} is not an unsigned EBML integer`);
  let length = width ?? 1;
  if (width === undefined) while (length < 8 && value >= 2 ** (8 * length)) length++;
  if (value >= 2 ** (8 * length)) throw new Error(`${value} does not fit in ${length} bytes`);
  return bigEndian(value, length);
}

/** A float body, always eight bytes (a double). */
export function ebmlFloat(value: number): Uint8Array {
  const bytes = new Uint8Array(8);
  new DataView(bytes.buffer).setFloat64(0, value);
  return bytes;
}

/** A string body. The ids and names this writer uses are ASCII, which UTF-8 leaves unchanged. */
export function ebmlString(value: string): Uint8Array {
  return new TextEncoder().encode(value);
}

/** An element: its id, its body's size, then the body. */
export function ebmlElement(id: number, body: Uint8Array | readonly Uint8Array[]): EbmlParts {
  // Tested as a view rather than with instanceof, which fails for arrays made in another realm
  // (a TextEncoder's output can be one).
  const parts = ArrayBuffer.isView(body) ? [body as Uint8Array] : (body as readonly Uint8Array[]);
  return [concat([ebmlId(id), ebmlSize(byteLength(parts))]), ...parts];
}

export function ebmlUintElement(id: number, value: number, width?: number): EbmlParts {
  return ebmlElement(id, ebmlUint(value, width));
}

export function byteLength(parts: readonly Uint8Array[]): number {
  let n = 0;
  for (const p of parts) n += p.byteLength;
  return n;
}

export function concat(parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(byteLength(parts));
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.byteLength;
  }
  return out;
}

function bigEndian(value: number, length: number): Uint8Array {
  const bytes = new Uint8Array(length);
  let v = value;
  for (let i = length - 1; i >= 0; i--) {
    bytes[i] = v % 256;
    v = Math.floor(v / 256);
  }
  return bytes;
}
