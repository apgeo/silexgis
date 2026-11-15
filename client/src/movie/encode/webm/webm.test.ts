// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { MemoryMovieSink } from '../memoryMovieSink.ts';
import type { EncodedVideoFrame } from '../video/encodedFrame.ts';
import { concat, EBML_ID as ID, ebmlId, ebmlSize, ebmlUint } from './ebml.ts';
import { muxWebm, WEBM_MAX_CLUSTER_MS } from './webmMuxer.ts';
import { WebmSinkWriter } from './webmSinkWriter.ts';

// ---- An EBML reader written from the format description, independent of the writer. ----

interface Node {
  id: number;
  /** Offset of the element's first byte (its id) in the file. */
  start: number;
  /** Offset of its body. */
  body: number;
  size: number;
  children?: Node[];
  bytes: Uint8Array;
}

const MASTERS = new Set<number>([
  ID.EBML,
  ID.Segment,
  ID.SeekHead,
  ID.Seek,
  ID.Info,
  ID.Tracks,
  ID.TrackEntry,
  ID.Video,
  ID.Cluster,
  ID.Cues,
  ID.CuePoint,
  ID.CueTrackPositions,
]);

function readVint(bytes: Uint8Array, at: number, keepMarker: boolean): { value: number; length: number } {
  const first = bytes[at];
  let length = 1;
  while (length <= 8 && !(first & (0x80 >> (length - 1)))) length++;
  if (length > 8) throw new Error(`no length marker at ${at}`);
  let value = keepMarker ? first : first & (0xff >> length);
  for (let i = 1; i < length; i++) value = value * 256 + bytes[at + i];
  return { value, length };
}

function parse(bytes: Uint8Array, from: number, to: number): Node[] {
  const nodes: Node[] = [];
  let at = from;
  while (at < to) {
    const id = readVint(bytes, at, true);
    const size = readVint(bytes, at + id.length, false);
    const body = at + id.length + size.length;
    const node: Node = { id: id.value, start: at, body, size: size.value, bytes: bytes.subarray(body, body + size.value) };
    expect(body + size.value, `element 0x${id.value.toString(16)} at ${at} runs past its parent`).toBeLessThanOrEqual(to);
    if (MASTERS.has(id.value)) node.children = parse(bytes, body, body + size.value);
    nodes.push(node);
    at = body + size.value;
  }
  expect(at).toBe(to);
  return nodes;
}

const uint = (b: Uint8Array) => b.reduce((v, x) => v * 256 + x, 0);
const text = (b: Uint8Array) => new TextDecoder().decode(b);
const child = (n: Node, id: number) => n.children?.find((c) => c.id === id);
const children = (n: Node, id: number) => n.children?.filter((c) => c.id === id) ?? [];

function frames(count: number, fps: number, keyEvery: number): EncodedVideoFrame[] {
  return Array.from({ length: count }, (_, i) => ({
    data: Uint8Array.from([i & 0xff, (i >> 8) & 0xff, 0xaa, 0x55, i % 7]),
    timestamp: Math.round((i * 1e6) / fps),
    duration: Math.round(((i + 1) * 1e6) / fps) - Math.round((i * 1e6) / fps),
    key: i % keyEvery === 0,
  }));
}

describe('EBML primitives', () => {
  it('writes sizes in the fewest bytes, with the length marker, and never as the all-ones "unknown"', () => {
    expect([...ebmlSize(0)]).toEqual([0x80]);
    expect([...ebmlSize(126)]).toEqual([0xfe]);
    // 127 in one byte would be 0xff, which means "unknown size"; it takes two.
    expect([...ebmlSize(127)]).toEqual([0x40, 0x7f]);
    expect([...ebmlSize(16382)]).toEqual([0x7f, 0xfe]);
    expect([...ebmlSize(16383)]).toEqual([0x20, 0x3f, 0xff]);
    expect([...ebmlSize(5, 8)]).toEqual([0x01, 0, 0, 0, 0, 0, 0, 5]);
    expect(() => ebmlSize(127, 1)).toThrow(/does not fit/);
    expect(() => ebmlSize(-1)).toThrow();
    for (const n of [0, 1, 126, 127, 300, 16382, 16383, 2 ** 21, 2 ** 40]) {
      const bytes = ebmlSize(n);
      expect(readVint(bytes, 0, false)).toEqual({ value: n, length: bytes.length });
    }
  });

  it('writes ids with their own marker bits and unsigned integers in the fewest bytes', () => {
    expect([...ebmlId(ID.EBML)]).toEqual([0x1a, 0x45, 0xdf, 0xa3]);
    expect([...ebmlId(ID.TimecodeScale)]).toEqual([0x2a, 0xd7, 0xb1]);
    expect([...ebmlId(ID.SimpleBlock)]).toEqual([0xa3]);
    expect([...ebmlUint(0)]).toEqual([0]);
    expect([...ebmlUint(1_000_000)]).toEqual([0x0f, 0x42, 0x40]);
    expect([...ebmlUint(7, 8)]).toEqual([0, 0, 0, 0, 0, 0, 0, 7]);
  });
});

describe('WebM muxer', () => {
  const track = { codecId: 'V_VP9' as const, width: 320, height: 240, fps: 25 };

  it('writes a well-formed tree: header, a Segment exactly as long as the rest of the file, Info with the duration', () => {
    const input = frames(60, 25, 50);
    const file = concat(muxWebm(track, input));
    const top = parse(file, 0, file.length);
    expect(top.map((n) => n.id)).toEqual([ID.EBML, ID.Segment]);
    const [header, segment] = top;
    expect(text(child(header, ID.DocType)!.bytes)).toBe('webm');
    expect(uint(child(header, ID.DocTypeVersion)!.bytes)).toBe(4);
    expect(uint(child(header, ID.DocTypeReadVersion)!.bytes)).toBe(2);
    expect(segment.body + segment.size).toBe(file.length);

    const info = child(segment, ID.Info)!;
    expect(uint(child(info, ID.TimecodeScale)!.bytes)).toBe(1_000_000);
    const duration = child(info, ID.Duration)!.bytes;
    expect(new DataView(duration.buffer, duration.byteOffset, 8).getFloat64(0)).toBe(2400); // 60 frames at 25 fps

    const entry = child(child(segment, ID.Tracks)!, ID.TrackEntry)!;
    expect(text(child(entry, ID.CodecID)!.bytes)).toBe('V_VP9');
    expect(uint(child(entry, ID.TrackType)!.bytes)).toBe(1);
    expect(uint(child(entry, ID.DefaultDuration)!.bytes)).toBe(40_000_000);
    const video = child(entry, ID.Video)!;
    expect(uint(child(video, ID.PixelWidth)!.bytes)).toBe(320);
    expect(uint(child(video, ID.PixelHeight)!.bytes)).toBe(240);
  });

  it('starts a Cluster at every keyframe, keeps every frame in order, and points each cue and seek entry at its element', () => {
    const input = frames(60, 25, 50); // keyframes at 0 and 50 (0 s and 2 s)
    const file = concat(muxWebm(track, input));
    const segment = parse(file, 0, file.length)[1];
    const dataStart = segment.body;
    const clusters = children(segment, ID.Cluster);
    expect(clusters.map((c) => uint(child(c, ID.Timecode)!.bytes))).toEqual([0, 2000]);

    const blocks = clusters.flatMap((c) =>
      children(c, ID.SimpleBlock).map((b) => ({ cluster: uint(child(c, ID.Timecode)!.bytes), b: b.bytes })),
    );
    expect(blocks).toHaveLength(60);
    blocks.forEach(({ cluster, b }, i) => {
      expect(b[0]).toBe(0x81); // track 1
      const relative = new DataView(b.buffer, b.byteOffset + 1, 2).getInt16(0);
      expect(cluster + relative).toBe(Math.round(input[i].timestamp / 1000));
      expect(b[3] & 0x80 ? true : false).toBe(input[i].key);
      expect([...b.subarray(4)]).toEqual([...input[i].data]);
    });

    const cuePoints = children(child(segment, ID.Cues)!, ID.CuePoint);
    expect(cuePoints).toHaveLength(2);
    cuePoints.forEach((cue, i) => {
      const positions = child(cue, ID.CueTrackPositions)!;
      const at = dataStart + uint(child(positions, ID.CueClusterPosition)!.bytes);
      expect(at).toBe(clusters[i].start);
      expect(uint(child(cue, ID.CueTime)!.bytes)).toBe(uint(child(clusters[i], ID.Timecode)!.bytes));
    });

    for (const seek of children(child(segment, ID.SeekHead)!, ID.Seek)) {
      const target = uint(child(seek, ID.SeekID)!.bytes);
      const at = dataStart + uint(child(seek, ID.SeekPosition)!.bytes);
      expect(readVint(file, at, true).value).toBe(target);
    }
  });

  it('never lets a cluster run long enough to overflow a block’s 16-bit relative time', () => {
    // One keyframe and then 80 s of frames at 1 fps: the cluster must be split without a keyframe.
    const input = frames(80, 1, 1000);
    const file = concat(muxWebm({ ...track, fps: 1 }, input));
    const segment = parse(file, 0, file.length)[1];
    const clusters = children(segment, ID.Cluster);
    expect(clusters.length).toBe(3);
    for (const c of clusters) {
      for (const b of children(c, ID.SimpleBlock)) {
        const relative = new DataView(b.bytes.buffer, b.bytes.byteOffset + 1, 2).getInt16(0);
        expect(relative).toBeGreaterThanOrEqual(0);
        expect(relative).toBeLessThanOrEqual(WEBM_MAX_CLUSTER_MS);
      }
    }
    // Only the cluster that opens on a keyframe is a place to start decoding from.
    expect(children(child(segment, ID.Cues)!, ID.CuePoint)).toHaveLength(1);
  });

  it('is a pure function of its input, and refuses input a player could not start from', () => {
    const input = frames(10, 10, 5);
    expect(concat(muxWebm(track, input))).toEqual(concat(muxWebm(track, input)));
    expect(() => muxWebm(track, [])).toThrow(/no frames/);
    expect(() => muxWebm(track, input.slice(1))).toThrow(/first frame is not a keyframe/);
  });
});

describe('a WebM written to a sink as its frames arrive', () => {
  it('reads as a whole file: a Segment as long as the rest, the duration, every frame, and a cue at each cluster’s own place', async () => {
    const track = { codecId: 'V_VP9' as const, width: 320, height: 240, fps: 25 };
    const input = frames(600, 25, 50); // twelve clusters of two seconds
    const sink = new MemoryMovieSink();
    // A hold the first cluster fills, so the eleven after it are each written as they end.
    const writer = new WebmSinkWriter(track, { count: 600, timestamp: (i) => Math.round((i * 1e6) / 25), key: (i) => i % 50 === 0 }, sink, 200);
    for (const frame of input) await writer.add(frame);
    await writer.finish();
    expect(sink.writes.length).toBeGreaterThan(10);
    expect(sink.writes.at(-1)!.position).toBe(0);

    const file = sink.bytes();
    const top = parse(file, 0, file.length);
    expect(top.map((n) => n.id)).toEqual([ID.EBML, ID.Segment]);
    const segment = top[1];
    expect(segment.body + segment.size).toBe(file.length);
    const duration = child(child(segment, ID.Info)!, ID.Duration)!.bytes;
    expect(new DataView(duration.buffer, duration.byteOffset, 8).getFloat64(0)).toBe(24_000);

    const clusters = children(segment, ID.Cluster);
    expect(clusters).toHaveLength(12);
    const blocks = clusters.flatMap((c) => children(c, ID.SimpleBlock));
    expect(blocks).toHaveLength(600);
    blocks.forEach((block, i) => expect([...block.bytes.subarray(4)]).toEqual([...input[i].data]));

    const points = children(child(segment, ID.Cues)!, ID.CuePoint);
    expect(points).toHaveLength(12);
    points.forEach((point, i) => {
      expect(uint(child(point, ID.CueTime)!.bytes)).toBe(i * 2000);
      const at = uint(child(child(point, ID.CueTrackPositions)!, ID.CueClusterPosition)!.bytes);
      expect(segment.body + at).toBe(clusters[i].start);
    });
  });
});
