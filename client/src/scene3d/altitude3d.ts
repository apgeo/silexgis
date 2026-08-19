// SPDX-License-Identifier: AGPL-3.0-or-later

// Where a surveyed altitude ends up in the scene.
//
// This is one rule with one home, and it is deliberately kept in a module of its own that depends
// on nothing: everything a cave is drawn as — its survey lines, its wall mesh — has to answer the
// same question the same way, and two drawings of one cave that disagree about height are worse
// than either drawing alone. A second copy of the arithmetic below is the defect this module
// exists to prevent.

/**
 * Where a surveyed altitude ends up in the scene, which depends entirely on whether the globe has
 * any relief on it.
 *
 * <b>On the bare ellipsoid there is nowhere honest to put it.</b> A survey's altitudes are heights
 * above the sea-level datum it was recorded against — roughly 1100 m for a cave in the Carpathians
 * — and the ellipsoid is a smooth sphere at height zero with no hillside anywhere on it. Drawing
 * the cave at its recorded altitudes would hang it a kilometre in the air over that sphere, above
 * the entrance markers sitting on the surface and above a camera flown down to the ground. So the
 * survey is anchored instead: its top is placed on the surface and everything else hangs below at
 * its true distance beneath that top, which is the depth a caver reads. Relative depths stay
 * exact; absolute altitude is not shown at all rather than shown wrongly.
 *
 * <b>With an elevation model there is.</b> The hillside is drawn, so the cave goes where it was
 * surveyed — inside it — and the anchoring becomes the thing that would be wrong, burying a cave
 * whose entrance is at 1100 m eleven hundred metres under its own hillside. `offsetM` is what
 * makes the two meet, and it is a property of the elevation model rather than of this
 * installation: a model serving heights above sea level, which is what an unconverted one does,
 * already speaks the same language as the survey and needs none, while one whose heights were
 * converted to the ellipsoid when it was baked needs the survey raised by the same conversion.
 *
 * <b>Neither applies to a row that arrived with no altitudes at all</b>, and this is the common
 * case rather than the exception: the compact representation the server sends for a whole region
 * at once is flat by construction, so at any ordinary browsing zoom most surveys carry a plan and
 * nothing more. There is no altitude to place such a row at, and treating the zeros it arrived
 * with as surveyed altitudes puts it at sea level — under real relief, an entire hillside below
 * the entrance markers of the very same caves. Those rows are laid on the ground instead, which
 * is both where a plan with no depths belongs and what the view already tells the viewer it did.
 */
export interface Altitude3DPlacement {
  /** True when the ground has relief, so a survey is drawn where it was surveyed. */
  absolute: boolean;
  /** Metres added to a surveyed altitude to place it against this scene's ground. */
  offsetM: number;
}

/** What a scene with no elevation model draws: every cave hung from the surface under it. */
export const ANCHORED_TO_SURFACE: Altitude3DPlacement = { absolute: false, offsetM: 0 };

/**
 * The height, in metres above the ellipsoid, that one surveyed altitude of a cave is drawn at.
 *
 * `surveyTopM` is the altitude of the highest point of that same cave, and it is what the anchored
 * rendering hangs everything from: with no relief on the globe the top of the cave goes onto the
 * surface and every other point sits at its true distance below that top, which is the depth a
 * caver reads. With relief, the cave goes where it was surveyed and the top is irrelevant.
 *
 * Every drawing of a cave passes through here, and they must all pass the SAME `surveyTopM` for
 * one cave: the survey lines and the wall mesh of a cave are two drawings of a single thing, and
 * hanging them from two different tops would separate them vertically by the difference.
 */
export function drawnAltitude(
  altitudeM: number,
  surveyTopM: number,
  placement: Altitude3DPlacement,
): number {
  return placement.absolute ? altitudeM + placement.offsetM : altitudeM - surveyTopM;
}

/**
 * The height the top of a survey is drawn at — zero on the bare ellipsoid, its own real altitude
 * against real ground. It is the rule above applied to the top itself, not a second rule.
 */
export function drawnTopHeight(surveyTopM: number, placement: Altitude3DPlacement): number {
  return drawnAltitude(surveyTopM, surveyTopM, placement);
}

/** Whether two answers to the rule are the same answer, so nothing is redrawn for no reason. */
export function samePlacement(a: Altitude3DPlacement, b: Altitude3DPlacement): boolean {
  return a.absolute === b.absolute && a.offsetM === b.offsetM;
}
