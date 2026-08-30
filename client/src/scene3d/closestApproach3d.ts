// SPDX-License-Identifier: AGPL-3.0-or-later
import { closestApproachPalette } from '../map/markerPalette.ts';
import {
  getClosestApproachLine,
  onClosestApproachLineChanged,
  type ClosestApproachLine,
} from '../workspace/closestApproachLine.ts';
import { ANCHORED_TO_SURFACE, drawnAltitude, type Altitude3DPlacement } from './altitude3d.ts';
import type { Scene3DPolyline, Scene3DVectorSource, Scene3DVectorSources } from './scene3dEngine.ts';

// Where two caves come closest, drawn in the scene that can actually show it.
//
// This is the view the measurement was made for. The flat map can only draw the plan of the
// shortest line; here the line is drawn through the rock at the depths its two ends were surveyed
// at, between the two surveys it joins, which is the whole question — where would one dig.
//
// <b>It is hung from the same anchors the surveys are.</b> With no elevation model the scene does
// not draw caves at their recorded altitudes at all: each cave's top is placed on the surface and
// the rest of it hangs below at the depth a caver reads. Two caves are therefore each anchored to
// their own top and are not, in that view, at honest heights relative to each other. So each end
// of this line is placed against the top of the cave that end belongs to, exactly as the survey
// lines of that cave were — which puts each end on the passage it was measured from. The drawn
// line's own apparent length is then not the measured distance, and that is why the distance is
// stated in words on the panel rather than left to be read off the picture. With an elevation
// model loaded the anchoring falls away, both caves sit where they were surveyed, and the drawn
// line is the measured one.
//
// A cave whose top the scene has not been told — its survey layer is off, or the camera is
// nowhere near it — has nothing to be hung from. The line is then laid on the ground instead of
// being drawn at a height invented for it, which is how everything else here treats geometry with
// no honest height of its own.

export const CLOSEST_APPROACH_SOURCE_ID = 'closest-approach';

/** Line width in screen pixels — wider than a survey line, because it is not one. */
const WIDTH_PIXELS = 4;

/** The altitude of the top of a cave's survey, or nothing when the scene has not been told it. */
export type SurveyTopLookup = (caveId: string) => number | undefined;

/**
 * The polylines for one measured pair, or none when there is nothing measured.
 *
 * Exported for its own sake: it is the whole of the arithmetic, and the arithmetic is what can be
 * wrong.
 */
export function closestApproachPolylines(
  line: ClosestApproachLine | null,
  placement: Altitude3DPlacement,
  surveyTopOf: SurveyTopLookup,
): Scene3DPolyline[] {
  if (!line) {
    return [];
  }
  const fromTop = surveyTopOf(line.caveAId);
  const toTop = surveyTopOf(line.caveBId);

  // Absolute placement needs no anchor from anybody: the cave is drawn where it was surveyed, so
  // the ends of this line are too. Anchored placement needs both tops, because each end is hung
  // from a different one.
  const anchored = placement.absolute || (fromTop !== undefined && toTop !== undefined);

  const positions = anchored
    ? [
        {
          longitude: line.from.longitude,
          latitude: line.from.latitude,
          height: drawnAltitude(line.from.altitudeM, fromTop ?? 0, placement),
        },
        {
          longitude: line.to.longitude,
          latitude: line.to.latitude,
          height: drawnAltitude(line.to.altitudeM, toTop ?? 0, placement),
        },
      ]
    : [
        { longitude: line.from.longitude, latitude: line.from.latitude, height: 0 },
        { longitude: line.to.longitude, latitude: line.to.latitude, height: 0 },
      ];

  return [
    {
      positions,
      widthPixels: WIDTH_PIXELS,
      color: closestApproachPalette.line,
      ...(anchored ? {} : { clampToGround: true }),
      // Nothing to select: this is an answer drawn over the data, not a thing in it, and a click
      // on it should select whatever survey lies under it.
      id: null,
    },
  ];
}

export interface ClosestApproach3DHandle {
  /** Redraws from the current measurement — what a reload of the survey tops calls. */
  refresh(): void;
  /** Applies the rule for where a surveyed altitude is drawn; a change redraws. */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /** Takes the line out of the scene. */
  detach(): void;
}

/**
 * Puts the measured line into the scene and keeps it there while this view is on screen.
 *
 * The survey tops are read through a function rather than passed in, because they change under
 * this: the loop that draws the caves learns them from each response, and a line hung from the
 * tops of a moment ago would separate vertically from the surveys it joins.
 */
export function attachClosestApproach3d(
  engine: Scene3DVectorSources,
  surveyTopOf: SurveyTopLookup,
): ClosestApproach3DHandle {
  const source: Scene3DVectorSource<Scene3DPolyline> =
    engine.createPolylineSource(CLOSEST_APPROACH_SOURCE_ID);
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;

  const draw = (line: ClosestApproachLine | null) => {
    source.replace(closestApproachPolylines(line, placement, surveyTopOf));
  };

  draw(getClosestApproachLine());
  const unsubscribe = onClosestApproachLineChanged(draw);

  return {
    refresh() {
      draw(getClosestApproachLine());
    },
    setAltitudePlacement(next) {
      placement = next;
      draw(getClosestApproachLine());
    },
    detach() {
      unsubscribe();
      source.remove();
    },
  };
}
