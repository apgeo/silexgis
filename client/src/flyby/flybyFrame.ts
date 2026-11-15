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

/** A rectangle, in pixels of the page, measured from the top left of the view it is in. */
export interface FlybyFrameRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** How far a framed scene keeps from the controls laid over the view, in pixels of the page. */
const FRAME_MARGIN = 8;

/**
 * The part of a view a framed scene is shown in: all of it, less what the controls laid over it
 * cover — a panel down its left side, a bar or a sheet across its foot — so that what is seen of
 * the frame is all of it.
 *
 * A control counts as a side panel when it starts in the left quarter of the view and is at least
 * half its height; as something across the foot when it starts below the top third; anything else
 * stands over the scene wherever it is and is left to do so. Where so much is covered that nothing
 * is left, the whole view is used rather than none of it.
 */
export function frameArea(
  view: { width: number; height: number },
  covered: readonly FlybyFrameRect[],
): FlybyFrameRect {
  let left = 0;
  let bottom = view.height;
  for (const rect of covered) {
    if (rect.width <= 0 || rect.height <= 0) {
      continue;
    }
    if (rect.left <= view.width / 4 && rect.height >= view.height / 2 && rect.left + rect.width < view.width / 2) {
      left = Math.max(left, rect.left + rect.width + FRAME_MARGIN);
    } else if (rect.top >= view.height / 3) {
      bottom = Math.min(bottom, rect.top - FRAME_MARGIN);
    }
  }
  if (left >= view.width || bottom <= 0) {
    return { left: 0, top: 0, width: view.width, height: view.height };
  }
  return { left, top: 0, width: view.width - left, height: bottom };
}

/**
 * Where in the view the scene's element goes to draw frames of `frame`: the largest box of exactly
 * the frame's shape in `area`, centred in it, at whole pixels of the page. Undefined when not even
 * the smallest box of that shape fits.
 */
export function placeFrame(
  frame: { width: number; height: number },
  area: FlybyFrameRect,
): (FlybyFrameRect & { scale: number }) | undefined {
  const box = frameBoxFor(frame.width, frame.height, area);
  if (!box) {
    return undefined;
  }
  return {
    left: Math.round(area.left + (area.width - box.width) / 2),
    top: Math.round(area.top + (area.height - box.height) / 2),
    width: box.width,
    height: box.height,
    scale: box.scale,
  };
}
