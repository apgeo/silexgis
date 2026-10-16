// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { DensityGrid, PointPattern } from '../../api/hooks.ts';

const { densitySpy, steppedSpy, patternSpy } = vi.hoisted(() => ({
  densitySpy: vi.fn(),
  steppedSpy: vi.fn(),
  patternSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useMapDensity: (...args: unknown[]) => densitySpy(...args),
  useSteppedMapDensity: (...args: unknown[]) => steppedSpy(...args),
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
        cellX: 10, cellY: 20,
        west: 25.42, south: 45.51, east: 25.43, north: 45.52,
        count: 3, areaKm2: 0.2, densityPerKm2: 15, studyAreaFraction: 1, kernelDensityPerKm2: 7.5,
        hotSpotZ: 2.4,
      },
      {
        cellX: 11, cellY: 20,
        west: 25.43, south: 45.51, east: 25.44, north: 45.52,
        count: 0, areaKm2: 0.2, densityPerKm2: 0, studyAreaFraction: 1, kernelDensityPerKm2: 1.5,
        hotSpotZ: -2.1,
      },
      {
        cellX: 12, cellY: 20,
        west: 25.44, south: 45.51, east: 25.45, north: 45.52,
        count: 1, areaKm2: 0.2, densityPerKm2: 5, studyAreaFraction: 1, kernelDensityPerKm2: 3,
        hotSpotZ: null,
      },
    ],
    autocorrelation: {
      cellCount: 3,
      neighbourPairCount: 4,
      index: 0.42,
      expectedIndex: -0.5,
      zScore: 3.1,
      pValue: 0.002,
      pattern: 'clustered',
      significanceZ: 1.96,
    },
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

// Most cases below set one grid on `densitySpy` and are about how it is read. The card's first
// answer comes from the shared step search, so by default that answer is the same grid, reached at
// the floor; the cases about the step search set `steppedSpy` themselves.
beforeEach(() => {
  steppedSpy.mockImplementation((query: unknown) => {
    const served = densitySpy(query) as { data?: DensityGrid; isLoading: boolean; isError: boolean } | undefined;
    return {
      ...served,
      data: served?.data
        ? { kind: 'grid', grid: served.data, limits: { minimumCellMetres: 500, maxCells: 5000 } }
        : undefined,
    };
  });
});

afterEach(() => {
  cleanup();
  densitySpy.mockReset();
  steppedSpy.mockReset();
  patternSpy.mockReset();
});

it('takes its first grid from the shared step search, over the outline\'s own window', () => {
  steppedSpy.mockReturnValue({
    data: { kind: 'grid', grid: grid(), limits: { minimumCellMetres: 500, maxCells: 5000 } },
    isLoading: false,
    isError: false,
  });
  densitySpy.mockReturnValue({ data: undefined, isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  // The step search is what asks with no cell first (its own tests say so), so the floor comes from
  // the server rather than a guess. What the card owes it is the window — the outline's own, in
  // the order the API states, not the coordinates in the order they happened to be written — and
  // the area.
  expect(steppedSpy.mock.calls[0][0]).toEqual({ box: [25.42, 45.51, 25.46, 45.54], areaId: 'a1' });

  // No second, fixed-cell request is made until the reader chooses a coarser cell.
  expect(densitySpy.mock.calls.every(([query]) => query === undefined)).toBe(true);
});

it('offers coarser cells from the multiple the step search fitted, when the floor was too fine', () => {
  // An area too wide to grid at the floor: the search was refused for too many cells, read the
  // floor (500 m) and the limit off the refusal, and was answered at four times the floor. The
  // card shows that grid and offers its choices from there — not the floor the window cannot hold.
  steppedSpy.mockReturnValue({
    data: {
      kind: 'grid',
      grid: grid({ cellMetres: 2000 }),
      limits: { minimumCellMetres: 500, maxCells: 5000 },
    },
    isLoading: false,
    isError: false,
  });
  densitySpy.mockReturnValue({ data: grid({ cellMetres: 4000 }), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  expect(screen.getByTestId('area-point-pattern')).toBeTruthy();
  expect(screen.getByTestId('karst-density-cell-value').textContent).toBe('2000');
  const options = screen.getByTestId('karst-density-cell').textContent ?? '';
  expect(options).toContain('2000 m');
  expect(options).toContain('16000 m');
  expect(options).not.toContain('500 m');

  fireEvent.click(screen.getByText('4000 m'));
  const asked = densitySpy.mock.calls.map(([query]) => query).filter(Boolean);
  expect(asked.at(-1)).toMatchObject({ areaId: 'a1', cellMetres: 4000 });
});

it('shows nothing when the step search was refused, rather than an empty grid', () => {
  // Too wide even when coarsened, or an area this reader may not place: both refusals, and a
  // refusal is not a grid with no caves in it.
  steppedSpy.mockReturnValue({
    data: { kind: 'refused', code: 'density.too_many_cells', limits: null },
    isLoading: false,
    isError: false,
  });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  const { container } = render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  expect(container.textContent).toBe('');
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

it('counts the hot and cold cells against the cells that could be scored, not against all of them', () => {
  // The fixture holds three cells: one hot, one cold, and one whose score is null because the
  // statistic was not defined there. A cell without a score is not a cell that scored nought, so
  // the denominator the reader is given has to be two.
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  const spots = screen.getByTestId('karst-hotspot-count').textContent ?? '';
  expect(spots).toContain('1 cells read hot');
  expect(spots).toContain('1 read cold');
  expect(spots).toContain('of 2 that could be scored');
});

it('says the score is uncorrected for having been taken at every cell at once', () => {
  // Without this sentence a reader takes each striking cell for a finding, when a scattering of
  // them over a large window is what chance alone produces.
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  expect(screen.getByTestId('karst-hotspot-count').textContent).toContain('not corrected');
});

it('names the grid the reading was taken over, so it cannot be read as a finer surface', () => {
  densitySpy.mockReturnValue({ data: grid(), isLoading: false, isError: false });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  const basis = screen.getByTestId('karst-autocorrelation').textContent ?? '';
  expect(basis).toContain('500 m cell and no finer');
});

it('reports an undetermined window as a question that could not be asked, never as a nought', () => {
  // Undetermined and random are different claims: the first says the window could not support the
  // question, the second says it was asked and the arrangement looked like chance. An index of
  // null rendered as 0.000 would silently turn the first into the second.
  densitySpy.mockReturnValue({
    data: grid({
      autocorrelation: {
        cellCount: 1,
        neighbourPairCount: 0,
        index: null,
        expectedIndex: null,
        zScore: null,
        pValue: null,
        pattern: 'undetermined',
        significanceZ: 1.96,
      },
    } as unknown as Partial<DensityGrid>),
    isLoading: false,
    isError: false,
  });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  expect(screen.getByTestId('karst-moran-absent')).toBeTruthy();
  expect(screen.queryByTestId('karst-moran-reading')).toBeNull();
  expect(screen.getByTestId('karst-autocorrelation').textContent).not.toContain('0.000');
});

it('counts hot and cold cells at the threshold the server published, not at one of its own', () => {
  // What counts as a departure from chance is the server's rule, and the whole window's reading is
  // printed directly above this count. A copy of the threshold kept in the client would let the two
  // be read at different conventions with nothing on screen saying so: the sentence would call the
  // arrangement indistinguishable from chance while the count beside it still called cells hot.
  densitySpy.mockReturnValue({
    data: grid({
      autocorrelation: {
        cellCount: 3,
        neighbourPairCount: 4,
        index: 0.42,
        expectedIndex: -0.5,
        zScore: 1.1,
        pValue: 0.27,
        pattern: 'random',
        significanceZ: 2.58,
      },
    } as unknown as Partial<DensityGrid>),
    isLoading: false,
    isError: false,
  });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  // The fixture's two scored cells sit at 2.4 and -2.1: hot and cold at the conventional 1.96,
  // neither at the stricter threshold this response declared.
  const counted = screen.getByTestId('karst-hotspot-count').textContent ?? '';
  expect(counted).toContain('0 cells read hot and 0 read cold, of 2');
});

it('never prints a nought for a figure the server did not publish', () => {
  // The combination that actually ships from the smallest testable window: a reading arrives with
  // no z-score and no p-value behind it. Substituted with noughts these render as nought standard
  // errors at p 0.0000 — the strongest evidence claim this card can make, printed for a statistic
  // that was never computed.
  densitySpy.mockReturnValue({
    data: grid({
      autocorrelation: {
        cellCount: 4,
        neighbourPairCount: 12,
        index: 0.42,
        expectedIndex: -0.3333,
        zScore: null,
        pValue: null,
        pattern: 'undetermined',
        significanceZ: 1.96,
      },
    } as unknown as Partial<DensityGrid>),
    isLoading: false,
    isError: false,
  });
  patternSpy.mockReturnValue({ data: pattern(), isLoading: false, isError: false });

  render(
    <AreaPointPatternCard featureId="a1" featureTypeCode="karst_area" geometry={OUTLINE} />,
  );

  // The window could not be tested, so it is reported as untested — whatever else the response
  // happened to carry.
  expect(screen.getByTestId('karst-moran-absent')).toBeTruthy();
  expect(screen.queryByTestId('karst-moran-reading')).toBeNull();

  const text = screen.getByTestId('karst-autocorrelation').textContent ?? '';
  expect(text).not.toContain('0.0000');
  expect(text).not.toContain('0.00');
});
