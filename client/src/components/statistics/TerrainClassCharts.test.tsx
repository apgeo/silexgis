// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { TerrainMeasure } from '../../api/hooks/karstProfiles.ts';
import { TerrainClassBars, TerrainFacingRose } from './TerrainClassCharts.tsx';

const row = (code: string, entranceShare: number | null, sinkholeShare: number | null, backgroundShare: number) => ({
  code,
  entrances: 1,
  entranceShare,
  entranceRatio: null,
  sinkholes: 0,
  sinkholeShare,
  sinkholeRatio: null,
  background: 10,
  backgroundShare,
});

const aspect: TerrainMeasure = {
  measure: 'aspect',
  available: true,
  reason: null,
  entrancesRead: 4,
  sinkholesRead: 0,
  backgroundRead: 100,
  classes: ['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw'].map((c, i) => row(c, i === 2 ? 0.5 : 0.1, null, 0.125)),
};

afterEach(cleanup);

describe('TerrainFacingRose', () => {
  it('draws the ground and the entrances, and leaves out a kind nothing was read for', () => {
    const { container } = render(<TerrainFacingRose measure={aspect} />);
    expect(screen.getByTestId('chart-terrain-aspect')).toBeTruthy();
    expect(container.querySelector('polygon[data-series="background"]')).not.toBeNull();
    expect(container.querySelector('polygon[data-series="entrances"]')).not.toBeNull();
    expect(container.querySelector('polygon[data-series="sinkholes"]')).toBeNull();
  });

  it('draws nothing for a measure without classes', () => {
    const { container } = render(<TerrainFacingRose measure={{ ...aspect, classes: [] }} />);
    expect(container.querySelector('svg')).toBeNull();
  });
});

describe('TerrainClassBars', () => {
  it('is labelled with the measure it shows', () => {
    render(<TerrainClassBars measure={{ ...aspect, measure: 'slope' }} />);
    expect(screen.getByTestId('chart-terrain-slope').getAttribute('aria-label')).toMatch(/slope/i);
  });
});
