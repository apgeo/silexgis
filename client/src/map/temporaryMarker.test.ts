// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import { Point } from 'ol/geom';
import type VectorLayer from 'ol/layer/Vector';
import type VectorSource from 'ol/source/Vector';
import { toLonLat } from 'ol/proj';
import { describe, expect, it } from 'vitest';
import { attachTemporaryMarker, formatMarkParam, parseMarkParam } from './temporaryMarker.ts';

describe('parseMarkParam', () => {
  it('reads latitude then longitude', () => {
    expect(parseMarkParam('45.5,-24.25')).toEqual({ lat: 45.5, lon: -24.25 });
  });

  it('reads nothing from an absent, partial, non-numeric or off-globe value', () => {
    for (const value of [null, '', '45', '45,', ',24', 'a,b', '91,10', '10,181', '1,2,3', 'NaN,1', 'Infinity,1']) {
      expect(parseMarkParam(value)).toBeNull();
    }
  });

  it('accepts the limits of the globe', () => {
    expect(parseMarkParam('-90,180')).toEqual({ lat: -90, lon: 180 });
  });

  it('round-trips what the converter writes', () => {
    expect(parseMarkParam(formatMarkParam({ lat: 45.123456, lon: 24.654321 }))).toEqual({
      lat: 45.123456,
      lon: 24.654321,
    });
  });
});

describe('attachTemporaryMarker', () => {
  it('draws one point at the asked position and removes it on detach', () => {
    const map = new Map({});
    const detach = attachTemporaryMarker(map, '?select=x&mark=45.5,24.25');
    const layers = map.getLayers().getArray() as VectorLayer<VectorSource>[];
    expect(layers).toHaveLength(1);
    const features = layers[0].getSource()!.getFeatures();
    expect(features).toHaveLength(1);
    const [lon, lat] = toLonLat((features[0].getGeometry() as Point).getCoordinates());
    expect(lon).toBeCloseTo(24.25, 6);
    expect(lat).toBeCloseTo(45.5, 6);

    detach();
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('adds nothing for an address without a valid marker', () => {
    const map = new Map({});
    attachTemporaryMarker(map, '?mark=somewhere')();
    attachTemporaryMarker(map, '')();
    expect(map.getLayers().getLength()).toBe(0);
  });
});
