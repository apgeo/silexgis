// SPDX-License-Identifier: AGPL-3.0-or-later

// The vertical exaggeration of the 3D scene: one factor by which every height in it is drawn
// taller than it is — the ground, the walls of caves, survey lines, scans, markers placed at a
// height, the opening cut over a cave and the camera itself, all together and about the ellipsoid,
// so an entrance still sits on the ground and a cave keeps its proportion to the mountain over it.
//
// The scene applies it at its own boundary and nowhere else: every height handed to it or read
// from it stays in true metres at every factor. What lives here is what the setting is — its
// range, how a stored or shared value is read back, and which of two values is in force — so the
// panel, the stored preference, a saved view and the scene cannot disagree about any of it.

/** The flattest the scene can be drawn: half its true height. */
export const MIN_EXAGGERATION = 0.5;

/** The tallest: five times its true height, which already makes a gentle karst plateau a range. */
export const MAX_EXAGGERATION = 5;

/** The steps a person chooses in. A flyby may ramp through any value between them. */
export const EXAGGERATION_STEP = 0.1;

/**
 * A factor brought into the range, or undefined for something that is not a factor at all.
 *
 * Out of range is clamped rather than refused, because a value just past an end is still plainly
 * asking for that end. Not a finite number is refused: there is no factor it could stand for, and
 * the caller keeps whatever it had.
 */
export function clampExaggeration(value: number): number | undefined {
  if (!Number.isFinite(value)) {
    return undefined;
  }
  return Math.min(MAX_EXAGGERATION, Math.max(MIN_EXAGGERATION, value));
}

/**
 * A stored or shared factor as this version reads it: a number, clamped to the range and rounded
 * to the step a person chooses in, or undefined for anything else. What a browser stored and what a
 * saved view carries were written by some version of this code, or by hand, so neither is trusted
 * as it stands.
 */
export function readExaggeration(value: unknown): number | undefined {
  if (typeof value !== 'number') {
    return undefined;
  }
  const clamped = clampExaggeration(value);
  return clamped === undefined ? undefined : roundToStep(clamped);
}

/**
 * The factor the scene is drawn at: the one a saved view brought into this session when it did,
 * else this browser's own, else true height.
 */
export function exaggerationInForce(session: number | undefined, preference: number | undefined): number {
  return readExaggeration(session) ?? readExaggeration(preference) ?? 1;
}

/** A factor as it is written for a person — `2`, `0.5`, `1.5` — in their language's own digits. */
export function formatExaggeration(factor: number, language: string): string {
  return roundToStep(factor).toLocaleString(language, { maximumFractionDigits: 1 });
}

/** To the nearest step; written out and read back so that 0.1 × 13 comes back as 1.3. */
function roundToStep(value: number): number {
  return Number((Math.round(value / EXAGGERATION_STEP) * EXAGGERATION_STEP).toFixed(1));
}
