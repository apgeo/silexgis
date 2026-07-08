// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Stroke, Style } from 'ol/style';
import { fetchCenterlineFeatures } from '../api/hooks.ts';

export const CENTERLINE_LAYER_ID = 'centerlines';

const source = new VectorSource();
const format = new GeoJSON();

// A doubled stroke (light casing under a dark dashed core) keeps the line readable on
// both aerial and topo bases; centerlines share the map with plenty of other line work.
const style = [
  new Style({ stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.7)', width: 4 }) }),
  new Style({ stroke: new Stroke({ color: '#7a1f1f', width: 2, lineDash: [6, 4] }) }),
];

export function createCenterlineLayer(): VectorLayer {
  const layer = new VectorLayer({ source, zIndex: 11, style });
  layer.set('id', CENTERLINE_LAYER_ID);
  return layer;
}

/** Bbox loading on moveend (debounced), stale responses discarded — mirrors the entrance loader. */
export function attachCenterlineLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    try {
      const collection = await fetchCenterlineFeatures(bbox);
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
    } catch {
      // Keep previous features on transient errors; next moveend retries.
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
  };
}
