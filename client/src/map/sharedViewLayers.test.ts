// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Map from 'ol/Map';
import View from 'ol/View';
import { fromLonLat } from 'ol/proj';

const fetchSharedViewEntrances = vi.fn();
const fetchSharedViewFeatures = vi.fn();
vi.mock('../api/hooks/sharedViews.ts', () => ({ fetchSharedViewEntrances, fetchSharedViewFeatures }));

const { createSharedViewLayers } = await import('./sharedViewLayers.ts');

function mapAt(lon: number, lat: number, zoom: number): Map {
  const map = new Map({ view: new View({ center: fromLonLat([lon, lat]), zoom }) });
  // No DOM to measure in a test: the size the view is computed from is stated instead.
  vi.spyOn(map, 'getSize').mockReturnValue([512, 512]);
  return map;
}

const collection = (features: unknown[]) => ({ type: 'FeatureCollection', features });

describe('the data layers of a shared view', () => {
  beforeEach(() => {
    fetchSharedViewEntrances.mockReset();
    fetchSharedViewFeatures.mockReset();
  });

  it('builds a layer only for an overlay the link draws, and asks only its route', async () => {
    fetchSharedViewEntrances.mockResolvedValue(
      collection([
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [12.4, 61.2] },
          properties: { name: 'Avenul' },
        },
      ]),
    );
    const { layers, attach } = createSharedViewLayers('token-1', { entrances: true, features: false });
    expect(layers).toHaveLength(1);

    const map = mapAt(12.4, 61.2, 15);
    const detach = attach(map);
    await vi.waitFor(() => expect(fetchSharedViewEntrances).toHaveBeenCalledTimes(1));

    const [token, bbox, zoom] = fetchSharedViewEntrances.mock.calls[0];
    expect(token).toBe('token-1');
    expect(zoom).toBe(15);
    // The window is the view's, on the globe, in the form the routes accept.
    const [west, south, east, north] = String(bbox).split(',').map(Number);
    expect(west).toBeLessThan(12.4);
    expect(east).toBeGreaterThan(12.4);
    expect(south).toBeLessThan(61.2);
    expect(north).toBeGreaterThan(61.2);
    expect(fetchSharedViewFeatures).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(layers[0].getSource()!.getFeatures()).toHaveLength(1));
    expect(layers[0].getSource()!.getFeatures()[0].get('name')).toBe('Avenul');
    detach();
  });

  it('draws nothing when the link draws no data', () => {
    expect(createSharedViewLayers('token-2', { entrances: false, features: false }).layers).toEqual([]);
  });

  it('keeps what it drew when an answer fails', async () => {
    fetchSharedViewFeatures
      .mockResolvedValueOnce(
        collection([
          { type: 'Feature', geometry: { type: 'Point', coordinates: [12.4, 61.2] }, properties: { name: 'Dolina' } },
        ]),
      )
      .mockRejectedValueOnce(new Error('offline'));
    const { layers, attach } = createSharedViewLayers('token-3', { entrances: false, features: true });
    const map = mapAt(12.4, 61.2, 15);
    const detach = attach(map);
    await vi.waitFor(() => expect(layers[0].getSource()!.getFeatures()).toHaveLength(1));

    map.dispatchEvent('moveend');
    await vi.waitFor(() => expect(fetchSharedViewFeatures).toHaveBeenCalledTimes(2), { timeout: 2_000 });
    expect(layers[0].getSource()!.getFeatures()).toHaveLength(1);
    detach();
  });
});
