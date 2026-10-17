// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import type { Geometry } from 'ol/geom';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { onRowHover } from '../workspace/tableMapLink.ts';
import { getEntranceSource } from './entranceLayer.ts';
import { getSurfaceFeatureSource } from './featureLayer.ts';
import { overburdenHighlightPalette as palette } from './markerPalette.ts';

export const ROW_HIGHLIGHT_LAYER_ID = 'row-highlight';

// The thing a list's reader is pointing at, marked on the map.
//
// The list sends ids and nothing else, so the mark is drawn over what this map has itself been
// given for those ids — an entrance at the point it was drawn at (the snapped one, for a reader who
// may not place it), a feature with the geometry it arrived with. A row this map has not drawn —
// outside the window, inside a cluster, withheld — is simply not marked: the list cannot make the
// map show more than the map was shown.

const source = new VectorSource();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getRowHighlightSource(): VectorSource {
  return source;
}

export function createRowHighlightLayer(): VectorLayer {
  const layer = new VectorLayer({ source, style: highlightStyle });
  layer.set('id', ROW_HIGHLIGHT_LAYER_ID);
  return layer;
}

/**
 * The geometries drawn for these ids: an entrance by its own id or its cave's (the cave registry's
 * row is the cave), a surface feature by its id. Clusters carry no id and are never matched.
 */
export function highlightedGeometries(
  ids: ReadonlySet<string>,
  entrances: readonly FeatureLike[],
  features: readonly FeatureLike[],
): Geometry[] {
  if (ids.size === 0) {
    return [];
  }
  const found: Geometry[] = [];
  const take = (feature: FeatureLike) => {
    const geometry = feature.getGeometry();
    if (geometry && 'clone' in geometry) {
      found.push((geometry as Geometry).clone());
    }
  };
  for (const entrance of entrances) {
    if (entrance.get('cluster') === true) {
      continue;
    }
    if (ids.has(entrance.get('id') as string) || ids.has(entrance.get('caveId') as string)) {
      take(entrance);
    }
  }
  for (const feature of features) {
    if (ids.has(feature.get('id') as string)) {
      take(feature);
    }
  }
  return found;
}

/**
 * Keeps the layer marking whatever a list is pointing at. Redrawn when the pointing changes and
 * when either layer it marks reloads, so a mark follows a pan rather than staying where the old
 * features were. Returns a detach function.
 */
export function attachRowHighlight(): () => void {
  let ids: ReadonlySet<string> = new Set();
  const redraw = () => {
    source.clear(true);
    for (const geometry of highlightedGeometries(
      ids,
      getEntranceSource().getFeatures(),
      getSurfaceFeatureSource().getFeatures(),
    )) {
      source.addFeature(new Feature({ geometry }));
    }
  };
  const onLayerChange = () => {
    if (ids.size > 0) {
      redraw();
    }
  };
  const entrances = getEntranceSource();
  const features = getSurfaceFeatureSource();
  entrances.on('change', onLayerChange);
  features.on('change', onLayerChange);
  const unsubscribe = onRowHover((next) => {
    ids = next;
    redraw();
  });
  return () => {
    unsubscribe();
    entrances.un('change', onLayerChange);
    features.un('change', onLayerChange);
    source.clear(true);
  };
}

function highlightStyle(feature: FeatureLike): Style | Style[] {
  const type = feature.getGeometry()?.getType();
  if (type === 'Point' || type === 'MultiPoint') {
    return new Style({
      image: new CircleStyle({
        radius: 13,
        fill: new Fill({ color: 'rgba(0, 0, 0, 0)' }),
        stroke: new Stroke({ color: palette.mark, width: 3 }),
      }),
    });
  }
  return [
    new Style({ stroke: new Stroke({ color: palette.casing, width: 8 }) }),
    new Style({ stroke: new Stroke({ color: palette.mark, width: 4 }) }),
  ];
}
