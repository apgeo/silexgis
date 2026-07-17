// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * True when the pointer driving the map is a finger rather than a mouse.
 *
 * Deliberately a different axis from viewport width: a phone held in landscape is wide
 * enough for the desktop layout yet still needs finger-sized hit tolerances, and a touch
 * laptop needs them without becoming a phone. Width decides layout; this decides how
 * generously OL interactions hit-test.
 *
 * Read at the moment it is needed rather than cached, so a tablet that gains a mouse
 * mid-session gets mouse tolerances on the next tool arm.
 */
export function coarsePointer(): boolean {
  return window.matchMedia?.('(pointer: coarse)').matches ?? false;
}
