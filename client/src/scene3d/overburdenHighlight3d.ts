// SPDX-License-Identifier: AGPL-3.0-or-later
import { overburdenHighlightPalette } from '../map/markerPalette.ts';
import {
  getOverburdenHighlight,
  onOverburdenHighlightChanged,
  type OverburdenHighlight,
} from '../workspace/overburdenHighlight.ts';
import { ANCHORED_TO_SURFACE, drawnAltitude, type Altitude3DPlacement } from './altitude3d.ts';
import type { Scene3DPolyline, Scene3DVectorSource, Scene3DVectorSources } from './scene3dEngine.ts';

// Where a reading from the overburden curve was taken, drawn in the view that can actually show
// what the reading is about.
//
// The reading is a thickness of rock between a passage and the surface over it, and that is a
// vertical fact. The flat map can only mark the plan position; here the mark is drawn as a short
// upright line standing at the passage's own altitude, so the reader sees the passage, the mark on
// it, and the hillside above — which is the whole of what the number says.
//
// <b>It hangs from the same anchor the cave's survey does.</b> With no elevation model the scene
// does not draw caves at their recorded altitudes at all: a cave's top is placed on the surface and
// the rest of it hangs below at the depth a caver reads. A mark placed at the raw surveyed altitude
// would then float somewhere unrelated to the passage it belongs to, so it is placed against the
// top of its own cave exactly as that cave's survey lines were. With an elevation model loaded the
// anchoring falls away and both sit where they were surveyed.
//
// A cave whose top the scene has not been told — its survey layer is off, or the camera is nowhere
// near it — has nothing to be hung from. The mark is then laid on the ground rather than drawn at
// a height invented for it, which is how everything else here treats geometry with no honest height
// of its own.

export const OVERBURDEN_HIGHLIGHT_SOURCE_ID = 'overburden-highlight';

/** Line width in screen pixels — wider than a survey line, because it is not one. */
const WIDTH_PIXELS = 5;

/**
 * How tall the upright mark is drawn, metres. Deliberately a fixed drawn size rather than the
 * thickness of rock the reading measured: a mark whose height was the answer would be a second
 * drawing of the same figure, at a scale nobody could read off, and a thin-roofed passage would
 * get a mark too short to see at exactly the place a reader most wants to find.
 */
const MARK_HEIGHT_M = 20;

/** The altitude of the top of a cave's survey, or nothing when the scene has not been told it. */
export type SurveyTopLookup = (caveId: string) => number | undefined;

/**
 * The polylines for the pressed reading, or none when nothing is pressed.
 *
 * Exported for its own sake: it is the whole of the arithmetic, and the arithmetic is what can be
 * wrong.
 */
export function overburdenHighlightPolylines(
  highlight: OverburdenHighlight | null,
  placement: Altitude3DPlacement,
  surveyTopOf: SurveyTopLookup,
): Scene3DPolyline[] {
  if (!highlight) {
    return [];
  }
  const top = surveyTopOf(highlight.caveId);
  // Absolute placement needs no anchor from anybody: the cave is drawn where it was surveyed, so
  // the mark on it is too.
  const anchored = placement.absolute || top !== undefined;
  const base = anchored ? drawnAltitude(highlight.altitudeM, top ?? 0, placement) : 0;

  return [
    {
      positions: [
        { longitude: highlight.longitude, latitude: highlight.latitude, height: base },
        { longitude: highlight.longitude, latitude: highlight.latitude, height: base + MARK_HEIGHT_M },
      ],
      widthPixels: WIDTH_PIXELS,
      color: overburdenHighlightPalette.mark,
      ...(anchored ? {} : { clampToGround: true }),
      // Nothing to select: this is an answer drawn over the data, not a thing in it, and a click
      // on it should select whatever survey lies under it.
      id: null,
    },
  ];
}

export interface OverburdenHighlight3DHandle {
  /** Redraws from the current reading — what a reload of the survey tops calls. */
  refresh(): void;
  /** Applies the rule for where a surveyed altitude is drawn; a change redraws. */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /** Takes the mark out of the scene. */
  detach(): void;
}

/**
 * Puts the pressed reading into the scene and keeps it there while this view is on screen.
 *
 * The survey tops are read through a function rather than passed in, because they change under
 * this: the loop that draws the caves learns them from each response, and a mark hung from the tops
 * of a moment ago would separate vertically from the passage it belongs to.
 */
export function attachOverburdenHighlight3d(
  engine: Scene3DVectorSources,
  surveyTopOf: SurveyTopLookup,
): OverburdenHighlight3DHandle {
  const source: Scene3DVectorSource<Scene3DPolyline> =
    engine.createPolylineSource(OVERBURDEN_HIGHLIGHT_SOURCE_ID);
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;

  const draw = (highlight: OverburdenHighlight | null) => {
    source.replace(overburdenHighlightPolylines(highlight, placement, surveyTopOf));
  };

  draw(getOverburdenHighlight());
  const unsubscribe = onOverburdenHighlightChanged(draw);

  return {
    refresh() {
      draw(getOverburdenHighlight());
    },
    setAltitudePlacement(next) {
      placement = next;
      draw(getOverburdenHighlight());
    },
    detach() {
      unsubscribe();
      source.remove();
    },
  };
}
