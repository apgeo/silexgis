// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import Overlay from 'ol/Overlay';
import { ENTRANCE_LAYER_ID } from './entranceLayer.ts';
import { entranceLabel, surfaceFeatureLabel } from './featureLabels.ts';
import { SURFACE_FEATURE_LAYER_ID } from './featureLayer.ts';
import { isHitTestable } from './hitTesting.ts';

/**
 * Name/type tooltip near the cursor for entrances and features,
 * plus a pointer cursor over any clickable feature. Returns a detach fn.
 */
export function attachHoverTooltip(map: Map): () => void {
  const element = document.createElement('div');
  element.className = 'map-hover-tooltip';
  const overlay = new Overlay({ element, offset: [12, 0], positioning: 'center-left' });
  map.addOverlay(overlay);

  const handler = (event: MapBrowserEvent) => {
    if (event.dragging) {
      overlay.setPosition(undefined);
      return;
    }

    let label: string | undefined;
    let clickable = false;
    map.forEachFeatureAtPixel(
      event.pixel,
      (feature, layer) => {
        const layerId = layer?.get('id') as string | undefined;
        const props = feature.getProperties();
        if (props.cluster === true) {
          clickable = true;
          return true; // clusters zoom on click but carry no name
        }
        // Both labels are composed by the shared rule the 3D scene reads them by too, so the
        // same place is named the same way whichever view a viewer is in.
        if (layerId === ENTRANCE_LAYER_ID) {
          clickable = true;
          label = entranceLabel(props);
          return true;
        }
        if (layerId === SURFACE_FEATURE_LAYER_ID) {
          clickable = true;
          label = surfaceFeatureLabel(props);
          return true;
        }
        return false;
      },
      { hitTolerance: 6, layerFilter: isHitTestable },
    );

    map.getTargetElement().style.cursor = clickable ? 'pointer' : '';
    if (label) {
      element.textContent = label;
      overlay.setPosition(event.coordinate);
    } else {
      overlay.setPosition(undefined);
    }
  };

  map.on('pointermove', handler);
  return () => {
    map.un('pointermove', handler);
    map.removeOverlay(overlay);
    map.getTargetElement()?.style.removeProperty('cursor');
  };
}
