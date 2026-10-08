// SPDX-License-Identifier: AGPL-3.0-or-later
import { checkFrameOrder, type EncodedVideoFrame } from '../video/encodedFrame.ts';
import {
  byteLength,
  concat,
  EBML_ID as ID,
  ebmlElement,
  ebmlFloat,
  ebmlId,
  ebmlSize,
  ebmlString,
  ebmlUint,
  ebmlUintElement,
  type EbmlParts,
} from './ebml.ts';

/**
 * A WebM file for one video track, written whole once every frame is known.
 *
 * Because the file is assembled in memory rather than streamed, every size is known before it is
 * written: nothing is left "unknown" and nothing is patched afterwards. That is also what lets the
 * file carry what a streaming recorder leaves out — the movie's Duration and a Cues index of its
 * keyframes — so players show the length and can seek.
 *
 * Layout inside the Segment: a SeekHead (where Info, Tracks and Cues are), Info, Tracks, Cues, then
 * the Clusters. The Cues come before the frames so a player finds them without reading to the end;
 * their cluster positions are written at a fixed width, so their size is known before the positions
 * they hold are.
 */

export type WebmCodecId = 'V_VP9' | 'V_VP8';

export interface WebmTrack {
  codecId: WebmCodecId;
  width: number;
  height: number;
  fps: number;
  /** Codec-specific setup bytes, when the codec has any. VP8 and VP9 need none. */
  codecPrivate?: Uint8Array;
}

// Block times are milliseconds: a TimecodeScale of a million nanoseconds.
const TIMECODE_SCALE_NS = 1_000_000;

/**
 * The longest a cluster may run. A block's time is stored relative to its cluster as a signed
 * 16-bit number of milliseconds, so a cluster must end before 32.767 s; a new one is started at
 * every keyframe anyway, and this only matters when keyframes are further apart than this.
 */
export const WEBM_MAX_CLUSTER_MS = 30_000;

// Positions that are written before they are known take a fixed eight bytes.
const POSITION_WIDTH = 8;

const TRACK_NUMBER = 1;

export const WEBM_APP_NAME = 'SilexGIS';

export interface WebmCluster {
  time: number;
  startsWithKey: boolean;
  blocks: EbmlParts;
}

/** What the file's front needs to know of a cluster: when it starts, whether a player can start
 *  decoding at it, and how many bytes its whole element takes. */
export interface WebmClusterShape {
  time: number;
  startsWithKey: boolean;
  size: number;
}

/** The whole WebM file, as parts to be joined in order. */
export function muxWebm(track: WebmTrack, frames: readonly EncodedVideoFrame[]): EbmlParts {
  checkFrameOrder(frames);
  const clusters = clustersOf(frames);
  const last = frames[frames.length - 1];
  const durationMs = (last.timestamp + last.duration) / 1000;
  const clusterParts = clusters.map(webmClusterElement);
  const shapes = clusters.map((c, i) => ({ time: c.time, startsWithKey: c.startsWithKey, size: byteLength(clusterParts[i]) }));
  return [...webmHead(track, durationMs, shapes), ...clusterParts.flat()];
}

/** A cluster as it is written: its start time, then its frames. */
export function webmClusterElement(cluster: WebmCluster): EbmlParts {
  return ebmlElement(ID.Cluster, [...ebmlUintElement(ID.Timecode, cluster.time), ...cluster.blocks]);
}

/**
 * Everything in the file before its first cluster: the EBML header, the Segment's own id and size,
 * the SeekHead, Info, Tracks and Cues. It is a function of the clusters' shapes alone — not of
 * their frames — which is what lets a file written as it is encoded have this part written last.
 */
export function webmHead(track: WebmTrack, durationMs: number, clusters: readonly WebmClusterShape[]): EbmlParts {
  const info = ebmlElement(ID.Info, [
    ...ebmlUintElement(ID.TimecodeScale, TIMECODE_SCALE_NS),
    ...ebmlElement(ID.Duration, ebmlFloat(durationMs)),
    ...ebmlElement(ID.MuxingApp, ebmlString(WEBM_APP_NAME)),
    ...ebmlElement(ID.WritingApp, ebmlString(WEBM_APP_NAME)),
  ]);
  const tracks = ebmlElement(ID.Tracks, ebmlElement(ID.TrackEntry, trackEntry(track)));

  const seekHeadSize = byteLength(seekHead(0, 0, 0));
  const infoAt = seekHeadSize;
  const tracksAt = infoAt + byteLength(info);
  const cuesAt = tracksAt + byteLength(tracks);
  const cuesSize = byteLength(cues(clusters, clusters.map(() => 0)));
  const clusterAt: number[] = [];
  let at = cuesAt + cuesSize;
  for (const cluster of clusters) {
    clusterAt.push(at);
    at += cluster.size;
  }

  // The Segment holds everything from here to the end of the file, clusters included; `at` is by
  // now the size of all of it.
  const front: EbmlParts = [...seekHead(infoAt, tracksAt, cuesAt), ...info, ...tracks, ...cues(clusters, clusterAt)];
  return [...ebmlHeader(), concat([ebmlId(ID.Segment), ebmlSize(at)]), ...front];
}

function ebmlHeader(): EbmlParts {
  return ebmlElement(ID.EBML, [
    ...ebmlUintElement(ID.EBMLVersion, 1),
    ...ebmlUintElement(ID.EBMLReadVersion, 1),
    ...ebmlUintElement(ID.EBMLMaxIDLength, 4),
    ...ebmlUintElement(ID.EBMLMaxSizeLength, 8),
    ...ebmlElement(ID.DocType, ebmlString('webm')),
    ...ebmlUintElement(ID.DocTypeVersion, 4),
    ...ebmlUintElement(ID.DocTypeReadVersion, 2),
  ]);
}

function trackEntry(track: WebmTrack): EbmlParts {
  return [
    ...ebmlUintElement(ID.TrackNumber, TRACK_NUMBER),
    ...ebmlUintElement(ID.TrackUID, 1),
    ...ebmlUintElement(ID.TrackType, 1), // video
    ...ebmlUintElement(ID.FlagLacing, 0),
    ...ebmlUintElement(ID.DefaultDuration, Math.round(1e9 / track.fps)),
    ...ebmlElement(ID.CodecID, ebmlString(track.codecId)),
    ...(track.codecPrivate ? ebmlElement(ID.CodecPrivate, track.codecPrivate) : []),
    ...ebmlElement(ID.Video, [
      ...ebmlUintElement(ID.PixelWidth, track.width),
      ...ebmlUintElement(ID.PixelHeight, track.height),
    ]),
  ];
}

function seekHead(infoAt: number, tracksAt: number, cuesAt: number): EbmlParts {
  const seek = (id: number, at: number) =>
    ebmlElement(ID.Seek, [
      ...ebmlElement(ID.SeekID, ebmlUint(id)),
      ...ebmlUintElement(ID.SeekPosition, at, POSITION_WIDTH),
    ]);
  return ebmlElement(ID.SeekHead, [...seek(ID.Info, infoAt), ...seek(ID.Tracks, tracksAt), ...seek(ID.Cues, cuesAt)]);
}

// One cue per cluster that opens with a keyframe — the places a player can start decoding from.
function cues(clusters: readonly WebmClusterShape[], clusterAt: readonly number[]): EbmlParts {
  const points: EbmlParts = [];
  clusters.forEach((c, i) => {
    if (!c.startsWithKey) return;
    points.push(
      ...ebmlElement(ID.CuePoint, [
        ...ebmlUintElement(ID.CueTime, c.time),
        ...ebmlElement(ID.CueTrackPositions, [
          ...ebmlUintElement(ID.CueTrack, TRACK_NUMBER),
          ...ebmlUintElement(ID.CueClusterPosition, clusterAt[i], POSITION_WIDTH),
        ]),
      ]),
    );
  });
  return ebmlElement(ID.Cues, points);
}

/** A frame's time in the file's own unit, whole milliseconds. */
export function webmTimeOf(timestampUs: number): number {
  return Math.round(timestampUs / 1000);
}

/** Whether a frame shown at `time` opens a new cluster rather than joining the one that is open. */
export function webmStartsCluster(current: { time: number } | null, key: boolean, time: number): boolean {
  return !current || key || time - current.time > WEBM_MAX_CLUSTER_MS;
}

function clustersOf(frames: readonly EncodedVideoFrame[]): WebmCluster[] {
  const clusters: WebmCluster[] = [];
  let current: WebmCluster | null = null;
  for (const frame of frames) {
    const time = webmTimeOf(frame.timestamp);
    if (!current || webmStartsCluster(current, frame.key, time)) {
      current = { time, startsWithKey: frame.key, blocks: [] };
      clusters.push(current);
    }
    current.blocks.push(...webmSimpleBlock(time - current.time, frame));
  }
  return clusters;
}

export function webmSimpleBlock(relativeMs: number, frame: EncodedVideoFrame): EbmlParts {
  const head = new Uint8Array(4);
  head[0] = 0x80 | TRACK_NUMBER; // the track number as a one-byte variable-length integer
  new DataView(head.buffer).setInt16(1, relativeMs);
  head[3] = frame.key ? 0x80 : 0x00;
  return ebmlElement(ID.SimpleBlock, [head, frame.data]);
}
