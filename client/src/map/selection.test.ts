// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import OlFeature from 'ol/Feature';
import Point from 'ol/geom/Point';
import { fromLonLat } from 'ol/proj';
import type OlMap from 'ol/Map';
import { filterLayerId } from './filterLayerList.ts';
import { attachSelection } from './selection.ts';

const SAVED = '00000000-0000-4000-8000-000000000009';

/** A map stand-in whose pixel always hits one feature on one layer. */
function mapHitting(properties: Record<string, unknown>, layerId: string) {
  let click: ((event: unknown) => void) | undefined;
  const feature = new OlFeature({ geometry: new Point(fromLonLat([25, 45])), ...properties });
  const layer = { get: (key: string) => (key === 'id' ? layerId : undefined) };
  const map = {
    on: (_type: string, handler: (event: unknown) => void) => {
      click = handler;
    },
    un: () => undefined,
    forEachFeatureAtPixel: (_pixel: unknown, callback: (f: unknown, l: unknown) => boolean) => {
      callback(feature, layer);
    },
    getView: () => ({ getZoom: () => 9 }),
  } as unknown as OlMap;
  return { map, click: (originalEvent: Partial<MouseEvent> = {}) => click?.({ pixel: [1, 1], originalEvent }) };
}

describe("a click on a saved filter's layer", () => {
  const layer = filterLayerId(SAVED);

  it('selects an entrance as the main entrance layer does', () => {
    const onPick = vi.fn();
    const { map, click } = mapHitting({ id: 'e1', caveId: 'c1', approximate: true }, layer);
    attachSelection(map, onPick);
    click();
    expect(onPick).toHaveBeenCalledWith({ kind: 'entrance', entranceId: 'e1', caveId: 'c1' });
  });

  it('selects a surface feature, and a modifier-click adds it to the set', () => {
    const onPick = vi.fn();
    const onAdd = vi.fn();
    const { map, click } = mapHitting({ id: 'f1' }, layer);
    attachSelection(map, onPick, onAdd);
    click();
    expect(onPick).toHaveBeenCalledWith({ kind: 'feature', featureId: 'f1' });
    click({ ctrlKey: true });
    expect(onAdd).toHaveBeenCalledWith({ kind: 'feature', id: 'f1' });
  });

  it('opens a cluster on the members of its own filter', () => {
    const onPick = vi.fn();
    const { map, click } = mapHitting({ cluster: true, count: 4, cell: [24.9, 44.9, 25.1, 45.1] }, layer);
    attachSelection(map, onPick);
    click();
    expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ kind: 'cluster', count: 4, savedFilterId: SAVED }));
  });

  it("leaves the main layer's clusters on the map's own filter", () => {
    const onPick = vi.fn();
    const { map, click } = mapHitting({ cluster: true, count: 4, cell: [24.9, 44.9, 25.1, 45.1] }, 'entrances');
    attachSelection(map, onPick);
    click();
    expect(onPick.mock.calls[0][0]).not.toHaveProperty('savedFilterId');
  });
});
