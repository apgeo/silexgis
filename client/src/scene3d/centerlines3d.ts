// SPDX-License-Identifier: AGPL-3.0-or-later
import { centerlinePalette } from '../map/markerPalette.ts';
import { featuresOf, lineStrings, propertiesOf, stringProperty } from './geoJson3d.ts';
import type { CenterlinePick } from './selection3d.ts';
import type { Scene3DPolyline, Scene3DPosition } from './scene3dEngine.ts';

// Cave survey lines, at the depths they were surveyed at.
//
// This is where the flat map and the scene deliberately part company. On the flat map the
// centerline overlay is excluded from hit testing altogether, because hit testing there replays
// the whole geometry of a cave for every pointer movement and a single long survey costs roughly
// a tenth of a second per move. The scene tests hits by reading back what the graphics card
// already drew, which costs the same few milliseconds whatever the geometry is, so here the
// survey lines are clickable — and clicking one selects the cave it belongs to.

/** Line width in screen pixels, matching the stroke the flat map uses for the same overlay. */
const WIDTH_PIXELS = 2;

/**
 * A survey's altitudes are heights above the sea-level datum the survey was recorded against —
 * roughly 1100 m for a cave in the Carpathians. The globe positions everything by height above
 * the reference ellipsoid, and with no elevation model loaded its surface is that ellipsoid: a
 * flat sphere at height zero, with no hillside anywhere on it for the cave to be inside. Drawing
 * the survey at its recorded altitudes would therefore hang it a kilometre in the air above a
 * smooth globe, above the entrance markers that sit on the surface and above a camera that a
 * viewer flies down to the ground — the survey would simply vanish upwards at exactly the zoom
 * that finally delivers real depths.
 *
 * So the survey is anchored: its top is placed on the surface and everything else hangs below at
 * its true distance beneath that top, which is the depth a caver reads. Relative depths within a
 * cave, the only thing this drawing can honestly show without terrain, are exact; absolute
 * altitude is not shown at all rather than shown wrongly, and no cave is drawn against a hillside
 * that is not there.
 *
 * The anchor is the top of the WHOLE centerline, which the response reports, and not the top of
 * the geometry that arrived. At close zooms the server cuts the survey to the viewport, so the
 * highest point in the payload changes as the viewer pans and anchoring to it would slide the
 * cave up and down. A response that reports no anchor falls back to the payload's own highest
 * point: less stable, but it still puts the cave where the viewer is looking rather than a
 * kilometre over their head.
 */
function anchorAltitude(
  properties: Record<string, unknown>,
  components: Scene3DPosition[][],
): number {
  const reported = properties.topAltitudeM;
  if (typeof reported === 'number' && Number.isFinite(reported)) {
    return reported;
  }
  let top = Number.NEGATIVE_INFINITY;
  for (const positions of components) {
    for (const position of positions) {
      top = Math.max(top, position.height);
    }
  }
  // A cave whose survey lies below sea level has a negative top and is anchored by it all the
  // same; only a feature with nothing in it at all falls through to the surface.
  return Number.isFinite(top) ? top : 0;
}

/**
 * The polylines for one response.
 *
 * A cave's survey arrives as one feature whose geometry has many components, and every component
 * becomes its own polyline — that is what a batched collection wants. All of them share one
 * payload object, by reference, so a click anywhere on a cave answers with the same cave and no
 * lookup table has to be maintained alongside the geometry.
 *
 * The flat map strokes a light casing under the line when it is showing full detail, to keep it
 * legible over both aerial and topographic bases. That second pass is not repeated here: it would
 * double the number of line components against a budget the server already enforces in the
 * thousands, to solve a legibility problem the scene does not have — a survey drawn over terrain
 * is read against one background, not against whichever basemap happens to be underneath.
 */
export function centerlinePolylines(collection: unknown): Scene3DPolyline[] {
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const centerlineId = stringProperty(properties, 'id');
    const caveId = stringProperty(properties, 'caveId');
    if (!centerlineId || !caveId) {
      continue; // Not a row this application can act on; drawing an unselectable line is worse.
    }
    const id: CenterlinePick = { kind: 'centerline', caveId, centerlineId };
    const components = lineStrings(feature);
    const anchor = anchorAltitude(properties, components);
    for (const positions of components) {
      polylines.push({
        // A flat row arrives at height zero throughout and its anchor is zero too, so it stays on
        // the surface and costs nothing here.
        positions: positions.map((position) => ({ ...position, height: position.height - anchor })),
        widthPixels: WIDTH_PIXELS,
        color: centerlinePalette.line,
        id,
      });
    }
  }
  return polylines;
}

/** What the last response held back, and how much of it could not be given real depths. */
export interface CenterlineLoad3DState {
  /** Rows the server could not fit in this view's budget, or gated as too large for this zoom. */
  withheldCount: number;
  /** True when at least one row was served as full survey detail rather than as the skeleton. */
  detail: boolean;
  /** Rows drawn on the surface because the representation served for them carries no altitude. */
  flatCount: number;
}

export const EMPTY_CENTERLINE_LOAD_STATE: CenterlineLoad3DState = {
  withheldCount: 0,
  detail: false,
  flatCount: 0,
};

/** Reads the counters the collection carries alongside its features. */
export function centerlineLoadState(collection: unknown): CenterlineLoad3DState {
  const source = collection as Partial<CenterlineLoad3DState> | null | undefined;
  return {
    withheldCount: typeof source?.withheldCount === 'number' ? source.withheldCount : 0,
    detail: source?.detail === true,
    flatCount: typeof source?.flatCount === 'number' ? source.flatCount : 0,
  };
}
