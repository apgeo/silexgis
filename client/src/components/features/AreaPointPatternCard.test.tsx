// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { DensityGrid, PointPattern } from '../../api/hooks.ts';

const { densitySpy, patternSpy } = vi.hoisted(() => ({
  densitySpy: vi.fn(),
  patternSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useMapDensity: (...args: unknown[]) => densitySpy(...args),
  useMapPointPattern: (...args: unknown[]) => patternSpy(...args),
}));

// The charting library needs a canvas the test environment does not have, and neither chart is
// what this file is about: what is asserted here is which request was made and which words the
// numbers arrived under.
vi.mock('../statistics/DistributionCharts.tsx', () => ({
  EnvelopeChart: () => <div data-testid="stub-envelope" />,
}));
vi.mock('../statistics/RoseDiagram.tsx', () => ({
  default: () => <div data-testid="stub-rose" />,
}));

const { default: AreaPointPatternCard } = await import('./AreaPointPatternCard.tsx');

const OUTLINE = {
  type: 'Polygon',
  coordinates: [[[25.42, 45.51], [25.46, 45.51], [25.46, 45.54], [25.42, 45.54], [25.42, 45.51]]],
};

function grid(overrides: Partial<DensityGrid> = {}): DensityGrid {
  return {
    cellMetres: 500,
    minimumCellMetres: 500,
    protectionGridMetres: 500,
    bandwidthMetres: 1000,
    minimumBandwidthMetres: 500,
    studyAreaId: 'a1',
    studyAreaKm2: 12.5,
    featureCount: 9,
    cellCount: 40,
    cells: [
      {
        west: 25.42, south: 45.51, east: 25.43, north: 45.52,
        count: 3, areaKm2: 0.2, densityPerKm2: 15, studyAreaFraction: 1, kernelDensityPerKm2: 7.5,
      },
    ],
    ...overrides,
  } as DensityGrid;
}

function pattern(overrides: Partial<PointPattern> = {}): PointPattern {
  return {
    featureCount: 7,
    minimumFeatureCount: 5,
    studyAreaKm2: 12.5,
    studyAreaId: 'a1',
    clarkEvans: {
      meanNearestNeighbourM: 400,
      expectedMeanM: 660,
      index: 0.606,
      zScore: -3.1,
      pValue: 0.0019,
    },
    ripley: {
      simulations: 99,
      seed: 1,
      maxRadiusM: 2000,
      steps: [{ radiusM: 500, observedL: 700, lowerL: 420, upperL: 610 }],
    },
    alignment: {
      pairCount: 21,
      minSeparationM: 500,
      maxSeparationM: 2000,
      rose: {
        sampleCount: 21,
        totalLengthM: 18000,
        byCount: { meanAxisDegrees: 47 },
        byLength: { meanAxisDegrees: 45 },
        bins: [],
      },
    },
    ...overrides,
  } as unknown as PointPattern;
}

afterEach(() => {
  cleanup();
  densitySpy.mockReset();
  patternSpy.mockReset();
});

it('asks with no cell size first, so the floor comes from the server rather than a guess', () => {
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  // The finest publishable cell is the installation's protection grid, which this client does not
  // know. Naming one on the first request is how a control ends up hard-coding a number that is
  // right on one installation and refused on every other.
  const first = densitySpy.mock.calls[0][0];
  expect(first).toMatchObject({ areaId: 'a1' });
  expect(first.cellMetres).toBeUndefined();

  // The window is the outline's own, in the order the API states, and not the coordinates in the
  // order they happened to be written.
  expect(first.bbox).toBe('25.420000,45.510000,25.460000,45.540000');
});

it('states the floor, the counts and both readings the spacing statistics cannot give', () => {
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  expect(screen.getByText(/location-protection grid/i)).toBeTruthy();

  // The two counts differ on purpose — the grid counts every entrance the reader may see, the
  // spacing only those they may place exactly — and the card has to say so rather than let one
  // number stand for both.
  expect(screen.getByText(/counted 9 entrances/i)).toBeTruthy();
  expect(screen.getByText(/only the 7 you may place exactly/i)).toBeTruthy();

  // An index below one is clustered, and the reading is given in words: the number alone requires
  // a reader to remember which side of one means what.
  expect(screen.getByText(/closer together than chance/i)).toBeTruthy();

  // Both charts are drawn through the components that already existed, not through new ones.
  expect(screen.getByTestId('stub-envelope')).toBeTruthy();
  expect(screen.getByTestId('stub-rose')).toBeTruthy();
});

it('shows nothing at all for a kind that is not a tract of karst', () => {
  densitySpy.mockReturnValue({ data: undefined, isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: undefined, isLoading: false, isError: false });

  const { container } = render(
    <AreaPointPatternCard featureId="b1" featureTypeCode="building" geometry={OUTLINE} />,
  );

  // Caves per square kilometre of a building is not a reading anybody wants offered, and the
  // request is not made either.
  expect(container.textContent).toBe('');
  expect(densitySpy.mock.calls[0][0]).toBeUndefined();
});

it('says no band was drawn when no simulation ran, rather than drawing a flat one', () => {
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({
    data: pattern({
      ripley: {
        simulations: 0,
        seed: 1,
        maxRadiusM: 2000,
        steps: [{ radiusM: 500, observedL: 700, lowerL: null, upperL: null }],
      },
    } as Partial<PointPattern>),
    isLoading: false,
    isError: false,
  });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  // "No significance was tested" and "indistinguishable from chance" are opposite findings, and a
  // zero-width band on the curve is how the first gets mistaken for the second.
  expect(screen.getByText(/no significance was tested/i)).toBeTruthy();
});
