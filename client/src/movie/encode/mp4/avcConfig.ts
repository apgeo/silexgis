// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The H.264 decoder configuration record (avcC) an MP4 carries, taken from the browser's encoder —
 * and rebuilt when what the encoder reported is malformed.
 *
 * Firefox 156 was seen reporting a record whose parameter sets each start with their one-byte
 * NAL header twice (`67 67 42 …`). Its frames still decode, because the same encoder also repeats
 * correct parameter sets in front of every keyframe, but a player that reads only the record (a
 * strict one, or ffmpeg, which logs "sps_id out of range" on it) sees nonsense. When the record
 * does not check out and the first keyframe carries its own parameter sets, the record is rebuilt
 * from those; otherwise it is used as given.
 */

const NAL_SPS = 7;
const NAL_PPS = 8;

/** The record to write: the reported one when it is well formed, else one rebuilt from the frame. */
export function avcDecoderConfig(reported: Uint8Array | null, firstKeyframe: Uint8Array | null): Uint8Array | null {
  if (reported && isWellFormedAvcC(reported)) return reported;
  const lengthSize = reported && reported.length >= 5 ? (reported[4] & 0x03) + 1 : 4;
  const sets = firstKeyframe ? inBandParameterSets(firstKeyframe, lengthSize) : null;
  if (sets && sets.sps.length > 0 && sets.pps.length > 0) return buildAvcC(sets.sps, sets.pps, lengthSize);
  return reported;
}

/**
 * Whether a record reads as the format describes it: version 1, at least one sequence parameter
 * set whose header says it is one and whose profile and level match the record's, then the
 * picture parameter sets, each saying it is one, all within the record's length.
 */
export function isWellFormedAvcC(record: Uint8Array): boolean {
  if (record.length < 7 || record[0] !== 1) return false;
  let o = 5;
  const spsCount = record[o++] & 0x1f;
  if (spsCount === 0) return false;
  for (let i = 0; i < spsCount; i++) {
    if (o + 2 > record.length) return false;
    const length = (record[o] << 8) | record[o + 1];
    o += 2;
    if (length < 4 || o + length > record.length) return false;
    if ((record[o] & 0x1f) !== NAL_SPS || record[o + 1] !== record[1] || record[o + 3] !== record[3]) return false;
    o += length;
  }
  if (o >= record.length) return false;
  const ppsCount = record[o++];
  if (ppsCount === 0) return false;
  for (let i = 0; i < ppsCount; i++) {
    if (o + 2 > record.length) return false;
    const length = (record[o] << 8) | record[o + 1];
    o += 2;
    if (length < 1 || o + length > record.length) return false;
    if ((record[o] & 0x1f) !== NAL_PPS) return false;
    o += length;
  }
  // Records for the higher profiles carry a few more bytes after the picture parameter sets.
  return o <= record.length;
}

/** The distinct parameter sets at the front of a length-prefixed frame, or null if it does not parse. */
export function inBandParameterSets(frame: Uint8Array, lengthSize: number): { sps: Uint8Array[]; pps: Uint8Array[] } | null {
  const sps: Uint8Array[] = [];
  const pps: Uint8Array[] = [];
  let o = 0;
  while (o < frame.length) {
    if (o + lengthSize > frame.length) return null;
    let length = 0;
    for (let i = 0; i < lengthSize; i++) length = length * 256 + frame[o + i];
    o += lengthSize;
    if (length === 0 || o + length > frame.length) return null;
    const nal = frame.subarray(o, o + length);
    const type = nal[0] & 0x1f;
    const into = type === NAL_SPS ? sps : type === NAL_PPS ? pps : null;
    if (into && !into.some((known) => sameBytes(known, nal))) into.push(nal);
    o += length;
  }
  return { sps, pps };
}

/** A version-1 record for the Baseline and Main profiles, which need nothing after the sets. */
export function buildAvcC(sps: readonly Uint8Array[], pps: readonly Uint8Array[], lengthSize: number): Uint8Array {
  const first = sps[0];
  const bytes: number[] = [1, first[1], first[2], first[3], 0xfc | (lengthSize - 1), 0xe0 | sps.length];
  for (const set of sps) bytes.push(set.length >> 8, set.length & 0xff, ...set);
  bytes.push(pps.length);
  for (const set of pps) bytes.push(set.length >> 8, set.length & 0xff, ...set);
  return Uint8Array.from(bytes);
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}
