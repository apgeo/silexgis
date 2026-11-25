// SPDX-License-Identifier: AGPL-3.0-or-later
import { MovieSinkMismatch, SINK_WRITE_BYTES, writeParts, type MovieFramePlan, type MovieSink } from '../movieSink.ts';
import type { EncodedVideoFrame } from '../video/encodedFrame.ts';
import { byteLength, concat, mp4Head, type Mp4Sample, type Mp4Track } from './mp4Muxer.ts';

const UINT32_MAX = 0xffffffff;

/**
 * Writes an MP4 to a sink as its frames arrive, to the same bytes `muxMp4` makes of those frames.
 *
 * The index stays in front of the frames, as in every MP4 written here, so the file can still be
 * shown before it has all arrived. The size of that front depends on how many frames there are,
 * which of them are keyframes and how long each lasts — all planned before the first frame is
 * encoded — and on nothing in the frames' bytes. So it is measured from the plan, the frames are
 * written past a gap of that size, and the front is written into the gap last, from the frames
 * there really were.
 *
 * Whether the file is right rests on one comparison, made at the end: the front built from the real
 * frames is exactly as long as the gap left for it. Where it is not — or as soon as it is certain
 * not to be: a keyframe the plan did not have, or a file past four gigabytes, whose offsets take
 * eight bytes each instead of four — the writer refuses with `MovieSinkMismatch` rather than write
 * a file that differs.
 */
export class Mp4SinkWriter {
  private readonly track: Mp4Track;
  private readonly plan: MovieFramePlan;
  private readonly sink: MovieSink;
  private readonly gap: number;
  private readonly samples: Mp4Sample[] = [];
  private pending: Uint8Array[] = [];
  private pendingBytes = 0;
  /** Bytes of frames already in the sink. */
  private written = 0;

  constructor(track: Mp4Track, plan: MovieFramePlan, sink: MovieSink) {
    this.track = track;
    this.plan = plan;
    this.sink = sink;
    if (plan.count < 1) throw new Error('there are no frames to write');
    const planned = Array.from({ length: plan.count }, (_, i) => ({
      size: 0,
      timestamp: plan.timestamp(i),
      duration: plan.timestamp(i + 1) - plan.timestamp(i),
      key: plan.key(i),
    }));
    this.gap = byteLength(mp4Head(track, planned));
  }

  async add(frame: EncodedVideoFrame): Promise<void> {
    const index = this.samples.length;
    if (index === 0 && !frame.key) throw new Error('the first frame is not a keyframe');
    if (index > 0 && frame.timestamp < this.samples[index - 1].timestamp) {
      throw new Error(`frame ${index} is shown before the frame it follows, which this writer cannot store`);
    }
    if (index >= this.plan.count) throw new MovieSinkMismatch(`frame ${index} is one more than the ${this.plan.count} planned`);
    if (frame.key !== this.plan.key(index)) {
      throw new MovieSinkMismatch(
        frame.key ? `frame ${index} came back as a keyframe nobody asked for` : `frame ${index} is not the keyframe that was asked for`,
      );
    }
    this.samples.push({ size: frame.data.byteLength, timestamp: frame.timestamp, duration: frame.duration, key: frame.key });
    this.pending.push(frame.data);
    this.pendingBytes += frame.data.byteLength;
    if (this.gap + this.written + this.pendingBytes > UINT32_MAX) {
      throw new MovieSinkMismatch('it passed four gigabytes, where its index takes a wider form');
    }
    if (this.pendingBytes >= SINK_WRITE_BYTES) await this.flush();
  }

  /** Writes what is left. The sink then holds the whole file; closing it is the caller's. */
  async finish(): Promise<void> {
    if (this.samples.length === 0) throw new Error('there are no frames to write');
    await this.flush();
    const head = mp4Head(this.track, this.samples);
    const size = byteLength(head);
    if (size !== this.gap) {
      throw new MovieSinkMismatch(`its front came to ${size} bytes where ${this.gap} had been left for it`);
    }
    await this.sink.write(0, concat(head));
  }

  private async flush(): Promise<void> {
    const parts = this.pending;
    const size = this.pendingBytes;
    this.pending = [];
    this.pendingBytes = 0;
    await writeParts(this.sink, this.gap + this.written, parts);
    this.written += size;
  }
}
