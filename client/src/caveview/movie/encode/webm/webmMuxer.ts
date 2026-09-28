// SPDX-License-Identifier: AGPL-3.0-or-later
import { checkFrameOrder, type EncodedVideoFrame } from '../video/encodedFrame.ts';
import {
  byteLength,
  EBML_ID as ID,
  ebmlElement,
  ebmlFloat,
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

interface Cluster {
  time: number;
  startsWithKey: boolean;
  blocks: EbmlParts;
}

/** The whole WebM file, as parts to be joined in order. */
export function muxWebm(track: WebmTrack, frames: readonly EncodedVideoFrame[]): EbmlParts {
  checkFrameOrder(frames);
  const clusters = clustersOf(frames);
  const last = frames[frames.length - 1];
  const durationMs = (last.timestamp + last.duration) / 1000;

  const info = ebmlElement(ID.Info, [
    ...ebmlUintElement(ID.TimecodeScale, TIMECODE_SCALE_NS),
    ...ebmlElement(ID.Duration, ebmlFloat(durationMs)),
    ...ebmlElement(ID.MuxingApp, ebmlString(WEBM_APP_NAME)),
    ...ebmlElement(ID.WritingApp, ebmlString(WEBM_APP_NAME)),
  ]);
  const tracks = ebmlElement(ID.Tracks, ebmlElement(ID.TrackEntry, trackEntry(track)));

  const clusterParts = clusters.map((c) =>
    ebmlElement(ID.Cluster, [...ebmlUintElement(ID.Timecode, c.time), ...c.blocks]),
  );

  const seekHeadSize = byteLength(seekHead(0, 0, 0));
  const infoAt = seekHeadSize;
  const tracksAt = infoAt + byteLength(info);
  const cuesAt = tracksAt + byteLength(tracks);
  const cuesSize = byteLength(cues(clusters, clusters.map(() => 0)));
  const clusterAt: number[] = [];
  let at = cuesAt + cuesSize;
  for (const parts of clusterParts) {
    clusterAt.push(at);
    at += byteLength(parts);
  }

  const segmentBody: EbmlParts = [
    ...seekHead(infoAt, tracksAt, cuesAt),
    ...info,
    ...tracks,
    ...cues(clusters, clusterAt),
    ...clusterParts.flat(),
  ];
  return [...ebmlHeader(), ...ebmlElement(ID.Segment, segmentBody)];
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
function cues(clusters: readonly Cluster[], clusterAt: readonly number[]): EbmlParts {
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

function clustersOf(frames: readonly EncodedVideoFrame[]): Cluster[] {
  const clusters: Cluster[] = [];
  let current: Cluster | null = null;
  for (const frame of frames) {
    const time = Math.round(frame.timestamp / 1000);
    if (!current || frame.key || time - current.time > WEBM_MAX_CLUSTER_MS) {
      current = { time, startsWithKey: frame.key, blocks: [] };
      clusters.push(current);
    }
    current.blocks.push(...simpleBlock(time - current.time, frame));
  }
  return clusters;
}

function simpleBlock(relativeMs: number, frame: EncodedVideoFrame): EbmlParts {
  const head = new Uint8Array(4);
  head[0] = 0x80 | TRACK_NUMBER; // the track number as a one-byte variable-length integer
  new DataView(head.buffer).setInt16(1, relativeMs);
  head[3] = frame.key ? 0x80 : 0x00;
  return ebmlElement(ID.SimpleBlock, [head, frame.data]);
}
