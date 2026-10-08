// SPDX-License-Identifier: AGPL-3.0-or-later
import { MovieSinkMismatch, writeParts, type MovieFramePlan, type MovieSink } from '../movieSink.ts';
import type { EncodedVideoFrame } from '../video/encodedFrame.ts';
import { byteLength, concat, type EbmlParts } from './ebml.ts';
import {
  webmClusterElement,
  webmHead,
  webmSimpleBlock,
  webmStartsCluster,
  webmTimeOf,
  type WebmCluster,
  type WebmClusterShape,
  type WebmTrack,
} from './webmMuxer.ts';

/**
 * How many bytes of clusters are kept in memory before any is written out: 256 MiB.
 *
 * The file's front holds the Segment's size, written in the fewest bytes that hold it — four up to
 * 256 MiB, five from there to 32 GiB. Everything after that field sits one byte further along in
 * the longer form, so no cluster can be put in its place until it is known which form the file
 * will have, and that is known the moment the clusters alone pass 256 MiB. A movie that never gets
 * there is written in one go at the end, exactly as the in-memory writer would have assembled it.
 */
export const WEBM_SINK_HOLD_BYTES = 2 ** 28;

/**
 * Writes a WebM to a sink as its frames arrive, to the same bytes `muxWebm` makes of those frames.
 *
 * Frames are gathered into clusters by the rule the in-memory writer uses. Finished clusters are
 * held until the hold is full; then the size of the file's front is worked out — from the clusters
 * so far and from the plan of the frames still to come, since the front lists every cluster a
 * player can seek to — the held clusters are written past a gap of that size, and each later one
 * follows as it is finished. The front itself is written into the gap last, from the clusters
 * there really were.
 *
 * Whether the file is right rests on one comparison, made at the end: the front that was built
 * from the real clusters is exactly as long as the gap left for it. Where it is not, nothing can be
 * moved, and the writer refuses with `MovieSinkMismatch` rather than write a file that differs.
 * A keyframe the plan did not ask for is refused as soon as it arrives, since it can only end that
 * way and the rest of the movie would be encoded for nothing.
 */
export class WebmSinkWriter {
  private readonly track: WebmTrack;
  private readonly plan: MovieFramePlan;
  private readonly sink: MovieSink;
  private readonly holdBytes: number;
  private readonly shapes: WebmClusterShape[] = [];
  private held: EbmlParts = [];
  private heldBytes = 0;
  /** The size of the gap left for the front; null while everything is still held. */
  private gap: number | null = null;
  /** Bytes of clusters already in the sink. */
  private written = 0;
  private current: WebmCluster | null = null;
  private index = 0;
  private lastTimestamp = 0;
  private endUs = 0;

  constructor(track: WebmTrack, plan: MovieFramePlan, sink: MovieSink, holdBytes: number = WEBM_SINK_HOLD_BYTES) {
    this.track = track;
    this.plan = plan;
    this.sink = sink;
    this.holdBytes = holdBytes;
  }

  async add(frame: EncodedVideoFrame): Promise<void> {
    if (this.index === 0 && !frame.key) throw new Error('the first frame is not a keyframe');
    if (this.index > 0 && frame.timestamp < this.lastTimestamp) {
      throw new Error(`frame ${this.index} is shown before the frame it follows, which this writer cannot store`);
    }
    const time = webmTimeOf(frame.timestamp);
    if (!this.current || webmStartsCluster(this.current, frame.key, time)) {
      await this.endCluster();
      if (this.gap === null && this.heldBytes >= this.holdBytes) await this.leaveGap();
      this.current = { time, startsWithKey: frame.key, blocks: [] };
    }
    if (this.gap !== null && (this.index >= this.plan.count || frame.key !== this.plan.key(this.index))) {
      throw new MovieSinkMismatch(
        frame.key ? `frame ${this.index} came back as a keyframe nobody asked for` : `frame ${this.index} is not the keyframe that was asked for`,
      );
    }
    this.current.blocks.push(...webmSimpleBlock(time - this.current.time, frame));
    this.lastTimestamp = frame.timestamp;
    this.endUs = frame.timestamp + frame.duration;
    this.index++;
  }

  /** Writes what is left. The sink then holds the whole file; closing it is the caller's. */
  async finish(): Promise<void> {
    if (this.index === 0) throw new Error('there are no frames to write');
    await this.endCluster();
    const head = webmHead(this.track, this.endUs / 1000, this.shapes);
    if (this.gap === null) {
      await writeParts(this.sink, 0, [...head, ...this.held]);
      this.held = [];
      return;
    }
    const size = byteLength(head);
    if (size !== this.gap) {
      throw new MovieSinkMismatch(`its front came to ${size} bytes where ${this.gap} had been left for it`);
    }
    await this.sink.write(0, concat(head));
  }

  private async endCluster(): Promise<void> {
    if (!this.current) return;
    const parts = webmClusterElement(this.current);
    const size = byteLength(parts);
    this.shapes.push({ time: this.current.time, startsWithKey: this.current.startsWithKey, size });
    this.current = null;
    if (this.gap === null) {
      this.held.push(...parts);
      this.heldBytes += size;
      return;
    }
    await writeParts(this.sink, this.gap + this.written, parts);
    this.written += size;
  }

  // Called between clusters, with `index` the frame that is about to open the next one.
  private async leaveGap(): Promise<void> {
    const planned: WebmClusterShape[] = [...this.shapes];
    let open: { time: number } | null = null;
    for (let i = this.index; i < this.plan.count; i++) {
      const time = webmTimeOf(this.plan.timestamp(i));
      const key = this.plan.key(i);
      if (webmStartsCluster(open, key, time)) {
        open = { time };
        // A cluster to come is given no size: the only thing in the front a cluster's size reaches
        // is the width of the Segment's own size, and the hold was chosen so that the clusters
        // already held have settled that.
        planned.push({ time, startsWithKey: key, size: 0 });
      }
    }
    this.gap = byteLength(webmHead(this.track, 0, planned));
    await writeParts(this.sink, this.gap, this.held);
    this.written = this.heldBytes;
    this.held = [];
    this.heldBytes = 0;
  }
}
