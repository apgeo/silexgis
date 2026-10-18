// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type Map from 'ol/Map';
import { fromLonLat } from 'ol/proj';
import type { PhotoFeatureCollection } from '../api/hooks.ts';
import { attachPhotoLoader, getPhotoLoadState, setPhotosEnabled, subscribePhotoLoadState } from './photoLayer.ts';

// An invented answer in the shape the photo map route sends; none of it real.
function answer(cutShort: boolean): PhotoFeatureCollection {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [25.5, 45.5] },
        properties: {},
      },
    ],
    cutShort,
  };
}

describe('the photo overlay says when its window was cut short', () => {
  let moveEnd: (() => void) | undefined;
  const map = {
    getSize: () => [800, 600],
    getView: () => ({ calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])] }),
    on: (_type: string, listener: () => void) => {
      moveEnd = listener;
    },
    un: () => {
      moveEnd = undefined;
    },
  } as unknown as Map;

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    setPhotosEnabled(false);
    vi.useRealTimers();
  });

  it('reports a cut-short answer, and stops reporting it once a full one arrives', async () => {
    let cutShort = true;
    const fetchPhotos = vi.fn(async () => answer(cutShort));
    const seen: boolean[] = [];
    const unsubscribe = subscribePhotoLoadState((state) => seen.push(state.cutShort));
    const detach = attachPhotoLoader(map, fetchPhotos);

    setPhotosEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(getPhotoLoadState().cutShort).toBe(true);

    cutShort = false;
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchPhotos).toHaveBeenCalledTimes(2);
    expect(getPhotoLoadState().cutShort).toBe(false);
    expect(seen).toEqual([true, false]);

    unsubscribe();
    detach();
  });

  it('reports nothing once the overlay is switched off, even for an answer still on its way', async () => {
    let release: (() => void) | undefined;
    const fetchPhotos = vi.fn(
      () =>
        new Promise<PhotoFeatureCollection>((resolve) => {
          release = () => resolve(answer(true));
        }),
    );
    const detach = attachPhotoLoader(map, fetchPhotos);

    setPhotosEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    release?.();
    await vi.advanceTimersByTimeAsync(0);
    expect(getPhotoLoadState().cutShort).toBe(true);

    setPhotosEnabled(false);
    expect(getPhotoLoadState().cutShort).toBe(false);

    setPhotosEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    setPhotosEnabled(false);
    release?.();
    await vi.advanceTimersByTimeAsync(0);
    expect(getPhotoLoadState().cutShort).toBe(false);

    detach();
  });
});
