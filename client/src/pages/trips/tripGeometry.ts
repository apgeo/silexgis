// SPDX-License-Identifier: AGPL-3.0-or-later
import GeoJSON from 'ol/format/GeoJSON';
import type Geometry from 'ol/geom/Geometry';
import type { TripLogInfo } from '../../api/hooks.ts';

/** The trip's own sketch, in the shape the API carries it. */
export type TripGeometry = NonNullable<TripLogInfo['geom']>;

/** The shapes a trip sketch may take. The column accepts any geometry; these are the drawable ones. */
export type TripShape = 'Point' | 'LineString' | 'Polygon';

// The map works in web mercator; the API speaks WGS84. The conversion happens in these two
// functions and nowhere else in the trip pages, so no other file has to know either projection.
const MAP_PROJECTION = 'EPSG:3857';
const DATA_PROJECTION = 'EPSG:4326';

// A format of this module's own rather than the workspace editor's: importing that one would
// pull the shared surface-feature source and its catalogue subscription into the trip bundle,
// and this needs nothing from it but two conversions.
const format = new GeoJSON();

/**
 * A stored sketch as an OpenLayers geometry the map can draw, or null when there is none.
 * A shape the reader cannot make sense of reads as none: a trip whose geometry was written by
 * something else must still open, without its page failing on the way in.
 */
export function readTripGeometry(geom: TripGeometry | null | undefined): Geometry | null {
  if (!geom) {
    return null;
  }
  try {
    return format.readGeometry(geom, {
      dataProjection: DATA_PROJECTION,
      featureProjection: MAP_PROJECTION,
    });
  } catch {
    return null;
  }
}

/** A drawn or edited geometry as the API takes it. */
export function writeTripGeometry(geometry: Geometry): TripGeometry {
  return JSON.parse(
    format.writeGeometry(geometry, {
      featureProjection: MAP_PROJECTION,
      dataProjection: DATA_PROJECTION,
    }),
  ) as TripGeometry;
}
