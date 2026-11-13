// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { muxMp4 } from './mp4/mp4Muxer.ts';
import type { EncodedVideoFrame } from './video/encodedFrame.ts';
import { muxWebm } from './webm/webmMuxer.ts';

/**
 * The two video writers assemble a file in memory, and writing a long one to disk as it goes was
 * added beside that without changing it. These digests were taken from the writers as they were
 * before: a movie made the way every movie was made until then is still the same bytes.
 */

// Deterministic frames: sizes and contents that differ, a keyframe every `keyEvery`.
function frames(count: number, fps: number, keyEvery: number): EncodedVideoFrame[] {
  let seed = 12345;
  const next = () => (seed = (seed * 1103515245 + 12345) % 2147483648);
  return Array.from({ length: count }, (_, i) => {
    const data = new Uint8Array(20 + (next() % 300));
    for (let b = 0; b < data.length; b++) data[b] = next() % 256;
    const timestamp = Math.round((i * 1e6) / fps);
    return { data, timestamp, duration: Math.round(((i + 1) * 1e6) / fps) - timestamp, key: i % keyEvery === 0 };
  });
}

function digest(parts: readonly Uint8Array[]): string {
  const hash = createHash('sha256');
  for (const part of parts) hash.update(part);
  return hash.digest('hex');
}

const AVCC = Uint8Array.from([1, 0x42, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x04, 0x67, 0x42, 0x00, 0x1f, 0x01, 0x00, 0x02, 0x68, 0xce]);

describe('the in-memory video writers', () => {
  it('write a WebM as they did before a long one could go to disk', () => {
    const webm = (count: number, fps: number, keyEvery: number) =>
      digest(muxWebm({ codecId: 'V_VP9', width: 640, height: 360, fps }, frames(count, fps, keyEvery)));
    expect([webm(1, 30, 60), webm(200, 30, 60), webm(1900, 60, 4000)]).toEqual(WEBM);
  });

  it('write an MP4 as they did before a long one could go to disk', () => {
    const mp4 = (count: number, fps: number, keyEvery: number) =>
      digest(muxMp4({ width: 640, height: 360, fps, avcC: AVCC }, frames(count, fps, keyEvery)));
    expect([mp4(1, 30, 60), mp4(200, 30, 60), mp4(333, 12.5, 25)]).toEqual(MP4);
  });
});

const WEBM = [
  '5922115ea3cc06e6db72767584ac5eccc671381135a2a49e2c376d4930916c00',
  '0dc4c1113cf65b6d792f1f2f92de38fabee54affeee0a7a3b8ba265d2d25285f',
  '6c732cc05eb1f5d470c182e8542585e622bd3bef6f0d170a0d022fab188f28eb',
];
const MP4 = [
  '251bb6572b05981235aeaefec4b137f29cf6e4a922620614c67b6fdbf9f3d044',
  'f02862f093e883a164adee6a859539d533820316e1303230208999a760a62cad',
  'f621c453aaf7cfdf3754ffda8926e9d3c1855916e5abd1039edfdc51f1dcb5c0',
];
