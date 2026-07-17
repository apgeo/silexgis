// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import type Point from 'ol/geom/Point';
import { toLonLat } from 'ol/proj';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import { ENTRANCE_LAYER_ID } from './entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from './featureLayer.ts';
import { coarsePointer } from './pointer.ts';

/**
 * Click behavior: cluster → publish a cluster selection (the panel lists its
 * members and offers zoom); entrance / surface feature → publish selection
 * (discriminated by the owning layer); empty → clear. Returns a detach function.
 */
export function attachSelection(
  map: Map,
  onPick: (selection: WorkspaceSelection | null) => void,
): () => void {
  const handler = (event: MapBrowserEvent) => {
    let handled = false;
    map.forEachFeatureAtPixel(
      event.pixel,
      (feature, layer) => {
        const props = feature.getProperties();
        if (props.cluster === true) {
          const geometry = feature.getGeometry() as Point | undefined;
          if (geometry) {
            const [lon, lat] = toLonLat(geometry.getCoordinates());
            onPick({
              kind: 'cluster',
              lon,
              lat,
              count: Number(props.count ?? 0),
              // Same rounding the entrance loader sends, so the cell matches
              // what the server aggregated.
              zoom: Math.round(map.getView().getZoom() ?? 8),
            });
          }
          handled = true;
          return true;
        }
        const layerId = layer?.get('id') as string | undefined;
        if (layerId === ENTRANCE_LAYER_ID && typeof props.caveId === 'string' && typeof props.id === 'string') {
          onPick({ kind: 'entrance', entranceId: props.id, caveId: props.caveId });
          handled = true;
          return true;
        }
        if (layerId === SURFACE_FEATURE_LAYER_ID && typeof props.id === 'string') {
          onPick({ kind: 'feature', featureId: props.id });
          handled = true;
          return true;
        }
        return false;
      },
      // A finger lands nowhere near as precisely as a cursor, and an entrance symbol is
      // only a few pixels of ink; without the wider tolerance most taps hit nothing.
      { hitTolerance: coarsePointer() ? 12 : 6 },
    );

    if (!handled) {
      onPick(null);
    }
  };

  map.on('singleclick', handler);
  return () => map.un('singleclick', handler);
}
