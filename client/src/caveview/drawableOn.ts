// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Whether a place somebody reported may be drawn on the survey model in front of a reader.
 *
 * <b>One rule, one home, and this is the client's end of it.</b> The server answers the same
 * question for a published page — where the comparison is made before anything is handed out,
 * because a follower's browser must not be trusted to hide what it was sent — and a signed-in
 * surface is sent both ids and asks it here. What must never happen is a third and a fourth
 * spelling: this comparison was written out inline in four different files, and they disagreed on
 * the very shape the whole feature turns on. One of them drew a station whose model id was null,
 * one hid it, one treated it as "not another survey", and one refused it. Feed one row to all
 * four and a reader gets a different answer about where a person is depending on which panel they
 * happen to be looking at.
 *
 * <b>A station path is a name inside one survey.</b> `sala-mare.4` of a re-survey may be a
 * different chamber, or no place at all; two caves surveyed by the same club may both have a
 * `p.g.7`. So a report is drawable only where the model it was measured against is the model being
 * drawn, and "measured against nothing" is never drawable.
 *
 * <b>Both absences answer false, and that is deliberate rather than defensive.</b> Each of them
 * has a meaning here and neither is an invitation to guess:
 *
 * - <b>A report whose model id is absent while it still names a station is a report whose survey
 *   has been deleted.</b> Deleting a survey model nulls the pointer on every position row that
 *   named it, so this shape is not a corrupt row — it is the ordinary, expected record of a
 *   station measured in a survey that is no longer on this server. Nothing can check that name
 *   against the drawing on screen, so nothing may draw it there.
 * - <b>A report withheld from this reader arrives with its station, its depth and its model all
 *   absent.</b> That is an absence of a place rather than a place that cannot be drawn, and the
 *   callers below tell the two apart by asking whether a place was reported at all — which is why
 *   this answers only the narrow question and never the wider one.
 * - <b>A surface that does not know which model it is showing</b> has nothing to compare against.
 *
 * <b>What follows from a false is "say so", not "say nothing".</b> The position exists and is
 * known; it is unplaceable <em>here</em>. Folding it into "nobody has reported a place" would tell
 * a coordinator — or a family reading a published page — that nobody knows where a caver is, when
 * somebody does. That is the single worst sentence either surface can produce.
 */
export function drawableOn(
  recordedOn: string | null | undefined,
  modelInUse: string | null | undefined,
): boolean {
  return (
    recordedOn !== null
    && recordedOn !== undefined
    && modelInUse !== null
    && modelInUse !== undefined
    && recordedOn === modelInUse
  );
}

/** A place as a report carries it, whichever read the report came off. */
export interface ReportedPlace {
  /** The station the report named, in the spelling the viewer addresses stations by. */
  stationName: string | null;
  /** The depth somebody reported, where the report was made as a depth. */
  depthM: number | null;
  /** The survey model that report was measured against — not the one the watch is on now. */
  surveyModelId: string | null | undefined;
}

/** A place a report claims, once it is known whether the drawing on screen may show it. */
export type DrawablePlace =
  | { kind: 'station'; station: string }
  | { kind: 'depth'; depthM: number }
  | { kind: 'otherModel' };

/**
 * Where one report may be drawn on the model in use: as the station, as the depth, as a place that
 * exists somewhere else, or not at all because no place was reported.
 *
 * <b>Null means "no place was reported", and it is never the answer for a place that exists.</b>
 * The three shapes below are genuinely different things and each caller says the null one in its
 * own words — a withholding on one surface, an early-trip silence on another — because how
 * strongly that absence may be stated depends on what else that surface was told. What is decided
 * here, once, is the part that must not vary: whether there is a place at all, and whether it
 * belongs to the drawing.
 */
export function placeOnModel(
  report: ReportedPlace,
  modelInUse: string | null | undefined,
): DrawablePlace | null {
  const station = report.stationName !== null && report.stationName.length > 0
    ? report.stationName
    : null;
  const depth = report.depthM;
  if (station === null && depth === null) {
    return null;
  }
  // Said before either is read, because both of the answers below would be wrong about it. The
  // depth goes with the station rather than surviving the test: a depth is metres below the
  // watch's datum, the datum is a station of the model the watch is on, and a number measured
  // against a datum that has since been replaced is not a depth in this one.
  if (!drawableOn(report.surveyModelId, modelInUse)) {
    return { kind: 'otherModel' };
  }
  if (station !== null) {
    return { kind: 'station', station };
  }
  if (depth !== null) {
    // A depth is a position and is not a station: it is somewhere on a line the model does not
    // draw, so it is said in words rather than placed at a station it might not be at.
    return { kind: 'depth', depthM: depth };
  }
  // A report carrying neither is answered at the top; saying it again here is how this reads
  // without a non-null assertion standing where the reasoning should be.
  return null;
}
