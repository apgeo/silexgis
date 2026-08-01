// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import Overlay from 'ol/Overlay';
import { ENTRANCE_LAYER_ID } from './entranceLayer.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  getFeatureTypeName,
  getFeatureTypeNameByCode,
} from './featureLayer.ts';
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
        if (layerId === ENTRANCE_LAYER_ID) {
          clickable = true;
          // `name` is the entrance's own name (falling back to the cave server-side);
          // when the entrance is named, its cave's name gives the missing context.
          const name = typeof props.name === 'string' && props.name ? props.name : undefined;
          const caveName =
            typeof props.caveName === 'string' && props.caveName ? props.caveName : undefined;
          label = name && caveName && name !== caveName ? `${name} — ${caveName}` : name;
          return true;
        }
        if (layerId === SURFACE_FEATURE_LAYER_ID) {
          clickable = true;
          // Server rows carry typeCode; pending locally drawn ones only a featureTypeId.
          const typeName =
            getFeatureTypeNameByCode(props.typeCode) ?? getFeatureTypeName(props.featureTypeId);
          const name = typeof props.name === 'string' && props.name ? props.name : undefined;
          label = name && typeName ? `${name} — ${typeName}` : (name ?? typeName);
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
