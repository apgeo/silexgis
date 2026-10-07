// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveDetail, MapLayerInfo, TripLogInfo } from '../../api/hooks.ts';
import type { TripGeometry } from './tripGeometry.ts';
import { tripGeometrySummary } from './tripGeometrySummary.ts';

// What goes on the map a trip's write-up is downloaded with.
//
// The map is a picture this reader's own browser draws, and it is drawn out of answers the
// write-up's page already holds: the trip as the page read it, and each cave the trip names as the
// page read that cave to print its name. Nothing here asks the server anything. That is the whole
// design — the server has already decided, per reader, which caves a trip names, where each of
// them may be said to be and whether exactly, and a second question put for the sake of a picture
// would be a second place those decisions are made.
//
// So everything below is arithmetic on values handed in, and it is deliberately narrow: a shape
// is drawn when the answer carried it as an exact position, and otherwise it is not drawn at all.

/**
 * The three things a trip can be drawn as. They are told apart on the picture by outline as well
 * as by colour, the way the workspace map already tells them apart, because they are three
 * different claims: where the party worked, where it met, and where a cave it names is.
 */
export type ReportMapShapeKind = 'sketch' | 'meeting' | 'cave';

export interface ReportMapShape {
  kind: ReportMapShapeKind;
  /** The geometry exactly as the answer carried it: GeoJSON, longitude then latitude. */
  geometry: TripGeometry;
  /** A cave's name, written beside its mark. The trip's own two shapes carry none. */
  label?: string;
}

export interface TripReportMapContent {
  shapes: ReportMapShape[];
}

/** The part of a cave's own read that says where it is, and how well this reader may know it. */
export type CaveAsRead = Pick<CaveDetail, 'name' | 'geom' | 'approximateLocation'>;

/** A longitude and a latitude that are both real numbers, and nothing else. */
function isPosition(value: unknown): value is [number, number] {
  return (
    Array.isArray(value) &&
    value.length >= 2 &&
    typeof value[0] === 'number' &&
    typeof value[1] === 'number' &&
    Number.isFinite(value[0]) &&
    Number.isFinite(value[1])
  );
}

/**
 * What the picture shows, or null when there is nothing to show.
 *
 * `trip` is the trip as its page read it. `caves` holds, by id, the answers the page already has
 * for the caves it reads by name; a cave it has no answer for is simply absent.
 *
 * Three rules, and the tests beside this hold each one:
 *
 *  - Only the trip's own list is walked. A cave the page happens to hold an answer for — from
 *    another screen, another trip — is not on this trip's map unless this trip's read named it.
 *    The list is where the server took out every cave this reader may not open or may not place.
 *  - A cave whose answer carries no position draws nothing. That is a position withheld, and a
 *    picture is not where it comes back.
 *  - A cave whose position arrived approximate draws nothing either. Such a position is a point
 *    snapped to a grid kilometres wide; drawn beside the trip's own exact sketch it would be a
 *    precise-looking mark in the wrong place, and the sketch beside it would say where the cave
 *    really is. The server keeps such a cave off the trip's list for that reason, so this is the
 *    same rule held a second time, on the side that draws.
 */
export function tripReportMapContent(
  trip: Pick<TripLogInfo, 'geom' | 'meetingGeom' | 'caveIds'>,
  caves: ReadonlyMap<string, CaveAsRead | undefined>,
): TripReportMapContent | null {
  const shapes: ReportMapShape[] = [];

  // A shape with no positions in it is no shape, and is left out the way the page leaves it out.
  if (trip.geom && tripGeometrySummary(trip.geom)) {
    shapes.push({ kind: 'sketch', geometry: trip.geom });
  }
  if (trip.meetingGeom && tripGeometrySummary(trip.meetingGeom)) {
    shapes.push({ kind: 'meeting', geometry: trip.meetingGeom });
  }

  for (const id of trip.caveIds) {
    const cave = caves.get(id);
    if (!cave?.geom || cave.approximateLocation) {
      continue;
    }
    if (cave.geom.type !== 'Point' || !isPosition(cave.geom.coordinates)) {
      continue;
    }
    shapes.push({ kind: 'cave', geometry: cave.geom, label: cave.name });
  }

  return shapes.length > 0 ? { shapes } : null;
}

/**
 * The one background a document's map may be drawn over, or null when the installation offers
 * none.
 *
 * A tile shown on a screen and a tile copied into a file that is then mailed are different uses,
 * and a source's terms decide the second separately. Which sources allow it is stated by the
 * installation's own catalogue, source by source, and that statement is all this goes by: it
 * never judges a source by its address or its name.
 *
 * The default background when it may be copied — so the picture is drawn over the map its reader
 * sees on the screen — and otherwise the first that may, in the catalogue's own order. The choice
 * does not follow whichever background this reader last picked in the workspace: two people
 * downloading the same trip get the same map under it.
 */
export function documentBasemap(catalog: readonly MapLayerInfo[] | undefined): MapLayerInfo | null {
  const usable = (catalog ?? [])
    .filter(
      (layer) =>
        layer.inDocuments === true &&
        layer.isBase &&
        layer.layerKind === 'xyz' &&
        // The credit is what the source's terms ask for beside the picture. The catalogue
        // already refuses to mark a source without one; a row that reached here without it is
        // not drawn rather than drawn uncredited.
        !!layer.attribution?.trim(),
    )
    .sort((a, b) => a.sortOrder - b.sortOrder);
  return usable.find((layer) => layer.isDefault) ?? usable[0] ?? null;
}
