// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useSyncExternalStore } from 'react';

/**
 * The media query the stylesheets use to decide that a control is being driven by a finger.
 *
 * Both halves earn their place. `(pointer: coarse)` is the touchscreen; `(hover: none)` catches a
 * device that has no hovering pointer at all, which is what makes a tooltip unreadable — and a
 * tooltip that never opens is the reason a layout branches on this rather than on width.
 *
 * Exported so a component and a stylesheet cannot drift apart: whichever of them is changed, the
 * other has to be changed with it, and a layout chosen on one rule while its hit tolerances are
 * sized by another is how a control ends up half converted.
 */
export const COARSE_POINTER_QUERY = '(hover: none), (pointer: coarse)';

/**
 * True when the viewer is driving this with a finger rather than a mouse.
 *
 * Deliberately a different axis from viewport width. A phone held in landscape is wide enough for
 * a desktop layout and still has no hover and no pixel precision, so a control that is a glyph
 * explained by a tooltip is, on that device, an unlabelled button — width alone would hand it one.
 *
 * Subscribed to rather than read once: a tablet that gains a keyboard and a trackpad changes the
 * answer without reloading the page, and a layout that only re-read it on the next render would
 * leave that viewer with finger-sized chrome until they happened to touch something else.
 */
export function useCoarsePointer(): boolean {
  const subscribe = useCallback((onChange: () => void) => {
    const query = window.matchMedia?.(COARSE_POINTER_QUERY);
    query?.addEventListener('change', onChange);
    return () => query?.removeEventListener('change', onChange);
  }, []);
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia?.(COARSE_POINTER_QUERY).matches ?? false,
    // On a server there is no pointer at all; the mouse layout is the one that renders the same
    // markup a desktop browser will then hydrate.
    () => false,
  );
}
