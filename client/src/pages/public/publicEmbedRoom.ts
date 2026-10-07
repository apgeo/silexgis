// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useSyncExternalStore } from 'react';

/**
 * The smallest frame a past trip's whole strip is drawn in, in CSS pixels of the frame itself.
 *
 * <b>Both figures come from measuring the strip under a finger, in the state it is tallest in.</b>
 * The whole strip is the statement, the transport and the rail, and what the drawing must keep
 * beside it is at least half the frame and never less than 150px — so a frame has room for it
 * only where it is twice the strip's height. Most of that height is the statement, which grows a
 * line for each thing it has to say: somebody is followed, the record was cut short, the link's
 * own trip is over too. With all three, in Romanian, under finger-sized controls, the strip
 * measured 419px in a frame 600px wide, 375px at 640 and 353px from 720 up — the statement's
 * sentences stop wrapping there — and a mouse's smaller controls take 54px off each figure.
 *
 * So the least box that holds it is 720 by 706, and the box asked for is a little more than
 * that. A trip's name and a team's are whatever a club called them: with a name twice as long
 * and a clock that has to name the day, the strip came to 375px at 800 wide, where at 720 it was
 * 423. At 800 by 760 both the measured states fit with the drawing keeping more than half, and
 * the ordinary one has a wrapped line to spare.
 *
 * <b>Which is more than an article usually gives a frame.</b> The pasted box is 4:3, so it is
 * 760px tall only in a column about a thousand wide; the 760 by 570 of an ordinary desktop
 * article is not room for the whole strip — there it measured 299 to 321px under a mouse, more
 * than half the box — and gets the one line, as a phone's 260px frame does.
 *
 * The frame is its own document, so its viewport is the box the snippet gave it and a media query
 * here is a question about that box — never about the article or the screen around it.
 */
export const ROOMY_FRAME = { minWidth: 800, minHeight: 760 } as const;

/** {@link ROOMY_FRAME} as the media query that asks it. */
export const ROOMY_FRAME_QUERY = `(min-width: ${ROOMY_FRAME.minWidth}px) and (min-height: ${ROOMY_FRAME.minHeight}px)`;

/**
 * True when the frame has room for a past trip's whole strip beside its drawing.
 *
 * Subscribed to rather than read once: a frame is resized with its article — a phone turned on its
 * side, a browser window dragged narrower — and the layout has to follow the box it is in.
 *
 * False where the question cannot be asked: the one line is the layout that fits every frame.
 */
export function useRoomyFrame(): boolean {
  const subscribe = useCallback((onChange: () => void) => {
    const query = window.matchMedia?.(ROOMY_FRAME_QUERY);
    query?.addEventListener('change', onChange);
    return () => query?.removeEventListener('change', onChange);
  }, []);
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia?.(ROOMY_FRAME_QUERY).matches ?? false,
    () => false,
  );
}
