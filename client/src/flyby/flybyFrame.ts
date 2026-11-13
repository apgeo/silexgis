// SPDX-License-Identifier: AGPL-3.0-or-later

// The shape of a movie's frame, and the box a scene has to be put in to draw one.
//
// A scene draws into an element at the element's own shape. A frame of 1080 by 1920 can only be
// taken from an element exactly nine wide for every sixteen tall — a pixel off, and the picture is
// a pixel short or the drawing surface refuses the size. So the element is given a size that is
// that shape *exactly*, in whole pixels of the page: a whole number of times the smallest box of
// that shape. The scene then draws each of those pixels as however many frame pixels it takes.

export interface FlybyFrameBox {
  /** Whole pixels of the page. */
  width: number;
  height: number;
  /** Frame pixels drawn for each pixel of the page; below 1 when the box is larger than the frame. */
  scale: number;
}

function greatestCommonDivisor(a: number, b: number): number {
  return b === 0 ? a : greatestCommonDivisor(b, a % b);
}

/**
 * The largest box of exactly the frame's shape that fits the space available, or undefined when not
 * even the smallest one does — a frame whose sides share no divisor has no smaller box than itself.
 */
export function frameBoxFor(
  frameWidth: number,
  frameHeight: number,
  available: { width: number; height: number },
): FlybyFrameBox | undefined {
  if (
    !Number.isInteger(frameWidth) ||
    !Number.isInteger(frameHeight) ||
    frameWidth <= 0 ||
    frameHeight <= 0
  ) {
    return undefined;
  }
  const divisor = greatestCommonDivisor(frameWidth, frameHeight);
  const unitWidth = frameWidth / divisor;
  const unitHeight = frameHeight / divisor;
  const times = Math.floor(Math.min(available.width / unitWidth, available.height / unitHeight));
  if (!(times >= 1)) {
    return undefined;
  }
  return { width: times * unitWidth, height: times * unitHeight, scale: divisor / times };
}
