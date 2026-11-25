// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { MemoryMovieSink } from './memoryMovieSink.ts';
import { isMovieSinkMismatch, SINK_WRITE_BYTES, writeParts, type MovieFramePlan } from './movieSink.ts';
import { muxMp4 } from './mp4/mp4Muxer.ts';
import { Mp4SinkWriter } from './mp4/mp4SinkWriter.ts';
import type { EncodedVideoFrame } from './video/encodedFrame.ts';
import { muxWebm } from './webm/webmMuxer.ts';
import { WebmSinkWriter } from './webm/webmSinkWriter.ts';

// A file written to a sink as its frames arrive has one thing to be: the file the in-memory writer
// makes of the same frames, byte for byte. Each case below writes the same frames both ways.

const timeOf = (index: number, fps: number) => Math.round((index * 1e6) / fps);

function plan(count: number, fps: number, keyEvery: number): MovieFramePlan {
  return { count, timestamp: (i) => timeOf(i, fps), key: (i) => i % keyEvery === 0 };
}

/** Frames as the plan has them, of differing sizes and contents; `scale` makes them larger. */
function frames(count: number, fps: number, keyEvery: number, scale = 1): EncodedVideoFrame[] {
  let seed = 4711;
  const next = () => (seed = (seed * 1103515245 + 12345) % 2147483648);
  return Array.from({ length: count }, (_, i) => {
    const data = new Uint8Array((20 + (next() % 300)) * scale);
    for (let b = 0; b < data.length; b += scale) data[b] = next() % 256;
    return { data, timestamp: timeOf(i, fps), duration: timeOf(i + 1, fps) - timeOf(i, fps), key: i % keyEvery === 0 };
  });
}

function join(parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.byteLength, 0));
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.byteLength;
  }
  return out;
}

/** Compared by length and then by content, since a failed comparison of megabytes prints them. */
function expectSameBytes(written: Uint8Array, expected: Uint8Array): void {
  expect(written.byteLength).toBe(expected.byteLength);
  let differsAt = -1;
  for (let i = 0; i < expected.byteLength && differsAt < 0; i++) if (written[i] !== expected[i]) differsAt = i;
  expect(differsAt).toBe(-1);
}

const WEBM = { codecId: 'V_VP9' as const, width: 640, height: 360, fps: 30 };
const AVCC = Uint8Array.from([1, 0x42, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x04, 0x67, 0x42, 0x00, 0x1f, 0x01, 0x00, 0x02, 0x68, 0xce]);
const MP4 = { width: 640, height: 360, fps: 30, avcC: AVCC };

async function webmThroughSink(input: readonly EncodedVideoFrame[], planned: MovieFramePlan, holdBytes?: number) {
  const sink = new MemoryMovieSink();
  const writer = new WebmSinkWriter(WEBM, planned, sink, holdBytes);
  for (const frame of input) await writer.add(frame);
  await writer.finish();
  return sink;
}

async function mp4ThroughSink(input: readonly EncodedVideoFrame[], planned: MovieFramePlan) {
  const sink = new MemoryMovieSink();
  const writer = new Mp4SinkWriter(MP4, planned, sink);
  for (const frame of input) await writer.add(frame);
  await writer.finish();
  return sink;
}

describe('writeParts', () => {
  it('writes parts end to end from where it was told, small ones gathered and none split or lost', async () => {
    const sink = new MemoryMovieSink();
    const big = new Uint8Array(SINK_WRITE_BYTES + 5).fill(7);
    const parts = [Uint8Array.from([1, 2, 3]), new Uint8Array(0), Uint8Array.from([4]), big, Uint8Array.from([9, 9])];
    const end = await writeParts(sink, 10, parts);
    expect(end).toBe(10 + 3 + 1 + big.byteLength + 2);
    expect(sink.writes).toEqual([
      { position: 10, size: 4 },
      { position: 14, size: big.byteLength },
      { position: 14 + big.byteLength, size: 2 },
    ]);
    expectSameBytes(sink.bytes().subarray(10), join(parts));
  });
});

describe('a WebM written to a sink', () => {
  it('is the in-memory file when it is the shortest there can be: one frame', async () => {
    const input = frames(1, 30, 60);
    const sink = await webmThroughSink(input, plan(1, 30, 60));
    expectSameBytes(sink.bytes(), join(muxWebm(WEBM, input)));
    // Nothing was placed before the whole of it was known: one run of writes from the start.
    expect(sink.writes[0].position).toBe(0);
  });

  it('is the in-memory file when it never fills the hold, whatever keyframes the encoder made', async () => {
    // Keyframes nobody planned: while everything is still held, no front has been sized by a plan.
    const input = frames(200, 30, 7);
    const sink = await webmThroughSink(input, plan(200, 30, 60));
    expectSameBytes(sink.bytes(), join(muxWebm(WEBM, input)));
  });

  it('is the in-memory file when its clusters were written as they came, the front last', async () => {
    const input = frames(600, 30, 60); // ten clusters of two seconds
    // A hold the second cluster passes, and past the size where a Segment's length takes three bytes
    // — as the real hold is past where it takes five — so that length is settled when the gap is left.
    const sink = await webmThroughSink(input, plan(600, 30, 60), 20_000);
    expectSameBytes(sink.bytes(), join(muxWebm(WEBM, input)));
    // Clusters went out one by one, each further along, before the front was written at the start.
    const positions = sink.writes.map((w) => w.position);
    expect(positions.length).toBeGreaterThan(8);
    expect(positions.at(-1)).toBe(0);
    expect(positions[0]).toBeGreaterThan(0);
    expect(positions.slice(0, -1)).toEqual([...positions.slice(0, -1)].sort((a, b) => a - b));
  });

  it('is the in-memory file when clusters end on the clock rather than on a keyframe', async () => {
    const fps = 60;
    const input = frames(1900, fps, 4000); // one keyframe; the cluster has to end at thirty seconds
    const track = { ...WEBM, fps };
    const sink = new MemoryMovieSink();
    const writer = new WebmSinkWriter(track, plan(1900, fps, 4000), sink, 20_000);
    for (const frame of input) await writer.add(frame);
    await writer.finish();
    expectSameBytes(sink.bytes(), join(muxWebm(track, input)));
    expect(sink.writes.at(-1)!.position).toBe(0);
  });

  it('refuses a keyframe nobody asked for once clusters are being written, rather than write a file that differs', async () => {
    const input = frames(600, 30, 60);
    input[400] = { ...input[400], key: true };
    const sink = new MemoryMovieSink();
    const writer = new WebmSinkWriter(WEBM, plan(600, 30, 60), sink, 20_000);
    let refused: unknown = null;
    let at = -1;
    for (const [i, frame] of input.entries()) {
      try {
        await writer.add(frame);
      } catch (error) {
        refused = error;
        at = i;
        break;
      }
    }
    expect(at).toBe(400);
    expect(isMovieSinkMismatch(refused)).toBe(true);
    expect((refused as Error).message).toContain('a keyframe nobody asked for');
  });

  it('refuses at the end when the front does not fit the gap left for it', async () => {
    // With nothing held, the gap is left while the file is still small enough for its length to
    // take two bytes; the finished file's takes three, so its front is a byte too long.
    const input = frames(600, 30, 60);
    const sink = new MemoryMovieSink();
    const writer = new WebmSinkWriter(WEBM, plan(600, 30, 60), sink, 0);
    for (const frame of input) await writer.add(frame);
    const refused = await writer.finish().catch((error: unknown) => error);
    expect(isMovieSinkMismatch(refused)).toBe(true);
    expect((refused as Error).message).toMatch(/its front came to \d+ bytes where \d+ had been left/);
  });

  it('refuses frames as the in-memory writer does: none, a first that is no keyframe, one shown before its predecessor', async () => {
    await expect(new WebmSinkWriter(WEBM, plan(1, 30, 60), new MemoryMovieSink()).finish()).rejects.toThrow('there are no frames to write');
    const [first, second] = frames(2, 30, 60);
    await expect(new WebmSinkWriter(WEBM, plan(2, 30, 60), new MemoryMovieSink()).add({ ...first, key: false })).rejects.toThrow(
      'the first frame is not a keyframe',
    );
    const writer = new WebmSinkWriter(WEBM, plan(2, 30, 60), new MemoryMovieSink());
    await writer.add({ ...second, key: true });
    await expect(writer.add({ ...first, key: false })).rejects.toThrow(/frame 1 is shown before the frame it follows/);
  });
});

describe('an MP4 written to a sink', () => {
  it('is the in-memory file when it is the shortest there can be: one frame', async () => {
    const input = frames(1, 30, 60);
    const sink = await mp4ThroughSink(input, plan(1, 30, 60));
    expectSameBytes(sink.bytes(), join(muxMp4(MP4, input)));
  });

  it('is the in-memory file when its frames went out in several writes, the index in front written last', async () => {
    const input = frames(400, 30, 60, 40); // about 2.6 MB of frames
    const sink = await mp4ThroughSink(input, plan(400, 30, 60));
    expectSameBytes(sink.bytes(), join(muxMp4(MP4, input)));
    const positions = sink.writes.map((w) => w.position);
    expect(positions.length).toBeGreaterThan(3);
    expect(positions.at(-1)).toBe(0);
    expect(positions[0]).toBeGreaterThan(0);
    // The first frame went exactly where the index ends: nothing was left between them.
    expect(positions[0]).toBe(sink.writes.at(-1)!.size);
  });

  it('is the in-memory file at a rate whose frames do not last a whole number of microseconds', async () => {
    const input = frames(333, 12.5, 25);
    const sink = new MemoryMovieSink();
    const track = { ...MP4, fps: 12.5 };
    const writer = new Mp4SinkWriter(track, plan(333, 12.5, 25), sink);
    for (const frame of input) await writer.add(frame);
    await writer.finish();
    expectSameBytes(sink.bytes(), join(muxMp4(track, input)));
  });

  it('refuses a keyframe nobody asked for, and a planned one that did not come, as soon as it arrives', async () => {
    const input = frames(10, 30, 60);
    const extra = new Mp4SinkWriter(MP4, plan(10, 30, 60), new MemoryMovieSink());
    await extra.add(input[0]);
    const refused = await extra.add({ ...input[1], key: true }).catch((error: unknown) => error);
    expect(isMovieSinkMismatch(refused)).toBe(true);

    const missing = new Mp4SinkWriter(MP4, plan(10, 30, 2), new MemoryMovieSink());
    await missing.add(input[0]);
    await missing.add(input[1]);
    const second = await missing.add(input[2]).catch((error: unknown) => error);
    expect(isMovieSinkMismatch(second)).toBe(true);
    expect((second as Error).message).toContain('is not the keyframe that was asked for');
  });

  it('refuses at the end when fewer frames came than the index was sized for', async () => {
    const input = frames(9, 30, 60);
    const sink = new MemoryMovieSink();
    const writer = new Mp4SinkWriter(MP4, plan(10, 30, 60), sink);
    for (const frame of input) await writer.add(frame);
    const refused = await writer.finish().catch((error: unknown) => error);
    expect(isMovieSinkMismatch(refused)).toBe(true);
    // Nothing was written at the start: what is in the sink is not a file, and whoever owns the
    // sink aborts it.
    expect(sink.writes.every((w) => w.position > 0)).toBe(true);
  });
});
