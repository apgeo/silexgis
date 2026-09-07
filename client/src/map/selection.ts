// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import type Point from 'ol/geom/Point';
import { toLonLat } from 'ol/proj';
import type { SelectedRef, WorkspaceSelection } from '../stores/workspaceStore.ts';
import { ENTRANCE_LAYER_ID } from './entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from './featureLayer.ts';
import { isHitTestable } from './hitTesting.ts';
import { TRIP_LAYER_ID } from './tripLayer.ts';
import { coarsePointer } from './pointer.ts';

/**
 * Click behavior: cluster → publish a cluster selection (the panel lists its
 * members and offers zoom); entrance / feature → publish selection
 * (discriminated by the owning layer); empty → clear. Returns a detach function.
 *
 * Holding a modifier adds to the selection instead of replacing it, which is what every list and
 * canvas anybody has used does. Only things with an identity join a set — a cluster is a place on
 * the screen rather than an object, so modifier-clicking one still opens it.
 */
export function attachSelection(
  map: Map,
  onPick: (selection: WorkspaceSelection | null) => void,
  onAdd?: (ref: SelectedRef) => void,
): () => void {
  const handler = (event: MapBrowserEvent) => {
    let handled = false;
    const original = event.originalEvent as MouseEvent | undefined;
    const adding = Boolean(onAdd) && Boolean(original?.ctrlKey || original?.metaKey || original?.shiftKey);
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
          if (adding) {
            onAdd?.({ kind: 'entrance', id: props.id });
          } else {
            onPick({ kind: 'entrance', entranceId: props.id, caveId: props.caveId });
          }
          handled = true;
          return true;
        }
        if (layerId === TRIP_LAYER_ID && typeof props.id === 'string') {
          // A trip has no place in a multi-selection: the set is a set of things on the ground
          // that can be compared with each other, and a trip is an account of a day.
          onPick({ kind: 'trip', tripId: props.id });
          handled = true;
          return true;
        }
        if (layerId === SURFACE_FEATURE_LAYER_ID && typeof props.id === 'string') {
          if (adding) {
            onAdd?.({ kind: 'feature', id: props.id });
          } else {
            onPick({ kind: 'feature', featureId: props.id });
          }
          handled = true;
          return true;
        }
        return false;
      },
      // A finger lands nowhere near as precisely as a cursor, and an entrance symbol is
      // only a few pixels of ink; without the wider tolerance most taps hit nothing.
      { hitTolerance: coarsePointer() ? 12 : 6, layerFilter: isHitTestable },
    );

    if (!handled && !adding) {
      // A modifier-click on empty ground is a miss during a multi-pick, not an instruction to
      // throw the set away — which is what clearing here would do.
      onPick(null);
    }
  };

  map.on('singleclick', handler);
  return () => map.un('singleclick', handler);
}
