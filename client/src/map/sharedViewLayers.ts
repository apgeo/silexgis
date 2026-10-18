// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { fetchSharedViewEntrances, fetchSharedViewFeatures } from '../api/hooks/sharedViews.ts';
import { worldBbox } from '../geo/coords.ts';
import { entranceStyle } from './entranceLayer.ts';
import { surfaceFeaturePalette } from './markerPalette.ts';

/** Which of a shared view's two data overlays its link draws. */
export interface SharedViewOverlays {
  entrances: boolean;
  features: boolean;
}

/** A surface feature on a shared view: a dot for a point, the outline or line for the rest. */
const sharedFeatureStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: surfaceFeaturePalette.point }),
    stroke: new Stroke({ color: surfaceFeaturePalette.stroke, width: 2 }),
  }),
  stroke: new Stroke({ color: surfaceFeaturePalette.line, width: 2 }),
  fill: new Fill({ color: surfaceFeaturePalette.fill }),
});

/**
 * The data layers of a shared view's page: the public cave entrances and surface features its
 * link draws, read-only, reloaded as the view moves.
 *
 * Kept apart from the workspace map's layers on purpose. Those are bound to the signed-in map —
 * its filters, its selection, its editing — and to routes that answer for an account; these read
 * the link's own routes, which answer for nobody in particular, and nothing on this page can
 * select, edit or open a feature.
 */
export function createSharedViewLayers(
  token: string,
  overlays: SharedViewOverlays,
): { layers: VectorLayer[]; attach: (map: Map) => () => void } {
  const format = new GeoJSON();
  const entrances = new VectorSource();
  const features = new VectorSource();
  const layers: VectorLayer[] = [];
  if (overlays.features) {
    layers.push(new VectorLayer({ source: features, style: sharedFeatureStyle, zIndex: 1 }));
  }
  if (overlays.entrances) {
    layers.push(new VectorLayer({ source: entrances, style: entranceStyle, zIndex: 2 }));
  }

  const attach = (map: Map) => {
    let requestSeq = 0;
    let timer: number | undefined;
    const load = async () => {
      const view = map.getView();
      const size = map.getSize();
      if (!size) {
        return;
      }
      const bbox = worldBbox(transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326'));
      const zoom = Math.round(view.getZoom() ?? 8);
      const seq = ++requestSeq;
      const [entranceAnswer, featureAnswer] = await Promise.allSettled([
        overlays.entrances ? fetchSharedViewEntrances(token, bbox, zoom) : Promise.resolve(undefined),
        overlays.features ? fetchSharedViewFeatures(token, bbox) : Promise.resolve(undefined),
      ]);
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      // A failed answer keeps what was drawn; the next move asks again.
      if (entranceAnswer.status === 'fulfilled' && entranceAnswer.value) {
        entrances.clear(true);
        entrances.addFeatures(format.readFeatures(entranceAnswer.value, { featureProjection: 'EPSG:3857' }));
      }
      if (featureAnswer.status === 'fulfilled' && featureAnswer.value) {
        features.clear(true);
        features.addFeatures(
          format
            .readFeatures(featureAnswer.value, { featureProjection: 'EPSG:3857' })
            .filter((feature) => feature.getGeometry() !== undefined),
        );
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
  };

  return { layers, attach };
}
