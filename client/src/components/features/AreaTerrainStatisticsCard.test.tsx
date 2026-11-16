// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AreaTerrainStatistics } from '../../api/hooks/karstProfiles.ts';

const { statsSpy } = vi.hoisted(() => ({ statsSpy: vi.fn() }));

vi.mock('../../api/hooks/karstProfiles.ts', () => ({
  useAreaTerrainStatistics: (...args: unknown[]) => statsSpy(...args),
}));

const { default: AreaTerrainStatisticsCard } = await import('./AreaTerrainStatisticsCard.tsx');

const row = (code: string, entrances: number, ratio: number | null) => ({
  code,
  entrances,
  entranceShare: entrances / 3,
  entranceRatio: ratio,
  sinkholes: 0,
  sinkholeShare: null,
  sinkholeRatio: null,
  background: 50,
  backgroundShare: 0.5,
});

const stats: AreaTerrainStatistics = {
  areaId: 'area-1',
  hasTerrain: true,
  entrances: 3,
  sinkholes: 0,
  backgroundPoints: 100,
  measures: [
    {
      measure: 'slope',
      available: true,
      reason: null,
      entrancesRead: 3,
      sinkholesRead: 0,
      backgroundRead: 100,
      classes: [row('5to15', 2, 1.33), row('30to45', 1, 0.67)],
    },
    { measure: 'aspect', available: false, reason: 'no_layer', entrancesRead: 0, sinkholesRead: 0, backgroundRead: 0, classes: [] },
    { measure: 'position', available: false, reason: 'no_layer', entrancesRead: 0, sinkholesRead: 0, backgroundRead: 0, classes: [] },
  ],
};

function show(data: AreaTerrainStatistics | undefined, isError = false, type: string | null = 'Polygon') {
  statsSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(<AreaTerrainStatisticsCard featureId="area-1" geometryType={type} />);
}

afterEach(cleanup);

describe('AreaTerrainStatisticsCard', () => {
  it('shows each class with its ratio against the ground', () => {
    show(stats);
    const text = screen.getByTestId('area-terrain-statistics').textContent ?? '';
    expect(text).toContain('1.33');
    expect(text).toContain('0.67');
    expect(text).toContain('5–15°');
  });

  it('draws the slope classes as a figure above the table', () => {
    show(stats);
    expect(screen.getByTestId('chart-terrain-slope')).toBeTruthy();
    expect(screen.getAllByRole('table').length).toBeGreaterThan(0);
  });

  it('says a measure has no picture rather than drawing an empty table', () => {
    show({ ...stats, measures: [{ ...stats.measures[1] }] });
    const text = screen.getByTestId('area-terrain-statistics').textContent ?? '';
    expect(text).toMatch(/No picture of the terrain/);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('names landform classes as the landform legend does, not as the position classes', () => {
    const landform: AreaTerrainStatistics['measures'][number] = {
      measure: 'landform',
      available: true,
      reason: null,
      entrancesRead: 3,
      sinkholesRead: 0,
      backgroundRead: 100,
      classes: [row('hollow', 2, 2.5), row('valley', 1, 0.4)],
    };
    show({ ...stats, measures: [landform] });
    const card = screen.getByTestId('area-terrain-statistics');
    expect(screen.getByRole('tab', { name: 'Landform' })).toBeTruthy();
    expect(screen.getByTestId('chart-terrain-landform')).toBeTruthy();
    const cells = Array.from(card.querySelectorAll('td')).map((td) => td.textContent);
    // "Hollow" is the landform; the position class of the same code reads "hollow".
    expect(cells).toContain('Hollow');
    expect(cells).toContain('Valley');
    expect(cells).not.toContain('hollow');
    expect(card.textContent).toContain('2.50');
    expect(card.textContent).toMatch(/newest landform picture/);
  });

  it('says landforms cannot be drawn here when the analysis program is not run', () => {
    show({
      ...stats,
      measures: [
        {
          measure: 'landform',
          available: false,
          reason: 'analysis_unavailable',
          entrancesRead: 0,
          sinkholesRead: 0,
          backgroundRead: 0,
          classes: [],
        },
      ],
    });
    const text = screen.getByTestId('area-terrain-statistics').textContent ?? '';
    expect(text).toMatch(/WhiteboxTools/);
    expect(text).not.toMatch(/No picture of the terrain/);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('renders nothing for a refusal or for a feature that is not an outline', () => {
    show(undefined, true);
    expect(screen.queryByTestId('area-terrain-statistics')).toBeNull();
    cleanup();
    show(stats, false, 'Point');
    expect(screen.queryByTestId('area-terrain-statistics')).toBeNull();
  });
});
