// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DScreenPosition } from './scene3dEngine.ts';

// Where a piece of chrome pinned to a place in the scene actually goes.
//
// The engine answers where a position lands on the drawing surface and tests no bounds while doing
// it, so a point off the side of the view projects to a pixel outside the surface rather than to
// nothing. Deciding what to do about that is this module's job, and it is here rather than in the
// component because it is arithmetic with edge cases and no DOM: a component that did it inline
// could only be tested by measuring a rendered box.
//
// What is deliberately NOT decided here is whether the anchor is hidden behind the globe. The
// scene draws its cave data without depth testing — a marker on the far side of the planet is
// drawn over the near side rather than being occluded by it — so a label that hid itself there
// would disagree with the icon it names. Chrome and icon are both drawn, or neither is; the one
// case where there is genuinely nothing to name is an anchor behind the camera, and the engine
// answers that with no pixel at all.

/** How big the thing being placed is, and how big the surface it is placed on is. */
export interface OverlayBox {
  widthPixels: number;
  heightPixels: number;
}

/** A box on the drawing surface, measured from its top left corner. */
export interface OverlayRect extends OverlayBox {
  leftPixels: number;
  topPixels: number;
}

export interface OverlayPlacementOptions {
  /** Where the engine says the anchor landed, or undefined when it has no answer. */
  screen: Scene3DScreenPosition | undefined;
  /** The drawing surface, in CSS pixels. */
  surface: OverlayBox;
  /** The element being placed, in CSS pixels. */
  element: OverlayBox;
  /** Gap between the anchor and the element, in CSS pixels. */
  offsetPixels: number;
  /**
   * How far outside the surface the anchor may drift before the chrome is taken down rather than
   * pinned to the edge. Anchors do not stop existing at the edge of the screen — a cave a little
   * way off the side is still projected — and clamping one of those to the border would leave a
   * label pointing confidently at nothing.
   */
  marginPixels?: number;
  /**
   * A patch of the surface this element must try to stay off, in surface pixels — the corner the
   * scene's own controls stand in.
   *
   * A label pinned to a place in the world goes wherever that place is, and one of the places it
   * goes is underneath the buttons. Whoever is looking then cannot read the label, cannot reach
   * its close button, and — on a phone, where those buttons are the only route to the camera
   * positions and every layer control — is left tapping a button they can see and getting nothing.
   * Which of the two wins the tap is decided by the stacking order and not here; what this does is
   * keep them from being in the same place to begin with.
   */
  reserved?: OverlayRect;
}

export interface OverlayPlacement {
  /** False when nothing should be shown; the other fields are then meaningless. */
  visible: boolean;
  /** Distance from the surface's left edge to the element's left edge, in CSS pixels. */
  left: number;
  /** Distance from the surface's top edge to the element's top edge, in CSS pixels. */
  top: number;
  /** Which side of the anchor the element ended up on, for a pointer or a tail to follow. */
  side: 'right' | 'left';
}

const HIDDEN: OverlayPlacement = { visible: false, left: 0, top: 0, side: 'right' };

/**
 * Places an element beside an anchor, flipping and clamping so that it stays on the surface.
 *
 * To the right of the anchor by default, because that is where the flat map puts the same thing
 * and a viewer's eye already goes there. It flips to the left rather than being clamped when there
 * is no room, because clamping would slide the label off its own anchor and leave it naming
 * whatever it happened to come to rest over.
 *
 * Vertically it is centred on the anchor and then clamped, which is the opposite choice: a label
 * nudged up or down still points at the right thing, while flipping it would make it jump about
 * as the camera moves.
 */
export function placeOverlay(options: OverlayPlacementOptions): OverlayPlacement {
  const { screen, surface, element, offsetPixels } = options;
  const margin = options.marginPixels ?? 0;
  if (!screen || !Number.isFinite(screen.x) || !Number.isFinite(screen.y)) {
    return HIDDEN;
  }
  if (
    screen.x < -margin ||
    screen.y < -margin ||
    screen.x > surface.widthPixels + margin ||
    screen.y > surface.heightPixels + margin
  ) {
    return HIDDEN;
  }

  const rightLeft = screen.x + offsetPixels;
  const fitsRight = rightLeft + element.widthPixels <= surface.widthPixels;
  const side: 'right' | 'left' = fitsRight ? 'right' : 'left';
  const left = fitsRight ? rightLeft : screen.x - offsetPixels - element.widthPixels;

  const top = screen.y - element.heightPixels / 2;
  // Clamped in both axes even on the side that fitted: an element wider than the surface it is
  // on has no side that fits, and the honest answer there is the left edge rather than a
  // negative offset that scrolls the page.
  const placed = {
    visible: true as const,
    left: clamp(left, 0, Math.max(0, surface.widthPixels - element.widthPixels)),
    top: clamp(top, 0, Math.max(0, surface.heightPixels - element.heightPixels)),
    side,
  };
  return options.reserved ? movedClearOf(placed, options.reserved, surface, element) : placed;
}

/**
 * Moves a placed element off the reserved patch, or leaves it where it is when it cannot be moved
 * off it without going off the surface as well.
 *
 * Vertically first, and towards whichever edge of the patch is nearer, because the reserved patch
 * is a corner: sliding out of a corner sideways would carry the element across its own anchor and
 * hide the very thing it names, while sliding it up or down leaves it beside the anchor still.
 * Sideways is the fallback for the case where neither vertical direction has room — a short view,
 * or a patch that runs the whole height of it.
 *
 * "Leave it where it is" is a real answer and not a failure: whatever is being kept clear of is
 * chrome the viewer drives, and that is drawn over this and takes its own presses regardless, so
 * the worst case here is a label partly hidden rather than a button that cannot be pressed.
 */
function movedClearOf(
  placement: OverlayPlacement,
  reserved: OverlayRect,
  surface: OverlayBox,
  element: OverlayBox,
): OverlayPlacement {
  const overlapsHorizontally =
    placement.left < reserved.leftPixels + reserved.widthPixels &&
    placement.left + element.widthPixels > reserved.leftPixels;
  const overlapsVertically =
    placement.top < reserved.topPixels + reserved.heightPixels &&
    placement.top + element.heightPixels > reserved.topPixels;
  if (!overlapsHorizontally || !overlapsVertically) {
    return placement;
  }

  const below = reserved.topPixels + reserved.heightPixels;
  const above = reserved.topPixels - element.heightPixels;
  const fitsBelow = below + element.heightPixels <= surface.heightPixels;
  const fitsAbove = above >= 0;
  if (fitsBelow && (!fitsAbove || below - placement.top <= placement.top - above)) {
    return { ...placement, top: below };
  }
  if (fitsAbove) {
    return { ...placement, top: above };
  }

  const toTheLeft = reserved.leftPixels - element.widthPixels;
  const toTheRight = reserved.leftPixels + reserved.widthPixels;
  if (toTheLeft >= 0) {
    return { ...placement, left: toTheLeft };
  }
  if (toTheRight + element.widthPixels <= surface.widthPixels) {
    return { ...placement, left: toTheRight };
  }
  return placement;
}

function clamp(value: number, low: number, high: number): number {
  return Math.min(Math.max(value, low), high);
}
