// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import { fetchEntranceFeatures } from '../api/hooks.ts';

export const ENTRANCE_LAYER_ID = 'entrances';

const source = new VectorSource();
const format = new GeoJSON();

export function createEntranceLayer(): VectorLayer {
  const layer = new VectorLayer({ source, zIndex: 10, style: entranceStyle });
  layer.set('id', ENTRANCE_LAYER_ID);
  return layer;
}

/**
 * Bbox/zoom loading strategy: reload on moveend (debounced),
 * stale responses discarded. Returns a detach function.
 */
export function attachEntranceLoader(map: Map): () => void {
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
    const zoom = Math.round(view.getZoom() ?? 8);
    const seq = ++requestSeq;
    try {
      const collection = await fetchEntranceFeatures(bbox, zoom);
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

const palette = {
  point: '#146262',
  approximate: '#d46b08',
  cluster: '#0f4c4c',
  stroke: '#ffffff',
};

function entranceStyle(feature: FeatureLike): Style {
  const props = feature.getProperties();

  if (props.cluster === true) {
    const count = Number(props.count ?? 0);
    return new Style({
      image: new CircleStyle({
        radius: Math.min(24, 12 + Math.sqrt(count)),
        fill: new Fill({ color: palette.cluster }),
        stroke: new Stroke({ color: palette.stroke, width: 2 }),
      }),
      text: new Text({
        text: String(count),
        fill: new Fill({ color: palette.stroke }),
        font: 'bold 12px sans-serif',
      }),
    });
  }

  const approximate = props.approximate === true;
  return new Style({
    image: new CircleStyle({
      radius: approximate ? 8 : 7,
      fill: new Fill({ color: approximate ? palette.approximate : palette.point }),
      stroke: new Stroke({ color: palette.stroke, width: 2, lineDash: approximate ? [2, 2] : undefined }),
    }),
  });
}
