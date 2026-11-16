// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import Feature from 'ol/Feature';
import Polygon from 'ol/geom/Polygon';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { getSurfaceFeatureSource } from '../../map/featureLayer.ts';
import LithologyLegend from './LithologyLegend.tsx';
import { lithologyKeyOf } from './lithologyKey.ts';

function unit(id: string, lithology: string | null, karstifiable: boolean | null): Feature {
  const feature = new Feature(new Polygon([[[0, 0], [1, 0], [1, 1], [0, 0]]]));
  feature.setProperties({ id, typeCode: 'geological_unit', lithology, karstifiable });
  return feature;
}

beforeEach(() => getSurfaceFeatureSource().clear());
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  getSurfaceFeatureSource().clear();
});

describe('lithologyKeyOf', () => {
  it('lists each class drawn once, in vocabulary order, unrecognised then unclassified last', () => {
    const source = getSurfaceFeatureSource();
    const doline = new Feature(new Polygon([[[0, 0], [1, 0], [1, 1], [0, 0]]]));
    doline.setProperties({ id: 'd', typeCode: 'doline' });
    source.addFeatures([
      unit('a', null, null),
      unit('b', 'sandstone', false),
      unit('c', 'chalk', null),
      unit('d1', 'limestone', true),
      unit('d2', 'limestone', true),
      doline,
    ]);

    expect(lithologyKeyOf(source).map((e) => e.lithology)).toEqual(['limestone', 'sandstone', 'chalk', null]);
  });
});

describe('LithologyLegend', () => {
  it('draws nothing while no unit is loaded and the classes once units arrive', () => {
    vi.useFakeTimers();
    const { container } = render(<LithologyLegend />);
    expect(container).toBeEmptyDOMElement();

    act(() => {
      getSurfaceFeatureSource().addFeatures([unit('a', 'limestone', true), unit('b', null, null)]);
      vi.advanceTimersByTime(250);
    });

    expect(screen.getByTestId('lithology-legend')).toBeInTheDocument();
    expect(screen.getByText(/Limestone|Calcar/)).toBeInTheDocument();
    expect(screen.getByText(/Unclassified|Neclasificat/)).toBeInTheDocument();
  });
});
