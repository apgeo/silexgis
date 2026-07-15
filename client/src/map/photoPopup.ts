// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import type Point from 'ol/geom/Point';
import Overlay from 'ol/Overlay';
import { PHOTO_LAYER_ID } from './photoLayer.ts';

/**
 * Builds the popup body for a photo feature: a thumbnail linking to the full image, plus the
 * file name. Names go through textContent (never innerHTML) so a crafted file name cannot inject
 * markup. Pure DOM construction — no OpenLayers — so it is unit-testable on its own.
 */
export function photoPopupNodes(props: Record<string, unknown>): Node[] {
  const link = document.createElement('a');
  link.href = String(props.contentUrl ?? '#');
  link.target = '_blank';
  link.rel = 'noopener';
  const img = document.createElement('img');
  img.src = String(props.thumbnailUrl ?? '');
  img.alt = typeof props.name === 'string' ? props.name : '';
  link.appendChild(img);

  const caption = document.createElement('div');
  caption.className = 'map-photo-popup-name';
  caption.textContent = typeof props.name === 'string' ? props.name : '';

  return [link, caption];
}

/**
 * Thumbnail popup for the photo overlay: clicking a photo point shows its thumbnail (linking to
 * the full image); clicking elsewhere dismisses it. The thumbnail/content URLs are the
 * server-signed ones carried in the feature properties. Returns a detach fn.
 */
export function attachPhotoPopup(map: Map): () => void {
  const element = document.createElement('div');
  element.className = 'map-photo-popup';
  // stopEvent keeps clicks on the popup (the image link) from bubbling back to the map.
  const overlay = new Overlay({ element, positioning: 'bottom-center', offset: [0, -16], stopEvent: true });
  map.addOverlay(overlay);

  const handler = (event: MapBrowserEvent) => {
    const feature = map.forEachFeatureAtPixel(event.pixel, (f) => f, {
      hitTolerance: 6,
      layerFilter: (layer) => layer.get('id') === PHOTO_LAYER_ID,
    });
    if (!feature) {
      overlay.setPosition(undefined);
      return;
    }

    element.replaceChildren(...photoPopupNodes(feature.getProperties()));
    overlay.setPosition((feature.getGeometry() as Point).getCoordinates());
  };

  map.on('singleclick', handler);
  return () => {
    map.un('singleclick', handler);
    map.removeOverlay(overlay);
  };
}
