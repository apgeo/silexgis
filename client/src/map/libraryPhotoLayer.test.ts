// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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
