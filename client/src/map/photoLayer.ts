// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Fill, Icon, RegularShape, Stroke, Style } from 'ol/style';
import { fetchPhotoFeatures } from '../api/hooks.ts';

export const PHOTO_LAYER_ID = 'photos';

const source = new VectorSource();
const format = new GeoJSON();

// A self-contained camera pin (inline SVG data URI — no external asset, works in the CSP artifact).
const cameraSvg =
  "<svg xmlns='http://www.w3.org/2000/svg' width='26' height='26' viewBox='0 0 24 24'>" +
  "<circle cx='12' cy='12' r='11' fill='#7b2ff7' stroke='#fff' stroke-width='1.5'/>" +
  "<rect x='5.5' y='8.5' width='13' height='8.5' rx='1.6' fill='#fff'/>" +
  "<rect x='9' y='6.8' width='6' height='2.2' rx='0.8' fill='#fff'/>" +
  "<circle cx='12' cy='12.7' r='2.6' fill='#7b2ff7'/></svg>";

// Always drawn: a photo pin that vanished under decluttering would be a photo nobody can open.
const pin = new Icon({
  src: `data:image/svg+xml;utf8,${encodeURIComponent(cameraSvg)}`,
  scale: 1,
  declutterMode: 'obstacle',
});

/**
 * A photograph's pin, plus a wedge showing which way the camera looked where the picture
 * recorded a bearing.
 *
 * The wedge is worth drawing. An entrance photograph that says which way the lens faced turns
 * "somewhere on this slope" into a hole somebody can walk back to, and it is the one thing a
 * phone records about a picture that no other layer on this map can show. It is drawn as a
 * fixed-length shape in screen space rather than in metres: it is a direction, not a distance,
 * and a wedge that grew with the zoom would read as a claim about how far away the subject was.
 */
function styleOf(feature: { get: (key: string) => unknown }): Style[] {
  const bearing = feature.get('directionDegrees');
  if (typeof bearing !== 'number') {
    return [new Style({ image: pin })];
  }

  return [
    new Style({
      image: new RegularShape({
        points: 3,
        radius: 13,
        // A triangle's first vertex points up; the shape is rotated to the bearing and pushed
        // out along it so its tip leads away from the pin rather than covering it.
        displacement: [Math.sin((bearing * Math.PI) / 180) * 17, Math.cos((bearing * Math.PI) / 180) * 17],
        rotation: (bearing * Math.PI) / 180,
        rotateWithView: true,
        fill: new Fill({ color: 'rgba(123, 47, 247, 0.45)' }),
        stroke: new Stroke({ color: 'rgba(123, 47, 247, 0.9)', width: 1 }),
      }),
    }),
    new Style({ image: pin }),
  ];
}

export function createPhotoLayer(): VectorLayer {
  const layer = new VectorLayer({ source, zIndex: 12, style: styleOf });
  layer.set('id', PHOTO_LAYER_ID);
  layer.setVisible(false); // opt-in overlay
  return layer;
}

// Loading only runs while the overlay is enabled — no wasted fetches when it is toggled off.
let enabled = false;
let activeReload: (() => void) | undefined;

/** Enables/disables loading; enabling triggers an immediate load of the current extent. */
export function setPhotosEnabled(value: boolean): void {
  enabled = value;
  if (enabled) {
    activeReload?.();
  }
}

/** Bbox loading on moveend (debounced), stale responses discarded — mirrors the entrance loader. */
export function attachPhotoLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    if (!enabled) {
      return;
    }
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    try {
      const collection = await fetchPhotoFeatures(bbox);
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
  activeReload = () => void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}
