// SPDX-License-Identifier: AGPL-3.0-or-later
import { surfaceFeaturePalette } from '../map/markerPalette.ts';
import {
  featuresOf,
  lineStrings,
  pointPositions,
  propertiesOf,
  stringProperty,
  type GeoJsonFeatureLike,
} from './geoJson3d.ts';
import { clusterIcon, entranceIcon, surfaceFeatureIcon } from './markerIcons3d.ts';
import type { ClusterPick, EntrancePick, FeaturePick } from './selection3d.ts';
import type { Scene3DMarker, Scene3DPolyline } from './scene3dEngine.ts';

// Entrances, clusters and surface features as scene items.
//
// Two things about clusters are load-bearing and neither is negotiable here. They are produced by
// the server, which sums entrances over a grid cell and reports the cell rather than its members;
// and the coordinates it reports are the cell's, because a protected cave's position is snapped to
// that same grid before it is ever aggregated. Clustering in the client would therefore need the
// exact positions the server is refusing to send, and re-clustering what the server already
// clustered would only hide the counts it worked out. Whatever arrives flagged as a cluster is
// drawn as a cluster and nothing else is done to it.

/**
 * Markers for the entrance overlay: an aggregation bubble per cluster the server returned, and a
 * pin per individual entrance.
 *
 * `zoom` is the integer this response was requested at. It is stamped onto every cluster payload
 * rather than read back when the cluster is clicked, because it is what fixed the size of the cell
 * the count belongs to — and by the time a viewer clicks, the camera may already be somewhere
 * else, at which point recomputing it would ask the server for the members of a different patch
 * of ground than the one that was summed.
 */
export function entranceMarkers(collection: unknown, zoom: number): Scene3DMarker[] {
  const markers: Scene3DMarker[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const positions = pointPositions(feature);
    if (positions.length === 0) {
      continue;
    }

    if (properties.cluster === true) {
      const count = typeof properties.count === 'number' ? properties.count : 0;
      const icon = clusterIcon(count);
      for (const position of positions) {
        const id: ClusterPick = {
          kind: 'cluster',
          lon: position.longitude,
          lat: position.latitude,
          count,
          zoom,
        };
        markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
      }
      continue;
    }

    const entranceId = stringProperty(properties, 'id');
    const caveId = stringProperty(properties, 'caveId');
    if (!entranceId || !caveId) {
      continue;
    }
    const icon = entranceIcon(properties.approximate === true);
    const id: EntrancePick = { kind: 'entrance', entranceId, caveId };
    for (const position of positions) {
      markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
    }
  }
  return markers;
}

/** Markers for the point features of the cross-kind overlay, drawn with their own type's symbol. */
export function surfaceFeatureMarkers(collection: unknown): Scene3DMarker[] {
  const markers: Scene3DMarker[] = [];
  for (const feature of featuresOf(collection)) {
    const id = featurePayload(feature);
    if (!id) {
      continue;
    }
    const properties = propertiesOf(feature);
    const symbol = properties.symbol;
    const icon = surfaceFeatureIcon(typeof symbol === 'string' ? symbol : null);
    for (const position of pointPositions(feature)) {
      markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
    }
  }
  return markers;
}

/**
 * The lines and area outlines of the same overlay. A fracture line or a karst area boundary is
 * real data a viewer expects to see on the ground, and drawing it as a line is both the cheapest
 * honest rendering and the same code path the survey lines already take. Areas get their rings
 * and no fill; a filled surface would hide whatever it was drawn over, which in this view is the
 * cave the viewer came for.
 *
 * They are drawn on the surface, altitude discarded. This overlay's points are placed on the
 * surface too, and the two halves of one feature have to agree: an imported geodata row can
 * carry a third ordinate on some geometries and not on others, and honouring it would leave a
 * karst area's outline floating a thousand metres over the symbols marking the same area.
 */
export function surfaceFeatureLines(collection: unknown): Scene3DPolyline[] {
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const id = featurePayload(feature);
    if (!id) {
      continue;
    }
    for (const positions of lineStrings(feature)) {
      polylines.push({
        positions: positions.map((position) => ({ ...position, height: 0 })),
        widthPixels: FEATURE_LINE_WIDTH_PIXELS,
        color: surfaceFeaturePalette.line,
        id,
      });
    }
  }
  return polylines;
}

/** Matches the flat map's 2.5 px stroke for the same overlay, rounded to whole screen pixels. */
const FEATURE_LINE_WIDTH_PIXELS = 3;

function featurePayload(feature: GeoJsonFeatureLike): FeaturePick | undefined {
  const featureId = stringProperty(propertiesOf(feature), 'id');
  return featureId ? { kind: 'feature', featureId } : undefined;
}
