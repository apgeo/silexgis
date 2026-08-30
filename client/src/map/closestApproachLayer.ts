// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import { LineString, Point } from 'ol/geom';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import {
  getClosestApproachLine,
  onClosestApproachLineChanged,
  type ClosestApproachLine,
} from '../workspace/closestApproachLine.ts';
import { closestApproachPalette as palette } from './markerPalette.ts';

export const CLOSEST_APPROACH_LAYER_ID = 'closest-approach';

// Where two caves come closest, drawn as the line it is.
//
// The plan of a three-dimensional line, which is a shorter line than the one measured: two caves
// two hundred metres apart can be forty metres apart on the ground and the rest of it straight
// down. That is why the length is written beside the line rather than left to be read off the
// scale bar — the flat map cannot show the part of this measurement that is vertical, and a
// number a viewer could scale off the drawing and get a different answer for would be worse than
// no number.

const source = new VectorSource();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getClosestApproachSource(): VectorSource {
  return source;
}

export function createClosestApproachLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: approachStyle });
  layer.set('id', CLOSEST_APPROACH_LAYER_ID);
  return layer;
}

/**
 * Keeps the layer showing whatever pair was measured last. Returns a detach function.
 *
 * Drawn immediately as well as on every change: a viewer who measured a pair on a cave's page and
 * then opened the map would otherwise arrive at an empty one and have nothing to press to fix it.
 */
export function attachClosestApproachLine(): () => void {
  draw(getClosestApproachLine());
  return onClosestApproachLineChanged(draw);
}

function draw(line: ClosestApproachLine | null): void {
  source.clear(true);
  if (!line) {
    return;
  }
  const from = fromLonLat([line.from.longitude, line.from.latitude]);
  const to = fromLonLat([line.to.longitude, line.to.latitude]);

  const segment = new Feature({ geometry: new LineString([from, to]) });
  segment.set('label', line.label);
  // Both ends are marked. The line between two caves that nearly touch is a few pixels long at
  // any zoom that shows both caves, and an unmarked one of those is indistinguishable from a
  // rendering artefact.
  source.addFeatures([
    segment,
    new Feature({ geometry: new Point(from) }),
    new Feature({ geometry: new Point(to) }),
  ]);
}

function approachStyle(feature: FeatureLike): Style {
  const label = feature.get('label');
  if (typeof label !== 'string') {
    return new Style({
      image: new CircleStyle({
        radius: 4,
        fill: new Fill({ color: palette.line }),
        stroke: new Stroke({ color: palette.casing, width: 2 }),
      }),
    });
  }
  return new Style({
    stroke: new Stroke({ color: palette.line, width: 3, lineDash: [8, 5] }),
    text: new Text({
      text: label,
      font: 'bold 12px sans-serif',
      fill: new Fill({ color: palette.line }),
      stroke: new Stroke({ color: palette.casing, width: 3 }),
      offsetY: -12,
    }),
  });
}
