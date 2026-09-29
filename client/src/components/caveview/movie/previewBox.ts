// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The largest box of a movie frame's shape that fits the room it is given, in whole page pixels.
 *
 * The width is floored and the height follows it, rounded, so the box is the frame's shape to
 * within half a pixel — the tolerance the viewer's capture session allows before it refuses a
 * container as the wrong shape. A room or a frame with no area gives an empty box.
 */
export function previewBox(
  room: { width: number; height: number },
  frame: { width: number; height: number },
): { width: number; height: number } {
  if (!(room.width > 0) || !(room.height > 0) || !(frame.width > 0) || !(frame.height > 0)) {
    return { width: 0, height: 0 };
  }
  const aspect = frame.width / frame.height;
  const width = Math.floor(Math.min(room.width, room.height * aspect));
  return { width, height: Math.round(width / aspect) };
}

/**
 * The size the viewer's surface is laid out at inside a preview box, before it is scaled up to
 * fill the box.
 *
 * The preview draws a caver's label at the share of the picture the movie gives it: a label of
 * `labelSize` frame pixels is drawn at `labelSize` × (the surface's device pixels over the frame's
 * pixels). The viewer's glyph atlas draws nothing larger than `maxLabel` device pixels, so on a
 * box with many more device pixels than the frame has — a small GIF on a large or dense screen —
 * the label would be held to that and look smaller in the preview than in the file. So the surface
 * is then laid out smaller, at no more device pixels than keep the label within the atlas, and
 * shown scaled up to the box: softer, as the small file will be, but in proportion.
 *
 * Answers the box itself whenever it needs no such reduction, or labels are not drawn at all.
 */
export function previewSurface(
  box: { width: number; height: number },
  frame: { width: number; height: number },
  labelSize: number | null,
  devicePixelRatio: number,
  maxLabel: number,
): { width: number; height: number } {
  const ratio = Number.isFinite(devicePixelRatio) && devicePixelRatio > 0 ? devicePixelRatio : 1;
  if (labelSize === null || !(labelSize > 0) || !(box.width > 0) || !(frame.width > 0)) {
    return box;
  }
  const widestDevice = (frame.width * maxLabel) / labelSize;
  if (box.width * ratio <= widestDevice) {
    return box;
  }
  const reduced = previewBox({ width: widestDevice / ratio, height: box.height }, frame);
  return reduced.width > 0 ? reduced : box;
}
