// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { EncodedVideoFrame } from '../video/encodedFrame.ts';
import { avcDecoderConfig, isWellFormedAvcC } from './avcConfig.ts';
import { mp4MediaTimescale, muxMp4 } from './mp4Muxer.ts';

// ---- An ISO base media box reader written from the format description, independent of the writer. ----

interface Box {
  type: string;
  start: number;
  size: number;
  body: Uint8Array;
  children?: Box[];
}

const CONTAINERS = new Set(['moov', 'trak', 'mdia', 'minf', 'dinf', 'stbl']);

function parse(bytes: Uint8Array, from: number, to: number): Box[] {
  const boxes: Box[] = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let at = from;
  while (at < to) {
    let size = view.getUint32(at);
    const type = String.fromCharCode(...bytes.subarray(at + 4, at + 8));
    let header = 8;
    if (size === 1) {
      size = Number(view.getBigUint64(at + 8));
      header = 16;
    }
    expect(size, `box '${type}' at ${at}`).toBeGreaterThanOrEqual(header);
    expect(at + size, `box '${type}' at ${at} runs past its parent`).toBeLessThanOrEqual(to);
    const box: Box = { type, start: at, size, body: bytes.subarray(at + header, at + size) };
    if (CONTAINERS.has(type)) box.children = parse(bytes, at + header, at + size);
    boxes.push(box);
    at += size;
  }
  expect(at).toBe(to);
  return boxes;
}

function find(boxes: readonly Box[] | undefined, path: string): Box {
  const [head, ...rest] = path.split('/');
  const box = boxes?.find((b) => b.type === head);
  if (!box) throw new Error(`no '${head}' box`);
  return rest.length === 0 ? box : find(box.children, rest.join('/'));
}

const u32 = (b: Uint8Array, at: number) => new DataView(b.buffer, b.byteOffset, b.byteLength).getUint32(at);
const u16 = (b: Uint8Array, at: number) => new DataView(b.buffer, b.byteOffset, b.byteLength).getUint16(at);
/** A full box's table of 32-bit entries after its version/flags and count. */
const table = (b: Uint8Array, width = 1) =>
  Array.from({ length: u32(b, 4) * width }, (_, i) => u32(b, 8 + 4 * i));

function join(parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.byteLength, 0));
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.byteLength;
  }
  return out;
}

function frames(count: number, fps: number, keyEvery: number): EncodedVideoFrame[] {
  return Array.from({ length: count }, (_, i) => ({
    // Distinct lengths and contents, so a wrong offset or size cannot land on the right bytes.
    data: Uint8Array.from({ length: 3 + (i % 11) }, (_, j) => (i * 31 + j) & 0xff),
    timestamp: Math.round((i * 1e6) / fps),
    duration: Math.round(((i + 1) * 1e6) / fps) - Math.round((i * 1e6) / fps),
    key: i % keyEvery === 0,
  }));
}

const avcC = Uint8Array.from([1, 0x42, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x04, 0x67, 0x42, 0x00, 0x1f, 0x01, 0x00, 0x02, 0x68, 0xce]);

describe('MP4 muxer', () => {
  it('writes ftyp, then the index, then the data, with every box size adding up to the file', () => {
    const file = join(muxMp4({ width: 640, height: 360, fps: 25, avcC }, frames(60, 25, 50)));
    const top = parse(file, 0, file.length);
    expect(top.map((b) => b.type)).toEqual(['ftyp', 'moov', 'mdat']);
    expect(String.fromCharCode(...top[0].body.subarray(0, 4))).toBe('isom');
    expect(top.reduce((n, b) => n + b.size, 0)).toBe(file.length);

    const avc1 = find(top, 'moov/trak/mdia/minf/stbl/stsd').body.subarray(8);
    expect(String.fromCharCode(...avc1.subarray(4, 8))).toBe('avc1');
    expect(u16(avc1, 8 + 24)).toBe(640);
    expect(u16(avc1, 8 + 26)).toBe(360);
    // The avcC box sits after the 78 bytes of the visual sample entry and holds the record as given.
    const avcCBox = avc1.subarray(8 + 78);
    expect(String.fromCharCode(...avcCBox.subarray(4, 8))).toBe('avcC');
    expect([...avcCBox.subarray(8, u32(avcCBox, 0))]).toEqual([...avcC]);
    expect(u32(avc1, 0)).toBe(8 + 78 + 8 + avcC.length);
  });

  it('points every chunk offset at its own frame’s bytes inside mdat, with sizes matching', () => {
    const input = frames(60, 25, 50);
    const file = join(muxMp4({ width: 640, height: 360, fps: 25, avcC }, input));
    const top = parse(file, 0, file.length);
    const mdat = find(top, 'mdat');
    const stbl = find(top, 'moov/trak/mdia/minf/stbl');
    const offsets = table(find(stbl.children, 'stco').body);
    const stsz = find(stbl.children, 'stsz').body;
    expect(u32(stsz, 4)).toBe(0); // sizes differ, so each is listed
    const sizes = Array.from({ length: u32(stsz, 8) }, (_, i) => u32(stsz, 12 + 4 * i));
    expect(table(find(stbl.children, 'stsc').body, 3)).toEqual([1, 1, 1]); // one frame per chunk
    expect(offsets).toHaveLength(60);
    expect(sizes).toEqual(input.map((f) => f.data.length));
    offsets.forEach((offset, i) => {
      expect(offset).toBeGreaterThanOrEqual(mdat.start + 8);
      expect(offset + sizes[i]).toBeLessThanOrEqual(mdat.start + mdat.size);
      expect([...file.subarray(offset, offset + sizes[i])]).toEqual([...input[i].data]);
    });
    expect(offsets[59] + sizes[59]).toBe(mdat.start + mdat.size);
  });

  it('keeps the timing consistent: one constant frame length, the keyframes, and the same length in every header', () => {
    const fps = 30; // a rate whose microsecond timestamps do not divide evenly
    const input = frames(90, fps, 60);
    const file = join(muxMp4({ width: 1280, height: 720, fps, avcC }, input));
    const top = parse(file, 0, file.length);
    const stbl = find(top, 'moov/trak/mdia/minf/stbl');
    const timescale = mp4MediaTimescale(fps);
    expect(table(find(stbl.children, 'stts').body, 2)).toEqual([90, timescale / fps]);
    expect(table(find(stbl.children, 'stss').body)).toEqual([1, 61]);

    const mdhd = find(top, 'moov/trak/mdia/mdhd').body;
    expect(u32(mdhd, 12)).toBe(timescale);
    expect(u32(mdhd, 16)).toBe(90 * (timescale / fps)); // 3 s in the track's ticks
    const mvhd = find(top, 'moov/mvhd').body;
    expect(u32(mvhd, 12)).toBe(1000);
    expect(u32(mvhd, 16)).toBe(3000);
    const tkhd = find(top, 'moov/trak/tkhd').body;
    expect(u32(tkhd, 20)).toBe(3000);
    expect(u32(tkhd, 76) / 0x10000).toBe(1280);
    expect(u32(tkhd, 80) / 0x10000).toBe(720);
    expect(String.fromCharCode(...find(top, 'moov/trak/mdia/hdlr').body.subarray(8, 12))).toBe('vide');
  });

  it('refuses frames it could only store wrongly', () => {
    const input = frames(4, 10, 2);
    expect(() => muxMp4({ width: 16, height: 16, fps: 10, avcC }, [])).toThrow(/no frames/);
    expect(() => muxMp4({ width: 16, height: 16, fps: 10, avcC: new Uint8Array(0) }, input)).toThrow(/decoder configuration/);
    const reordered = [input[0], input[2], input[1], input[3]];
    expect(() => muxMp4({ width: 16, height: 16, fps: 10, avcC }, reordered)).toThrow(/shown before/);
  });
});

describe('H.264 decoder configuration', () => {
  // Parameter sets of a synthetic 320×240 Baseline clip: SPS (type 7) and PPS (type 8).
  const sps = Uint8Array.from([0x67, 0x42, 0xc0, 0x1f, 0xd9, 0x01, 0x41, 0xfa, 0x10]);
  const pps = Uint8Array.from([0x68, 0xcb, 0x8c, 0xb2]);
  const good = Uint8Array.from([1, 0x42, 0xc0, 0x1f, 0xff, 0xe1, 0, sps.length, ...sps, 1, 0, pps.length, ...pps]);
  const lengthPrefixed = (...nals: Uint8Array[]) =>
    Uint8Array.from(nals.flatMap((n) => [0, 0, 0, n.length, ...n]));
  const idr = Uint8Array.from([0x65, 0x88, 0x84, 0x00]);

  it('keeps a well-formed record exactly as the encoder reported it', () => {
    expect(isWellFormedAvcC(good)).toBe(true);
    expect(avcDecoderConfig(good, lengthPrefixed(idr))).toBe(good);
  });

  it('rebuilds a record whose parameter sets repeat their header byte from the sets in front of the first keyframe', () => {
    // The shape Firefox reported: every set starts with its NAL header twice.
    const doubled = Uint8Array.from([1, 0x42, 0xc0, 0x1f, 0x03, 0x01, 0, sps.length + 1, 0x67, ...sps, 1, 0, pps.length + 1, 0x68, ...pps]);
    expect(isWellFormedAvcC(doubled)).toBe(false);
    const rebuilt = avcDecoderConfig(doubled, lengthPrefixed(sps, pps, idr));
    expect([...rebuilt!]).toEqual([...good]);
    // With nothing in the frame to rebuild from, the reported record is all there is.
    expect(avcDecoderConfig(doubled, lengthPrefixed(idr))).toBe(doubled);
  });
});
