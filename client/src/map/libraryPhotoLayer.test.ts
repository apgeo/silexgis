// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { Icon, Style } from 'ol/style';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { libraryPhotoPalette } from './markerPalette.ts';

const fetchLibraryPhotoFeatures = vi.fn();

vi.mock('../api/hooks.ts', () => ({
  fetchLibraryPhotoFeatures: (...args: unknown[]) => fetchLibraryPhotoFeatures(...args),
}));

const {
  attachLibraryPhotoLoader,
  createLibraryPhotoLayer,
  getLibraryPhotoLoadState,
  libraryPhotoLayerId,
  libraryPhotoSourceOf,
  setLibraryPhotosEnabled,
  setLibraryPhotoPictures,
  getLibraryPhotoPictures,
  LIBRARY_PHOTO_PICTURE_LIMIT,
} = await import('./libraryPhotoLayer.ts');

/** One photograph, in the shape the endpoint answers with. Invented, and deliberately not a place. */
const collection = (overrides: Record<string, unknown> = {}) => ({
  type: 'FeatureCollection',
  features: [
    {
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [10, 10] },
      properties: { source: 'photoprism', reference: 'abc', title: 'a picture' },
    },
  ],
  source: 'photoprism',
  libraryName: 'PhotoPrism',
  picturesAvailable: true,
  pictureUrlTemplate: '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}',
  readAt: '2026-09-03T08:00:00Z',
  truncated: false,
  omittedCount: 0,
  ...overrides,
});

/**
 * The map the loader listens to, reduced to what it actually uses. A real map needs a rendered
 * target before it will report a size, and none of what is under test here is about rendering.
 */
function stubMap() {
  const handlers = new Set<() => void>();
  const map = {
    getSize: () => [800, 600],
    getView: () => ({ calculateExtent: () => [1000, 2000, 3000, 4000] }),
    on: (_type: string, handler: () => void) => handlers.add(handler),
    un: (_type: string, handler: () => void) => handlers.delete(handler),
  };
  return { map: map as unknown as Map, moveEnd: () => handlers.forEach((h) => h()) };
}

describe('libraryPhotoLayer ids', () => {
  it('round-trips a library name through a layer id', () => {
    expect(libraryPhotoSourceOf(libraryPhotoLayerId('photoprism'))).toBe('photoprism');
  });

  it('does not claim a layer belonging to something else', () => {
    // The click handler and the layer panel both decide what a layer is from its id alone, so an
    // id that merely mentions photographs must not be read as one of these.
    expect(libraryPhotoSourceOf('photos')).toBeUndefined();
    expect(libraryPhotoSourceOf(undefined)).toBeUndefined();
  });
});

describe('libraryPhotoLayer styling', () => {
  /** The pin one library is drawn with, read back out of the icon the layer styles its features with. */
  const pin = (source: string): string => {
    const style = createLibraryPhotoLayer(source).getStyle() as () => Style[];
    return decodeURIComponent((style()[0].getImage() as Icon).getSrc() ?? '');
  };

  it('gives each library its own colour, because a photograph both of them hold draws two pins on one point', () => {
    // Two libraries indexing one drive really do produce two pins on the same coordinate. That is
    // not a defect to hide — it is the comparison somebody running both is looking for — so the
    // pins have to tell themselves apart at a glance.
    expect(pin('immich')).toContain(libraryPhotoPalette.immich);
    expect(pin('photoprism')).toContain(libraryPhotoPalette.photoprism);
    expect(pin('immich')).not.toBe(pin('photoprism'));
  });

  it('still draws a library this build has no colour for', () => {
    // A pin in the wrong colour is a photograph somebody can click. No pin at all is a library
    // that looks empty, which is the one thing this overlay must never say by accident.
    expect(pin('a-library-added-later')).toContain(libraryPhotoPalette.photoprism);
  });
});

describe('libraryPhotoLayer loading', () => {
  let detach: () => void;
  let moveEnd: () => void;

  beforeEach(() => {
    vi.useFakeTimers();
    fetchLibraryPhotoFeatures.mockReset();
    const stub = stubMap();
    moveEnd = stub.moveEnd;
    detach = attachLibraryPhotoLoader(stub.map);
  });

  afterEach(() => {
    setLibraryPhotosEnabled('immich', false);
    setLibraryPhotosEnabled('photoprism', false);
    detach();
    vi.useRealTimers();
  });

  it('draws what the library answered and says how much, when it was read, and by whom', async () => {
    fetchLibraryPhotoFeatures.mockResolvedValue(collection());
    const layer = createLibraryPhotoLayer('photoprism');

    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok'));

    const state = getLibraryPhotoLoadState('photoprism');
    expect(state.shownCount).toBe(1);
    expect(state.libraryName).toBe('PhotoPrism');
    expect(state.readAt).toBe('2026-09-03T08:00:00Z');
    expect(state.pictureUrlTemplate).toContain('{reference}');
    expect(layer.getSource()?.getFeatures()).toHaveLength(1);
  });

  it('reports a library that did not answer without emptying the map of what it last said', async () => {
    // The two states this pair exists to keep apart: a library that answered and holds nothing
    // here, and a library that did not answer at all. Both draw no new pins, and only this says
    // which happened — so the failing case is asserted beside the empty one, in one test.
    fetchLibraryPhotoFeatures.mockResolvedValue(collection());
    const layer = createLibraryPhotoLayer('photoprism');
    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok'));

    fetchLibraryPhotoFeatures.mockResolvedValue(collection({ features: [] }));
    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').shownCount).toBe(0));
    expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok');
    expect(layer.getSource()?.getFeatures()).toHaveLength(0);

    fetchLibraryPhotoFeatures.mockResolvedValue(collection());
    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').shownCount).toBe(1));

    fetchLibraryPhotoFeatures.mockRejectedValue(new Error('the library did not answer'));
    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').reach).toBe('unreachable'));
    // The pins stay: they are the last positions the library gave, and clearing them would be the
    // claim that there is nothing here, which is a different and false answer.
    expect(layer.getSource()?.getFeatures()).toHaveLength(1);
  });

  it('reads each library on its own, so one that stops answering does not empty the other', async () => {
    // The whole reason an installation runs two: they are separate products with separate
    // databases, separate storage and separate uptime. One request each, never one for both — a
    // joined request is as slow as the slower of them and as broken as the more broken one.
    const immich = createLibraryPhotoLayer('immich');
    const photoprism = createLibraryPhotoLayer('photoprism');

    fetchLibraryPhotoFeatures.mockImplementation((source: string) =>
      Promise.resolve(
        collection({ source, libraryName: source === 'immich' ? 'Immich' : 'PhotoPrism' }),
      ),
    );

    setLibraryPhotosEnabled('immich', true);
    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() => {
      expect(getLibraryPhotoLoadState('immich').reach).toBe('ok');
      expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok');
    });
    expect(getLibraryPhotoLoadState('immich').libraryName).toBe('Immich');
    expect(immich.getSource()?.getFeatures()).toHaveLength(1);
    expect(photoprism.getSource()?.getFeatures()).toHaveLength(1);

    // One of them stops answering. The other must not notice.
    fetchLibraryPhotoFeatures.mockImplementation((source: string) =>
      source === 'immich'
        ? Promise.reject(new Error('the library did not answer'))
        : Promise.resolve(collection()),
    );
    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('immich').reach).toBe('unreachable'));
    expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok');
    expect(immich.getSource()?.getFeatures()).toHaveLength(1);
    expect(photoprism.getSource()?.getFeatures()).toHaveLength(1);

    // And switching one off is switching one off: the other keeps its pins and goes on being read.
    setLibraryPhotosEnabled('immich', false);
    expect(immich.getSource()?.getFeatures()).toHaveLength(0);
    expect(photoprism.getSource()?.getFeatures()).toHaveLength(1);

    fetchLibraryPhotoFeatures.mockClear();
    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchLibraryPhotoFeatures).toHaveBeenCalledTimes(1);
    expect(fetchLibraryPhotoFeatures).toHaveBeenCalledWith('photoprism', expect.any(String));
  });

  it('asks the library nothing while its overlay is switched off', async () => {
    fetchLibraryPhotoFeatures.mockResolvedValue(collection());
    const layer = createLibraryPhotoLayer('photoprism');

    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchLibraryPhotoFeatures).not.toHaveBeenCalled();

    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').reach).toBe('ok'));
    expect(fetchLibraryPhotoFeatures).toHaveBeenCalledTimes(1);

    setLibraryPhotosEnabled('photoprism', false);
    expect(layer.getSource()?.getFeatures()).toHaveLength(0);
    expect(getLibraryPhotoLoadState('photoprism').reach).toBe('idle');

    moveEnd();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchLibraryPhotoFeatures).toHaveBeenCalledTimes(1);
  });
});

describe('drawing the photographs themselves', () => {
  /** Many photographs in one answer, so the ceiling can be crossed without inventing a new shape. */
  const many = (count: number) => {
    const one = collection().features[0];
    return collection({
      features: Array.from({ length: count }, (_, i) => ({
        ...one,
        properties: { ...one.properties, reference: `ref-${i}` },
      })),
    });
  };

  it('is off until it is asked for, and reports itself', async () => {
    expect(getLibraryPhotoPictures('photoprism')).toBe(false);
    setLibraryPhotoPictures('photoprism', true);
    expect(getLibraryPhotoPictures('photoprism')).toBe(true);
    expect(getLibraryPhotoLoadState('photoprism').pictures).toBe(true);
    setLibraryPhotoPictures('photoprism', false);
    expect(getLibraryPhotoLoadState('photoprism').pictures).toBe(false);
  });

  it('draws pins past the ceiling, and says that is why', async () => {
    fetchLibraryPhotoFeatures.mockResolvedValue(many(LIBRARY_PHOTO_PICTURE_LIMIT + 1));
    setLibraryPhotoPictures('photoprism', true);
    const layer = createLibraryPhotoLayer('photoprism');
    const detach = attachLibraryPhotoLoader(stubMap().map);
    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() =>
      expect(getLibraryPhotoLoadState('photoprism').shownCount).toBe(LIBRARY_PHOTO_PICTURE_LIMIT + 1),
    );

    // Refused, and the refusal is carried rather than left for a surface to work out again.
    expect(getLibraryPhotoLoadState('photoprism').picturesSuppressed).toBe(true);

    // And what is actually drawn is the pin — the check that matters, because the flag above is a
    // claim about the style and this is the style.
    const feature = layer.getSource()!.getFeatures()[0]!;
    const style = (layer.getStyleFunction() as (f: unknown, r: number) => Style[])(feature, 1);
    const src = (style[0]!.getImage() as Icon).getSrc() ?? '';
    expect(src.startsWith('data:image/svg+xml')).toBe(true);

    detach();
    setLibraryPhotosEnabled('photoprism', false);
    setLibraryPhotoPictures('photoprism', false);
  });

  it('stays under the ceiling without suppressing', async () => {
    fetchLibraryPhotoFeatures.mockResolvedValue(many(LIBRARY_PHOTO_PICTURE_LIMIT));
    setLibraryPhotoPictures('photoprism', true);
    createLibraryPhotoLayer('photoprism');
    const detach = attachLibraryPhotoLoader(stubMap().map);
    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() =>
      expect(getLibraryPhotoLoadState('photoprism').shownCount).toBe(LIBRARY_PHOTO_PICTURE_LIMIT),
    );
    expect(getLibraryPhotoLoadState('photoprism').picturesSuppressed).toBe(false);

    detach();
    setLibraryPhotosEnabled('photoprism', false);
    setLibraryPhotoPictures('photoprism', false);
  });

  it('draws a pin for a library that publishes no picture URL, however it is asked', async () => {
    fetchLibraryPhotoFeatures.mockResolvedValue(
      collection({ picturesAvailable: false, pictureUrlTemplate: null }),
    );
    setLibraryPhotoPictures('photoprism', true);
    const layer = createLibraryPhotoLayer('photoprism');
    const detach = attachLibraryPhotoLoader(stubMap().map);
    setLibraryPhotosEnabled('photoprism', true);
    await vi.waitFor(() => expect(getLibraryPhotoLoadState('photoprism').shownCount).toBe(1));

    const feature = layer.getSource()!.getFeatures()[0]!;
    const style = (layer.getStyleFunction() as (f: unknown, r: number) => Style[])(feature, 1);
    expect(((style[0]!.getImage() as Icon).getSrc() ?? '').startsWith('data:image/svg+xml')).toBe(true);

    detach();
    setLibraryPhotosEnabled('photoprism', false);
    setLibraryPhotoPictures('photoprism', false);
  });
});
