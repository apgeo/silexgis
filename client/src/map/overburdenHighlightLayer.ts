// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import { Point } from 'ol/geom';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import {
  getOverburdenHighlight,
  onOverburdenHighlightChanged,
  type OverburdenHighlight,
} from '../workspace/overburdenHighlight.ts';
import { overburdenHighlightPalette as palette } from './markerPalette.ts';

export const OVERBURDEN_HIGHLIGHT_LAYER_ID = 'overburden-highlight';

// Where on the ground a reading from the overburden curve was taken.
//
// The curve's horizontal axis is distance along the passage, which no reader can turn into a place
// in their head — a dip at four hundred metres along says nothing about which valley it is under.
// So the point pressed on the chart is marked here, at the passage's own position, and the
// thickness of rock is written beside it because the flat map cannot show the part of this reading
// that is vertical: the mark is directly above a passage some tens of metres below it, and nothing
// in the plan says how far.

const source = new VectorSource();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getOverburdenHighlightSource(): VectorSource {
  return source;
}

export function createOverburdenHighlightLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: highlightStyle });
  layer.set('id', OVERBURDEN_HIGHLIGHT_LAYER_ID);
  return layer;
}

/**
 * Keeps the layer showing whichever reading was pressed last. Returns a detach function.
 *
 * Drawn immediately as well as on every change: a viewer who pressed a point on a cave's page and
 * then opened the map would otherwise arrive at an empty one and have nothing to press to fix it.
 */
export function attachOverburdenHighlight(): () => void {
  draw(getOverburdenHighlight());
  return onOverburdenHighlightChanged(draw);
}

function draw(highlight: OverburdenHighlight | null): void {
  source.clear(true);
  if (!highlight) {
    return;
  }
  const mark = new Feature({
    geometry: new Point(fromLonLat([highlight.longitude, highlight.latitude])),
  });
  mark.set('label', highlight.label);
  source.addFeature(mark);
}

function highlightStyle(feature: FeatureLike): Style {
  const label = feature.get('label');
  return new Style({
    image: new CircleStyle({
      radius: 7,
      fill: new Fill({ color: palette.mark }),
      stroke: new Stroke({ color: palette.casing, width: 3 }),
    }),
    text:
      typeof label === 'string'
        ? new Text({
            text: label,
            font: 'bold 12px sans-serif',
            fill: new Fill({ color: palette.mark }),
            stroke: new Stroke({ color: palette.casing, width: 3 }),
            offsetY: -16,
          })
        : undefined,
  });
}
