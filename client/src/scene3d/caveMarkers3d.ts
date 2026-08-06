// SPDX-License-Identifier: AGPL-3.0-or-later
import { entranceLabel, surfaceFeatureLabel } from '../map/featureLabels.ts';
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
import type {
  Scene3DAnchor,
  Scene3DMarker,
  Scene3DPolyline,
  Scene3DPosition,
} from './scene3dEngine.ts';

// Entrances, clusters and surface features as scene items.
//
// Every marker here is dropped onto the ground, so the altitude a row carries is not where the
// marker ends up: an entrance recorded at 952 m is drawn on the surface underneath that point.
// Chrome pinned to a marker therefore has to be anchored to where it is DRAWN and not to where it
// was surveyed. The difference is not cosmetic — with the camera nine hundred metres up looking
// down, an anchor at the recorded altitude is fifty metres BEHIND the camera, the projection
// correctly answers that it is nowhere on the screen, and the label silently never appears.
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
        // No name: a cluster is a count over a patch of ground, and the patch has no name. What
        // chrome says about one is built from the count, in the viewer's own language.
        const id: ClusterPick = {
          kind: 'cluster',
          lon: position.longitude,
          lat: position.latitude,
          count,
          zoom,
          anchor: drawnOnTheGround(position),
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
    const label = entranceLabel(properties);
    for (const position of positions) {
      // A payload per position rather than one shared across them, because each carries where it
      // is: a feature with several points is several markers, and chrome pinned to one of them
      // must sit on the one that was picked and not on the first of them.
      const id: EntrancePick = {
        kind: 'entrance',
        entranceId,
        caveId,
        anchor: drawnOnTheGround(position),
        ...(label ? { label } : {}),
      };
      markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
    }
  }
  return markers;
}

/** Markers for the point features of the cross-kind overlay, drawn with their own type's symbol. */
export function surfaceFeatureMarkers(collection: unknown): Scene3DMarker[] {
  const markers: Scene3DMarker[] = [];
  for (const feature of featuresOf(collection)) {
    const payload = featurePayload(feature);
    if (!payload) {
      continue;
    }
    const properties = propertiesOf(feature);
    const symbol = properties.symbol;
    const icon = surfaceFeatureIcon(typeof symbol === 'string' ? symbol : null);
    for (const position of pointPositions(feature)) {
      const id: FeaturePick = { ...payload, anchor: drawnOnTheGround(position) };
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
 * They are laid on the ground, altitude discarded. This overlay's points are dropped onto the
 * ground too, and the two halves of the overlay have to agree: an imported geodata row can carry a
 * third ordinate on some geometries and not on others, and honouring it would leave a karst area's
 * outline floating a thousand metres over the symbols marking the same ground.
 *
 * Discarding the altitude is not by itself enough, and the difference only appears once an
 * elevation model is loaded. A height of zero is the ellipsoid, which is the ground on a smooth
 * globe and a whole hillside below it on a real one — and turning the depth test off does not
 * rescue it, because that governs what hides what and not where a point lands on the screen: from
 * any camera that is not looking exactly along the line's own vertical, a vertex eleven hundred
 * metres under the ground it belongs to projects a long way from it, and the outline is seen
 * visibly adrift from the imagery draped over that same ground. So these are marked as belonging
 * to the ground rather than given a height, and where they end up is the renderer's to decide.
 */
export function surfaceFeatureLines(collection: unknown): Scene3DPolyline[] {
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const payload = featurePayload(feature);
    if (!payload) {
      continue;
    }
    for (const positions of lineStrings(feature)) {
      const flattened = positions.map((position) => ({ ...position, height: 0 }));
      // One end of the line rather than its middle: the middle of a fracture line kilometres long
      // is a place nothing was drawn near, while an end is a point on the line itself. Anchored to
      // the ground for the same reason the line is, so chrome about it is pinned to where it is
      // actually drawn instead of to the ellipsoid underneath.
      const id: FeaturePick =
        flattened.length > 0 ? { ...payload, anchor: drawnOnTheGround(flattened[0]) } : payload;
      polylines.push({
        positions: flattened,
        widthPixels: FEATURE_LINE_WIDTH_PIXELS,
        color: surfaceFeaturePalette.line,
        clampToGround: true,
        id,
      });
    }
  }
  return polylines;
}

/** Matches the flat map's 2.5 px stroke for the same overlay, rounded to whole screen pixels. */
const FEATURE_LINE_WIDTH_PIXELS = 3;

/**
 * Where a marker dropped onto the ground actually is, for chrome to be pinned to.
 *
 * The height cannot be worked out here and is not meant to be. This module has no scene and no
 * elevation model — it turns a server response into items — and how high the ground is at a point
 * is a question only the thing drawing it can answer, and only for the tiles it is holding at that
 * moment. So the anchor says *that it is on the ground* and carries zero, the ellipsoid, as the
 * answer to fall back on. On the featureless globe a stock deployment draws, those are the same
 * surface and the fallback is exact.
 *
 * With an elevation model loaded they are not, and the error is neither small nor a matter of
 * slope: a marker dropped onto the ground goes to the terrain height, so an anchor left at zero
 * ends up the WHOLE of that height below it. In the Carpathian karst this application is for that
 * is around 1100 m, on flat ground and on a cliff alike. With the camera a couple of kilometres up
 * looking down, an anchor a kilometre under its own marker projects hundreds of pixels off the
 * bottom of the view and the label is not drawn at all; further out it is drawn visibly detached
 * from the thing it names.
 */
function drawnOnTheGround(position: Scene3DPosition): Scene3DAnchor {
  return { ...position, height: 0, onGround: true };
}

function featurePayload(feature: GeoJsonFeatureLike): FeaturePick | undefined {
  const properties = propertiesOf(feature);
  const featureId = stringProperty(properties, 'id');
  if (!featureId) {
    return undefined;
  }
  const label = surfaceFeatureLabel(properties);
  return { kind: 'feature', featureId, ...(label ? { label } : {}) };
}
