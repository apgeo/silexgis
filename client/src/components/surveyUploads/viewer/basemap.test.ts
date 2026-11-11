// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { MapLayerInfo } from '../../../api/hooks.ts';
import { defaultBasemap } from './basemap.ts';

function layer(over: Partial<MapLayerInfo>): MapLayerInfo {
  return { id: 1, name: 'x', layerKind: 'xyz', url: 'https://t/{z}/{x}/{y}.png', isBase: true, isDefault: false, ...over } as MapLayerInfo;
}

/** Which background the survey's map is drawn over. */
describe('the map background', () => {
  it('is the installation\'s default base layer, else its first base layer', () => {
    const first = layer({ id: 1 });
    const chosen = layer({ id: 2, isDefault: true });
    expect(defaultBasemap([first, chosen])?.id).toBe(2);
    expect(defaultBasemap([first, layer({ id: 3 })])?.id).toBe(1);
  });

  it('is none — the fallback background — when the installation publishes no drawable base layer', () => {
    expect(defaultBasemap(undefined)).toBeNull();
    expect(defaultBasemap([])).toBeNull();
    expect(defaultBasemap([layer({ isBase: false, isDefault: true })])).toBeNull();
    expect(defaultBasemap([layer({ layerKind: 'wms', wms: null })])).toBeNull();
  });
});
