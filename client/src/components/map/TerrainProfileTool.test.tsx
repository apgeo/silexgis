// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { mutate, reset, armed, state } = vi.hoisted(() => ({
  mutate: vi.fn(),
  reset: vi.fn(),
  armed: { finish: null as null | ((p: { longitude: number; latitude: number }[]) => void) },
  state: { data: undefined as unknown, isPending: false, isError: false },
}));

vi.mock('../../api/hooks/karstProfiles.ts', () => ({
  useTerrainProfile: () => ({ mutate, reset, ...state }),
}));
vi.mock('../../map/terrainProfileTool.ts', () => ({
  armTerrainProfileDraw: (cb: typeof armed.finish) => {
    armed.finish = cb;
    return vi.fn();
  },
  showTerrainProfileLine: vi.fn(),
}));
vi.mock('../statistics/DistributionCharts.tsx', () => ({
  EnvelopeChart: ({ x, testId }: { x: number[]; testId?: string }) => (
    <div data-testid={testId}>{x.length} readings</div>
  ),
}));

const { default: TerrainProfileTool } = await import('./TerrainProfileTool.tsx');

afterEach(() => {
  cleanup();
  state.data = undefined;
  state.isPending = false;
  state.isError = false;
  mutate.mockClear();
});

describe('TerrainProfileTool', () => {
  it('asks for the profile of the line once it is drawn', () => {
    render(<TerrainProfileTool />);
    fireEvent.click(screen.getByTestId('terrain-profile-button'));
    const line = [
      { longitude: 25, latitude: 46 },
      { longitude: 25.01, latitude: 46 },
    ];
    armed.finish?.(line);
    expect(mutate).toHaveBeenCalledWith(line);
  });

  it('draws the profile with its ascent and descent', () => {
    state.data = {
      hasTerrain: true,
      lengthM: 1000,
      readCount: 3,
      lowestM: 900,
      highestM: 1000,
      ascentM: 120,
      descentM: 20,
      samples: [
        { distanceAlongM: 0, longitude: 25, latitude: 46, outcome: 'sampled', elevationM: 900 },
        { distanceAlongM: 500, longitude: 25, latitude: 46, outcome: 'noData', elevationM: null },
        { distanceAlongM: 1000, longitude: 25, latitude: 46, outcome: 'sampled', elevationM: 1000 },
      ],
    };
    render(<TerrainProfileTool />);
    expect(screen.getByTestId('chart-terrain-profile').textContent).toBe('3 readings');
    expect(screen.getByTestId('terrain-profile-tool').textContent).toContain('120.0 m');
  });

  it('says the elevation data does not reach a line rather than drawing a flat one', () => {
    state.data = { hasTerrain: true, lengthM: 10, readCount: 0, lowestM: null, highestM: null, ascentM: 0, descentM: 0, samples: [] };
    render(<TerrainProfileTool />);
    expect(screen.queryByTestId('chart-terrain-profile')).toBeNull();
    expect(screen.getByTestId('terrain-profile-tool').textContent).toMatch(/does not reach/);
  });
});
