// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type Map from 'ol/Map';
import type { Polygon } from 'ol/geom';
import { fromLonLat, toLonLat } from 'ol/proj';
import { ApiError } from '../api/client.ts';
import type { KarstIndexArea, KarstIndexQuery, KarstIndexWindow } from '../api/hooks.ts';
import i18n from '../i18n';
import {
  attachKarstIndexLoader,
  createKarstIndexLayer,
  getKarstIndexLoadState,
  getKarstIndexSource,
  karstIndexFeatures,
  setKarstIndexEnabled,
} from './karstIndexLayer.ts';

// Invented areas in the shape the karst-index route answers with; none of it real.

function area(over: Partial<KarstIndexArea> = {}): KarstIndexArea {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    name: 'Zona 1',
    outline: {
      type: 'Polygon',
      coordinates: [
        [
          [25, 45],
          [25.1, 45],
          [25.1, 45.1],
          [25, 45.1],
          [25, 45],
        ],
      ],
    },
    areaKm2: 87,
    caveCount: 3,
    cavesPerKm2: 0.03,
    depressionCount: 0,
    depressionAreaRatio: null,
    score: 0.5,
    class: 'moderate',
    components: [],
    ...over,
  };
}

function window(areas: KarstIndexArea[], truncated = false): KarstIndexWindow {
  return { basis: 'declared', areas, truncated, maxAreas: 2 };
}

describe('karstification-index areas', () => {
  it('draws each area from the outline the server sent, carrying its class and a label', async () => {
    await i18n.changeLanguage('en');
    const [feature] = karstIndexFeatures([area()]);
    expect(feature.getId()).toBe('00000000-0000-0000-0000-000000000001');
    expect(feature.get('class')).toBe('moderate');
    expect(feature.get('label')).toBe('Zona 1 · Moderate');
    const ring = (feature.getGeometry() as Polygon).getCoordinates()[0].map((c) => toLonLat(c));
    expect(ring[0][0]).toBeCloseTo(25, 6);
    expect(ring[2][1]).toBeCloseTo(45.1, 6);
  });

  it('labels an unnamed area by its class alone, in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    const [feature] = karstIndexFeatures([area({ name: null, class: 'unknown' })]);
    expect(feature.get('label')).toBe(i18n.t('karstArea.class.unknown'));
    await i18n.changeLanguage('en');
  });

  it('draws a larger area under the smaller ones inside it', () => {
    const order = createKarstIndexLayer().getRenderOrder();
    const [massif, karstArea] = karstIndexFeatures([
      area({ id: 'a', areaKm2: 500 }),
      area({ id: 'b', areaKm2: 20 }),
    ]);
    expect(order?.(massif, karstArea)).toBeLessThan(0);
    expect(order?.(karstArea, massif)).toBeGreaterThan(0);
  });
});

describe('loading the areas of a window', () => {
  let moveEnd: (() => void) | undefined;
  const map = {
    getSize: () => [800, 600],
    getView: () => ({
      calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])],
      getZoom: () => 10.6,
    }),
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
    setKarstIndexEnabled(false);
    vi.useRealTimers();
  });

  it('asks nothing while the layer is off, then asks for the window at the rounded zoom', async () => {
    const fetchIndex = vi.fn(async (_query: KarstIndexQuery) => window([area()]));
    const detach = attachKarstIndexLoader(map, fetchIndex);
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchIndex).not.toHaveBeenCalled();

    setKarstIndexEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(fetchIndex).toHaveBeenCalledTimes(1);
    const query = fetchIndex.mock.calls[0][0];
    expect(query.zoom).toBe(11);
    expect(query.bbox.split(',').map(Number)[0]).toBeCloseTo(25, 4);
    expect(getKarstIndexSource().getFeatures()).toHaveLength(1);
    expect(getKarstIndexLoadState()).toMatchObject({ status: 'ok', areaCount: 1, truncated: false });
    detach();
  });

  it('aborts the request a newer window supersedes and draws only the newer answer', async () => {
    const signals: AbortSignal[] = [];
    const answers = [window([area({ id: 'old' })]), window([area({ id: 'new' }), area({ id: 'two' })], true)];
    const fetchIndex = vi.fn(
      (_query: KarstIndexQuery, signal?: AbortSignal) =>
        new Promise<KarstIndexWindow>((resolve) => {
          signals.push(signal!);
          const answer = answers[signals.length - 1];
          setTimeout(() => resolve(answer), 1000);
        }),
    );
    const detach = attachKarstIndexLoader(map, fetchIndex);
    setKarstIndexEnabled(true);
    await vi.advanceTimersByTimeAsync(10);
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(signals[0].aborted).toBe(true);
    await vi.advanceTimersByTimeAsync(2000);
    expect(getKarstIndexSource().getFeatures().map((f) => f.getId())).toEqual(['new', 'two']);
    expect(getKarstIndexLoadState()).toMatchObject({ status: 'ok', areaCount: 2, truncated: true, maxAreas: 2 });
    detach();
  });

  it('says a refusal as one and leaves nothing of an earlier window drawn', async () => {
    let refuse = false;
    const fetchIndex = vi.fn(async () => {
      if (refuse) {
        throw new ApiError(400, 'map.invalid_bbox');
      }
      return window([area()]);
    });
    const detach = attachKarstIndexLoader(map, fetchIndex);
    setKarstIndexEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(getKarstIndexSource().getFeatures()).toHaveLength(1);

    refuse = true;
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(getKarstIndexSource().getFeatures()).toHaveLength(0);
    expect(getKarstIndexLoadState()).toMatchObject({ status: 'refused', refusal: 'map.invalid_bbox' });
    detach();
  });
});
