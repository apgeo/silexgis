// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import type Point from 'ol/geom/Point';
import type { EntranceSelection } from '../stores/workspaceStore.ts';

/**
 * Click behavior: cluster → zoom in; entrance → publish selection; empty → clear.
 * Returns a detach function.
 */
export function attachSelection(
  map: Map,
  onPick: (selection: EntranceSelection | null) => void,
): () => void {
  const handler = (event: MapBrowserEvent) => {
    let handled = false;
    map.forEachFeatureAtPixel(
      event.pixel,
      (feature) => {
        const props = feature.getProperties();
        if (props.cluster === true) {
          const geometry = feature.getGeometry() as Point | undefined;
          if (geometry) {
            const view = map.getView();
            view.animate({ center: geometry.getCoordinates(), zoom: (view.getZoom() ?? 8) + 2, duration: 400 });
          }
          handled = true;
          return true;
        }
        if (typeof props.caveId === 'string' && typeof props.id === 'string') {
          onPick({ entranceId: props.id, caveId: props.caveId });
          handled = true;
          return true;
        }
        return false;
      },
      { hitTolerance: 6 },
    );

    if (!handled) {
      onPick(null);
    }
  };

  map.on('singleclick', handler);
  return () => map.un('singleclick', handler);
}
