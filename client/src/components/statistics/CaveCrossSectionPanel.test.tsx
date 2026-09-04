// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveCrossSection } from '../../api/hooks.ts';

const { crossSectionSpy } = vi.hoisted(() => ({ crossSectionSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveCrossSection: (...args: unknown[]) => crossSectionSpy(...args),
}));

const { default: CaveCrossSectionPanel } = await import('./CaveCrossSectionPanel.tsx');

const distribution = (median: number) => ({
  count: 3,
  minimum: median - 1,
  lowerQuartile: median - 0.5,
  median,
  upperQuartile: median + 0.5,
  maximum: median + 1,
  mean: median,
});

const measured: CaveCrossSection = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasReadings: true,
  summary: {
    readingCount: 5,
    stationCount: 4,
    widthStationCount: 3,
    heightStationCount: 3,
    areaStationCount: 2,
    width: distribution(4),
    height: distribution(2),
    widthHeightRatio: distribution(2),
    area: distribution(6),
    scaling: { stationCount: 3, exponent: 0.62, coefficient: 1.4, rSquared: 0.81 },
    volume: {
      volumeM3: 620,
      legCount: 5,
      measuredLegCount: 2,
      lengthM: 250,
      measuredLengthM: 100,
      lengthFraction: 0.4,
    },
    bandWidthM: 10,
    bands: [
      {
        fromM: 1000,
        toM: 1010,
        stationCount: 2,
        areaStationCount: 1,
        width: distribution(4),
        height: distribution(2),
        area: distribution(6),
      },
    ],
  },
};

function show(data: CaveCrossSection | undefined, { isError = false, isLoading = false } = {}) {
  crossSectionSpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <CaveCrossSectionPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('CaveCrossSectionPanel', () => {
  it('shows the sizes with the count each was worked out over', () => {
    show(measured);
    expect(screen.getByTestId('cave-cross-section')).toBeTruthy();
    // The denominators are the point: a median width is a claim about the stations that produced
    // one, not about the cave.
    // Width and height each came from three stations; the area from two, which is the
    // count that matters and the one a reader would otherwise assume matched the others.
    expect(screen.getAllByText(/over 3 stations/).length).toBeGreaterThan(0);
    expect(screen.getByText(/over 2 stations/)).toBeTruthy();
  });

  it('says how much of the cave the volume was computed over', () => {
    show(measured);
    expect(
      screen.getByText(/Computed over 100 m of 250 m of passage — 2 of 5 pieces/),
    ).toBeTruthy();
  });

  it('refuses the volume rather than reporting nought when no leg had both ends measured', () => {
    show({
      ...measured,
      summary: {
        ...measured.summary!,
        volume: {
          volumeM3: null,
          legCount: 5,
          measuredLegCount: 0,
          lengthM: 250,
          measuredLengthM: 0,
          lengthFraction: 0,
        },
      },
    });
    expect(screen.getByText(/Nought here would say this cave encloses no space/)).toBeTruthy();
    // And the figure itself is a dash, not a zero: "0 m³" is a statement about the cave.
    expect(screen.queryByText('0 m³')).toBeNull();
  });

  // The server returns a five-number summary so a box can be drawn from it. A panel showing only
  // the median throws away the spread the quartiles carry, and the spread is what separates a broad
  // phreatic gallery from a narrow canyon at the same median.
  it('draws the spread rather than only the middle of it', async () => {
    show(measured);

    expect(await screen.findByTestId('chart-cross-section-spread')).toBeTruthy();
    expect(await screen.findByTestId('chart-cross-section-bands')).toBeTruthy();
  });

  it('reports how height scales with width, with the fit it was read from', () => {
    show(measured);

    expect(screen.getByText(/height rises as width to the power 0.62/)).toBeTruthy();
    expect(screen.getByText(/over 3 stations, accounting for 0.81 of the spread/)).toBeTruthy();
  });

  it('says nobody measured the walls rather than showing passages of no size', () => {
    show({ ...measured, hasReadings: false, summary: null });
    expect(screen.getByText(/carries no wall distances/)).toBeTruthy();
    expect(screen.queryByTestId('cave-cross-section')).toBeNull();
  });

  it('is absent, not blank, when the server refuses', () => {
    const { container } = show(undefined, { isError: true });
    expect(container.textContent).toBe('');
  });
});
