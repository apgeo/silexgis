// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import type Overlay from 'ol/Overlay';
import Point from 'ol/geom/Point';
import { describe, expect, it } from 'vitest';
import '../i18n';
import { attachGeofilePopup } from './geofilePopup.ts';
import { GEOFILE_LABEL_PROPERTY } from './geofileProperties.ts';
import { attachLibraryPhotoPopup } from './libraryPhotoPopup.ts';
import { libraryPhotoLayerId } from './libraryPhotoLayer.ts';
import { attachPhotoPopup } from './photoPopup.ts';
import { PHOTO_LAYER_ID } from './photoLayer.ts';

/**
 * Just enough of a map to open a balloon on: it records the overlay it is given and the click
 * handler, and answers every pixel with one feature on one layer. Invented values throughout.
 */
function mapWith(feature: Feature, layerId: string) {
  const overlays: Overlay[] = [];
  let onClick: ((event: MapBrowserEvent) => void) | undefined;
  const layer = { get: (key: string) => (key === 'id' ? layerId : undefined) };
  const map = {
    addOverlay: (overlay: Overlay) => overlays.push(overlay),
    removeOverlay: () => undefined,
    on: (_type: string, handler: (event: MapBrowserEvent) => void) => {
      onClick = handler;
    },
    un: () => undefined,
    forEachFeatureAtPixel: (
      _pixel: number[],
      callback: (hit: Feature, hitLayer: typeof layer) => unknown,
      options?: { layerFilter?: (candidate: typeof layer) => boolean },
    ) => (options?.layerFilter && !options.layerFilter(layer) ? undefined : callback(feature, layer)),
  } as unknown as Map;
  return {
    map,
    open: () => {
      onClick!({ pixel: [0, 0] } as unknown as MapBrowserEvent);
      return overlays[0].getElement()!;
    },
  };
}

/**
 * The map's element is an application region, in which a screen reader stops reading; a balloon
 * inside it is text to read, so each one says it is a document and what it is about.
 */
describe('balloons over the map', () => {
  it('a photograph says it is one, by its name', () => {
    const { map, open } = mapWith(
      new Feature({ geometry: new Point([0, 0]), name: 'intrare.jpg', thumbnailUrl: '/t', contentUrl: '/c' }),
      PHOTO_LAYER_ID,
    );
    attachPhotoPopup(map);

    const element = open();

    expect(element.getAttribute('role')).toBe('document');
    expect(element.getAttribute('aria-label')).toBe('Photo intrare.jpg');
  });

  it('a point of an imported file says where it came from, and its name', () => {
    const { map, open } = mapWith(
      new Feature({ geometry: new Point([0, 0]), [GEOFILE_LABEL_PROPERTY]: 'Punct A' }),
      'geofile:file-1',
    );
    attachGeofilePopup(map);

    const element = open();

    expect(element.getAttribute('role')).toBe('document');
    expect(element.getAttribute('aria-label')).toBe('Point from an imported file: Punct A');
  });

  it("a library's photograph names the library it came from", () => {
    const { map, open } = mapWith(
      new Feature({ geometry: new Point([0, 0]), reference: 'ref-1' }),
      libraryPhotoLayerId('photoprism'),
    );
    attachLibraryPhotoPopup(map);

    const element = open();

    expect(element.getAttribute('role')).toBe('document');
    // Before its positions have been read the library has no name to give; the balloon still
    // says what it is rather than ending on a dangling "from".
    expect(element.getAttribute('aria-label')).toBe('Photograph from a photo library');
  });
});
