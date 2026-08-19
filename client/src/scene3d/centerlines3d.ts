// SPDX-License-Identifier: AGPL-3.0-or-later
import { centerlinePalette } from '../map/markerPalette.ts';
import { ANCHORED_TO_SURFACE, drawnTopHeight } from './altitude3d.ts';
import type { Altitude3DPlacement } from './altitude3d.ts';
import { featuresOf, lineStrings, propertiesOf, stringProperty } from './geoJson3d.ts';
import type { CenterlinePick } from './selection3d.ts';
import type { Scene3DBounds, Scene3DPolyline, Scene3DPosition } from './scene3dEngine.ts';

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
 * How far down a passage is, said in colour.
 *
 * The scene draws the survey over the ground rather than behind it, which is what keeps a whole
 * cave legible from every camera angle — and its one honest cost is that occlusion then says
 * nothing about depth: seen from above, a passage four hundred metres down and one ten metres down
 * are the same ink at the same place. Colour is the channel that survives that view, so depth is
 * carried in the colour of the line instead of being left to the geometry.
 *
 * The bands run from the survey's own highest point downwards, because that is the only depth this
 * drawing can state honestly: with no elevation model loaded there is no hillside to measure a
 * passage against, so how much rock is overhead is unknown, while how far below the top of the
 * cave a passage lies is exactly what the surveyors recorded. The first band keeps the colour the
 * flat map draws the same overlay in, so a cave is recognisably the same cave in both views, and
 * the sequence then turns steadily from warm to cool — an order a viewer reads as descending
 * without having to be told which end is which, and one that survives being printed in grey.
 *
 * The steps are wide near the surface and wide again far below it: most Romanian caves live in the
 * first two hundred metres, and a ramp with even steps would spend most of its range on depths
 * almost nothing reaches.
 */
export interface CenterlineDepthBand {
  /** Metres below the survey's highest point at which this band starts. */
  fromMeters: number;
  /** CSS colour the lines in this band are drawn in. */
  color: string;
}

export const CENTERLINE_DEPTH_BANDS: readonly CenterlineDepthBand[] = [
  { fromMeters: 0, color: centerlinePalette.line },
  { fromMeters: 50, color: '#8c2f6b' },
  { fromMeters: 150, color: '#6b3d9e' },
  { fromMeters: 300, color: '#2f5bb5' },
  { fromMeters: 600, color: '#0f7d8c' },
];

/**
 * The altitude the rest of the survey hangs from.
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
 * The highest surveyed point of a cave, at its recorded altitude, or nothing when no geometry
 * arrived. The caller re-states it against the same anchor the drawn lines are anchored to, so
 * whatever is pinned there sits on a line rather than a kilometre above one.
 */
function highestPosition(components: Scene3DPosition[][]): Scene3DPosition | undefined {
  let highest: Scene3DPosition | undefined;
  for (const positions of components) {
    for (const position of positions) {
      if (!highest || position.height > highest.height) {
        highest = position;
      }
    }
  }
  return highest;
}

/** Which band a drawn height falls in. Drawn heights are metres below the survey's own top. */
function depthBandIndex(height: number): number {
  const depth = -height;
  let index = 0;
  while (
    index + 1 < CENTERLINE_DEPTH_BANDS.length &&
    depth >= CENTERLINE_DEPTH_BANDS[index + 1].fromMeters
  ) {
    index += 1;
  }
  return index;
}

/** The point at which a straight leg from `from` to `to` passes through a given height. */
function crossingAt(
  from: Scene3DPosition,
  to: Scene3DPosition,
  height: number,
): Scene3DPosition {
  const span = to.height - from.height;
  const along = span === 0 ? 0 : Math.min(1, Math.max(0, (height - from.height) / span));
  return {
    longitude: from.longitude + (to.longitude - from.longitude) * along,
    latitude: from.latitude + (to.latitude - from.latitude) * along,
    height,
  };
}

/**
 * One survey component cut into the pieces that lie in each depth band, in order.
 *
 * The cut is made at the exact height the band changes rather than at whichever surveyed station
 * happens to be nearest it, so the colour changes where the depth does — a single long leg
 * dropping through three bands is drawn as three pieces, not as one piece in the colour of
 * whichever end won. Both pieces share the boundary point, so the line stays continuous.
 *
 * This does multiply the number of drawn components, but only where a survey actually crosses a
 * boundary: a shallow cave that never leaves its first band comes out as exactly the components it
 * went in as, and a flat row with no surveyed depths at all is untouched.
 */
function depthBandRuns(
  positions: readonly Scene3DPosition[],
): { band: number; positions: Scene3DPosition[] }[] {
  if (positions.length === 0) {
    return [];
  }
  const runs: { band: number; positions: Scene3DPosition[] }[] = [];
  let band = depthBandIndex(positions[0].height);
  let current: Scene3DPosition[] = [positions[0]];

  for (let index = 1; index < positions.length; index += 1) {
    const from = positions[index - 1];
    const to = positions[index];
    const target = depthBandIndex(to.height);
    // A leg may cross several boundaries; each one is found on the same straight line, so the
    // original endpoints stay the reference however many pieces come out of it.
    while (band !== target) {
      const descending = band < target;
      // Going down, the boundary crossed is the start of the band below. Coming up, it is the
      // start of the band being left.
      const boundary = descending ? band + 1 : band;
      const point = crossingAt(from, to, -CENTERLINE_DEPTH_BANDS[boundary].fromMeters);
      current.push(point);
      runs.push({ band, positions: current });
      band = descending ? boundary : boundary - 1;
      current = [point];
    }
    current.push(to);
  }
  runs.push({ band, positions: current });
  // A boundary that falls exactly on a station leaves a piece of one point behind it, which draws
  // nothing and would only cost the renderer a component to discover that.
  return runs.filter((run) => run.positions.length >= 2);
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
export function centerlinePolylines(
  collection: unknown,
  placement: Altitude3DPlacement = ANCHORED_TO_SURFACE,
): Scene3DPolyline[] {
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const centerlineId = stringProperty(properties, 'id');
    const caveId = stringProperty(properties, 'caveId');
    if (!centerlineId || !caveId) {
      continue; // Not a row this application can act on; drawing an unselectable line is worse.
    }
    const components = lineStrings(feature);
    // The server states, per row, whether the representation it served carries altitudes. It is
    // read rather than inferred: a row without them arrives as coordinate pairs, which read as
    // altitude zero, and zero is also a perfectly ordinary surveyed altitude — so the geometry
    // alone cannot tell "at sea level" from "no depths were sent for this cave".
    const flat = properties.hasZ === false;
    const anchor = flat ? 0 : anchorAltitude(properties, components);
    // Where the top of this cave is drawn. Everything below is worked out relative to the top and
    // then lifted by this, so the depth bands — which are metres below the top and nothing else —
    // are the same colours in the same places whether or not the ground has any relief on it.
    const topHeight = flat ? 0 : drawnTopHeight(anchor, placement);
    // Where chrome about this survey is pinned. One payload is shared by every line of a cave, so
    // it stands at one place rather than at each of them, and the highest surveyed point is the
    // honest choice: it is on the geometry, it is the shallowest part of it — so a label there is
    // the least buried it can be — and it is drawn at the surface, which is where the eye already
    // is. The name is the survey's own; a cave is usually surveyed under its own name, and it is
    // the only name this response carries.
    const label = stringProperty(properties, 'name');
    const top = highestPosition(components);
    const id: CenterlinePick = {
      kind: 'centerline',
      caveId,
      centerlineId,
      ...(label ? { label } : {}),
      ...(top
        ? {
            anchor: flat
              ? // A plan on the ground has no height of its own to be pinned at, so the anchor
                // says so and lets whatever draws the chrome ask the scene where the ground is.
                { ...top, height: 0, onGround: true }
              : { ...top, height: top.height - anchor + topHeight },
          }
        : {}),
    };
    for (const positions of components) {
      // A row with no altitudes is a plan and nothing more: one piece, in the first band's colour,
      // laid on the ground. Its coordinates arrived as pairs and read as height zero, which is the
      // ellipsoid — right until an elevation model is attached, from which point only the renderer
      // knows where the ground is, so it is asked to put the line there rather than told a number.
      if (flat) {
        polylines.push({
          positions,
          widthPixels: WIDTH_PIXELS,
          color: CENTERLINE_DEPTH_BANDS[0].color,
          clampToGround: true,
          id,
        });
        continue;
      }
      const anchored = positions.map((position) => ({
        ...position,
        height: position.height - anchor,
      }));
      for (const run of depthBandRuns(anchored)) {
        polylines.push({
          // The band was chosen from the depth below the cave's own top, which is the number the
          // colour means; the drawn height is that depth put back where the cave actually is.
          positions: topHeight === 0 ? run.positions : liftBy(run.positions, topHeight),
          widthPixels: WIDTH_PIXELS,
          color: CENTERLINE_DEPTH_BANDS[run.band].color,
          id,
        });
      }
    }
  }
  return polylines;
}

/** The same positions, all raised by the same number of metres. */
function liftBy(positions: readonly Scene3DPosition[], meters: number): Scene3DPosition[] {
  return positions.map((position) => ({ ...position, height: position.height + meters }));
}

/** The cave a survey line belongs to, or nothing when the line came from somewhere else. */
function caveIdOf(polyline: Scene3DPolyline): string | undefined {
  const id = polyline.id as Partial<CenterlinePick> | null | undefined;
  return typeof id?.caveId === 'string' ? id.caveId : undefined;
}

/**
 * The lines of the one cave the view is centred on, out of everything a response drew.
 *
 * The ground can only be cut away around one cave. An opening is sized to the survey it has to
 * reveal, so an outline drawn around every cave a wide view happens to hold is not a cutaway of a
 * cave at all — it is an ellipse as wide as the region, which takes the basemap off the screen
 * from horizon to horizon and replaces it with a flat floor under a few threads of survey. Worse,
 * the angle a viewer has to look from is worked out from that same outline, so a hole that wide
 * reports that it is legible from anywhere and never hands the view back.
 *
 * The cave nearest the middle of the view is the one the viewer is looking at, so it is the one
 * the opening belongs to. Every other cave stays drawn, under ground that stays whole.
 */
export function nearestCaveCenterlines(
  polylines: readonly Scene3DPolyline[],
  center: { longitude: number; latitude: number },
): Scene3DPolyline[] {
  const byCave = new Map<string, Scene3DPolyline[]>();
  for (const polyline of polylines) {
    const caveId = caveIdOf(polyline);
    if (!caveId) {
      continue;
    }
    const lines = byCave.get(caveId);
    if (lines) {
      lines.push(polyline);
    } else {
      byCave.set(caveId, [polyline]);
    }
  }
  if (byCave.size <= 1) {
    return [...byCave.values()][0] ?? [];
  }

  // Longitude is narrowed by the latitude so "nearest" means nearest on the ground: at Carpathian
  // latitudes a degree of longitude is about two thirds of a degree of latitude, and comparing the
  // two raw would pick a cave to the east over a nearer one to the north.
  const cosLatitude = Math.max(Math.cos((center.latitude * Math.PI) / 180), 1e-6);
  let nearest: Scene3DPolyline[] = [];
  let nearestDistance = Number.POSITIVE_INFINITY;

  for (const lines of byCave.values()) {
    let west = Number.POSITIVE_INFINITY;
    let east = Number.NEGATIVE_INFINITY;
    let south = Number.POSITIVE_INFINITY;
    let north = Number.NEGATIVE_INFINITY;
    for (const line of lines) {
      for (const position of line.positions) {
        west = Math.min(west, position.longitude);
        east = Math.max(east, position.longitude);
        south = Math.min(south, position.latitude);
        north = Math.max(north, position.latitude);
      }
    }
    if (!Number.isFinite(west) || !Number.isFinite(south)) {
      continue; // A cave with no drawn geometry at all cannot be the one being looked at.
    }
    // The middle of the cave rather than its nearest passage, so a sprawling system does not win
    // the whole region by reaching one arm towards the middle of the screen.
    const east0 = ((west + east) / 2 - center.longitude) * cosLatitude;
    const north0 = (south + north) / 2 - center.latitude;
    const distance = east0 * east0 + north0 * north0;
    if (distance < nearestDistance) {
      nearestDistance = distance;
      nearest = lines;
    }
  }
  return nearest;
}

/** Just the lines belonging to one cave, out of everything a response drew. */
export function caveCenterlines(
  polylines: readonly Scene3DPolyline[],
  caveId: string,
): Scene3DPolyline[] {
  return polylines.filter((polyline) => caveIdOf(polyline) === caveId);
}

/**
 * The surveyed altitude of the top of each cave in a response, by cave id.
 *
 * It is the number the anchored rendering hangs a whole cave from, and anything else drawn for
 * that cave has to hang from the same one or the two drawings separate vertically by the
 * difference. Only the server's own reported top counts here: the highest point of the geometry
 * that happened to arrive moves as the viewer pans, and a second drawing anchored to a moving
 * number would slide against the lines it belongs with.
 *
 * A row served without altitudes reports nothing, and is left out rather than reported as zero.
 */
export function centerlineTopAltitudes(collection: unknown): Map<string, number> {
  const tops = new Map<string, number>();
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const caveId = stringProperty(properties, 'caveId');
    const top = properties.topAltitudeM;
    if (!caveId || properties.hasZ === false || typeof top !== 'number' || !Number.isFinite(top)) {
      continue;
    }
    // One cave can arrive as more than one survey row; the highest top is the top of the cave.
    const held = tops.get(caveId);
    if (held === undefined || top > held) {
      tops.set(caveId, top);
    }
  }
  return tops;
}

/**
 * The ground box a set of survey lines occupies, or undefined when they hold no positions.
 *
 * Longitude and latitude only. This is what a "frame this cave" action needs, and a camera is
 * framed by the ground it has to cover: a cave eight hundred metres deep and forty metres wide
 * would, if its depth were counted, be framed from far enough away to be a dot.
 */
export function centerlineBounds(
  polylines: readonly Scene3DPolyline[],
): Scene3DBounds | undefined {
  let west = Number.POSITIVE_INFINITY;
  let south = Number.POSITIVE_INFINITY;
  let east = Number.NEGATIVE_INFINITY;
  let north = Number.NEGATIVE_INFINITY;
  for (const polyline of polylines) {
    for (const position of polyline.positions) {
      west = Math.min(west, position.longitude);
      east = Math.max(east, position.longitude);
      south = Math.min(south, position.latitude);
      north = Math.max(north, position.latitude);
    }
  }
  return Number.isFinite(west) && Number.isFinite(south) ? [west, south, east, north] : undefined;
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
