// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingDepthCandidate } from '../../api/hooks.ts';
import { drawableOn } from '../../caveview/drawableOn.ts';

/**
 * How far a station may sit from the depth somebody reported before that is worth saying out loud.
 *
 * <b>This exists because "nearest" has no floor under it.</b> A depth report is turned into a
 * station by taking whichever station of the trip's filter is closest to the asked depth, and there
 * is no tolerance anywhere in that: the nearest station to 1200 m in a 140 m cave is the bottom of
 * the cave, and it is returned as confidently as a station half a metre away would be. So a
 * coordinator taking word down a relayed phone call and typing `-1200` for `-120` gets a party
 * stored at the deepest point of the system with nothing on screen disagreeing. Nothing about that
 * is fixed by refusing the report — the owner decided the resolution stays exactly as it is and the
 * *display* changes — so what this module answers is the one question the display needs: is the
 * distance between what was asked for and what was recorded large enough that a person should look
 * at it again.
 *
 * <b>Neither an absolute figure nor a proportion works on its own, which is why this is both.</b>
 *
 * An absolute metre tolerance is wrong at both ends of the range caves come in. Ten metres is most
 * of a 30 m sump-bypass and is one pitch in a 900 m system; a figure tight enough to catch a typo
 * in the first cries wolf on every honest report in the second, where a survey may have nothing
 * between one rebelay and the next.
 *
 * A pure proportion of the asked depth scales with the cave for free — a shallow cave is reported
 * in small numbers and a deep one in large — but it collapses near the surface, where the asked
 * depth is the thing it is a proportion of. "They are 2 m in" against a station at the entrance is
 * a gap of 2 m and 100% of what was asked, and warning there would be the crying-wolf defect in its
 * purest form.
 *
 * So the tolerance is the larger of the two: a fixed floor that holds near the surface, and a
 * proportion that opens up as the cave gets deeper.
 */

/**
 * The floor, in metres: below this, never say anything, whatever the proportion works out at.
 *
 * Fifteen metres is roughly one pitch, and a pitch is the unit a caver actually reports in — "we
 * are at the bottom of the second one" becomes a number by somebody reading a survey, or an
 * altimeter watch that is honest to about ten metres on a good day. A station fifteen metres from
 * where somebody said they were is, in almost every cave, the same pitch and the same decision
 * about where to send help. It is also comfortably inside the spacing of a normal survey, so the
 * floor does not fire merely because the survey has no station at the exact depth asked for —
 * which it usually will not.
 */
export const DEPTH_GAP_FLOOR_M = 15;

/**
 * And the proportion, once a quarter of the asked depth is more than the floor — that is, below
 * about 60 m the floor rules and below it this does.
 *
 * A quarter is well outside any honest estimate: somebody who says 120 m is not 30 m out, and
 * somebody who says 400 m is not 100 m out. It is inside the reach of the mistakes this is here to
 * catch, which are order-of-magnitude ones — a digit too many, a decimal point in the wrong place,
 * a depth read off the wrong line of a survey. And it keeps the warning quiet in exactly the caves
 * where an absolute figure would not: in a 900 m system with fifty metres of pitch between
 * stations, a report at 600 m landing on a station at 585 m is an ordinary Tuesday and says nothing.
 *
 * <b>The gap to the *next* candidate was considered as the relative and deliberately not used.</b>
 * It answers a real question — whether the filter left the choice between two parallel shafts
 * genuinely ambiguous — but that is a different defect (right depth, wrong shaft), and it is silent
 * on this one: a wildly wrong depth in a cave with one shaft has no second candidate near it at
 * all, so a rule built on the runner-up would say nothing about the very case it was needed for.
 * The candidate list is still shown beside this, which is where that other question is answered.
 */
export const DEPTH_GAP_FRACTION = 0.25;

/** How far a station may sit from a depth of this size before it is worth a word. */
export function depthGapToleranceM(askedDepthM: number): number {
  // The sign is nothing: −120 and 120 both mean 120 metres down, which is the convention the
  // report field states and the resolution rule already folds away.
  return Math.max(DEPTH_GAP_FLOOR_M, Math.abs(askedDepthM) * DEPTH_GAP_FRACTION);
}

/** What was asked for, what it landed on, and how far apart the two are. */
export interface TrackingDepthGap {
  /** The station the depth resolves to — the place that is, or would be, recorded. */
  stationName: string;
  /** How far down that station actually is, under the trip's own datum. */
  stationDepthM: number;
  /**
   * How far that station sits from the depth that was asked for.
   *
   * Taken from the answer rather than subtracted here. The resolution rule owns what a depth means
   * — which datum, which spelling, which filter — and a residual worked out on this side would be
   * a second opinion about it that could drift.
   */
  gapM: number;
  /** Whether that distance is past the tolerance above. */
  wide: boolean;
}

/**
 * The gap between a reported depth and one candidate station, or null where there is nothing to
 * compare.
 *
 * Null rather than a zero gap when the candidate is missing, and the difference matters: a station
 * this reader was never given, a filter that has moved since the report was recorded, or a preview
 * that has not come back yet all arrive as "no candidate", and none of them is evidence that the
 * position is sound. A surface drawing this must stay silent there rather than reassure.
 */
export function trackingDepthGap(
  askedDepthM: number | null | undefined,
  candidate: TrackingDepthCandidate | undefined,
): TrackingDepthGap | null {
  if (askedDepthM === null || askedDepthM === undefined || candidate === undefined) {
    return null;
  }
  return {
    stationName: candidate.stationName,
    stationDepthM: candidate.depthM,
    gapM: candidate.deltaM,
    wide: candidate.deltaM > depthGapToleranceM(askedDepthM),
  };
}

/** A depth report already on the log, as the thing a gap can be measured for. */
export interface RecordedDepthReport {
  /** The depth somebody typed. Null on a report that named a station outright, or was withheld. */
  askedDepthM: number | null;
  /** The station the resolution wrote down for it. */
  stationName: string | null;
  /**
   * The survey model that report was resolved against — not the one the watch is on now.
   *
   * These are the same thing until somebody re-points the watch, which is a supported act: a
   * corrected survey arriving mid-trip is a real thing a coordinator does, and every report already
   * on the log goes on naming the model it was made against.
   */
  surveyModelId: string | null;
}

/**
 * The gap for a report that is already on the log, or null where measuring one would be a claim
 * this reader cannot support.
 *
 * <b>A stored depth report cannot simply be re-resolved and the answer believed, and that is the
 * whole substance of this function.</b> Nothing about the report records what it was resolved
 * under: no residual is stored, by decision, and neither is the datum or the filter of the moment.
 * What is stored is a station name and a number somebody typed. Asking today's configuration what
 * that number means answers a different question — what it *would* resolve to now — and the two
 * only coincide while nothing has moved. Where they do not, a distance drawn beside the recorded
 * station is a number measured somewhere else and attached to this row.
 *
 * Both ways that goes wrong are real and neither is exotic:
 *
 * <ul>
 * <li><b>A warning appearing on a report that was exact.</b> An administrator moves the reference
 * station to the real entrance thirty metres lower — an ordinary edit, made from the configuration
 * card at the top of this very screen. Every station in the cave is now thirty metres deeper than
 * it was, so a report of 120 m that landed forty centimetres from its station is suddenly thirty
 * metres from it, and the whole log grows warnings at the same instant. That is the crying-wolf
 * failure this feature exists to avoid, arriving through the back door.</li>
 * <li><b>Silence on a report that was wildly wrong.</b> A report taken against one survey, the
 * watch then re-pointed at a fuller re-survey of the same cave. Station names commonly survive a
 * re-survey, so a station of the same name may well sit near the mistyped depth in the new model —
 * and the row would compute a small distance and say nothing at all about a party stored a
 * kilometre from where they were reported.</li>
 * </ul>
 *
 * So the gap is drawn only where re-resolving is demonstrably the same question, which is two
 * conditions:
 *
 * <ol>
 * <li><b>The report was made against the model the watch is on now.</b> A depth is metres below the
 * watch's datum and the datum is a station of a model; a number measured against a model that has
 * since been replaced is not a depth in this one. The surface that draws the party on the survey
 * already refuses on exactly this test, and refusing here for the same reason keeps the two
 * agreeing about what a stored place means.</li>
 * <li><b>Re-resolving the stored depth reproduces the station that was stored.</b> The recording
 * path writes down the nearest candidate, and this reading asks for the candidates in the same
 * order under the same spelling — so if the configuration has not moved, the stored station is
 * necessarily still the first answer. If it is not first, something has moved since, and the only
 * honest thing to say about the distance is nothing. This is a proof by contradiction rather than a
 * record: it cannot tell a moved datum from an unmoved one directly, it can only notice when the
 * consequences no longer hold.</li>
 * </ol>
 *
 * <b>What that leaves uncovered, stated rather than glossed.</b> A datum edit that happens to leave
 * the same station nearest still changes the number, and this cannot detect it — closing that would
 * mean storing what each report was resolved under, which is a schema change and was decided
 * against with the trade-off understood. What survives is narrower and defensible: the row remarks
 * only on a distance that is true of the configuration in force, between a station and a depth that
 * configuration genuinely relates, which is the same distance anybody reading the survey today
 * would measure for themselves.
 */
export function recordedDepthGap(
  report: RecordedDepthReport,
  watchSurveyModelId: string | null | undefined,
  candidatesNow: readonly TrackingDepthCandidate[] | undefined,
): TrackingDepthGap | null {
  if (report.askedDepthM === null || report.stationName === null) {
    return null;
  }
  // Asked through the one rule that decides whether a stored place belongs to the model in use,
  // rather than compared here: a second spelling of it is how this row comes to measure a distance
  // the panel next door refuses to draw. Either side absent is not a match — a report whose survey
  // was deleted keeps its station and loses its model, and a watch on no model can resolve nothing
  // — and silence is the safe end of both.
  if (!drawableOn(report.surveyModelId, watchSurveyModelId)) {
    return null;
  }
  const nearestNow = candidatesNow?.[0];
  if (nearestNow === undefined || nearestNow.stationName !== report.stationName) {
    return null;
  }
  return trackingDepthGap(report.askedDepthM, nearestNow);
}
