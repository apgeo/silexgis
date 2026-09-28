// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * One compressed frame as a muxer takes it: the bytes a video encoder produced, when the frame is
 * shown and for how long (both in microseconds, the unit the browser's encoder counts in), and
 * whether it can be decoded without the frames before it.
 */
export interface EncodedVideoFrame {
  data: Uint8Array;
  timestamp: number;
  duration: number;
  key: boolean;
}

/** Checks what every muxer relies on: frames exist, the first is a keyframe, time never goes back. */
export function checkFrameOrder(frames: readonly EncodedVideoFrame[]): void {
  if (frames.length === 0) throw new Error('there are no frames to write');
  if (!frames[0].key) throw new Error('the first frame is not a keyframe');
  for (let i = 1; i < frames.length; i++) {
    // A writer that stores frames in the order they are shown cannot hold frames the encoder
    // reordered (B-frames); saying so beats writing a file that plays them in the wrong order.
    if (frames[i].timestamp < frames[i - 1].timestamp) {
      throw new Error(`frame ${i} is shown before the frame it follows, which this writer cannot store`);
    }
  }
}
