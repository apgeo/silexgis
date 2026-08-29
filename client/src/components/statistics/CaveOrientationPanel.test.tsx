// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveOrientation, OrientationBin } from '../../api/hooks.ts';

const { orientationSpy } = vi.hoisted(() => ({ orientationSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveOrientation: (...args: unknown[]) => orientationSpy(...args),
}));

const { default: CaveOrientationPanel } = await import('./CaveOrientationPanel.tsx');

/** The eighteen folded sectors the server always sends, with everything in one of them. */
function roseBins(): OrientationBin[] {
  return Array.from({ length: 18 }, (_, i) => {
    const dominant = i === 4;
    return {
      fromDegrees: i * 10,
      toDegrees: i * 10 + 10,
      count: dominant ? 30 : 0,
      lengthM: dominant ? 900 : 0,
      countFraction: dominant ? 1 : 0,
      lengthFraction: dominant ? 1 : 0,
    };
  });
}

/** The eighteen inclination bands, running from straight down to straight up. */
function dipBins(): OrientationBin[] {
  return Array.from({ length: 18 }, (_, i) => {
    const level = i === 9;
    return {
      fromDegrees: -90 + i * 10,
      toDegrees: -80 + i * 10,
      count: level ? 25 : 1,
      lengthM: level ? 700 : 20,
      countFraction: 0,
      lengthFraction: 0,
    };
  });
}

const measure = {
  meanAxisDegrees: 44,
  resultantLength: 0.8,
  effectiveSampleSize: 30,
  rayleighZ: 19,
  rayleighP: 0.0001,
  entropyNats: 0.4,
  entropyNormalized: 0.14,
};

const surveyed: CaveOrientation = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  segmentCount: 30,
  totalLengthM: 900,
  byCount: measure,
  byLength: measure,
  bins: roseBins(),
  dip: {
    sampleCount: 42,
    totalLengthM: 900,
    meanDipDegrees: 1.2,
    meanAbsoluteDipDegrees: 7.4,
    minimumDipDegrees: -34,
    maximumDipDegrees: 28,
    bins: dipBins(),
  },
};

function show(data: CaveOrientation | undefined, { isError = false, isLoading = false } = {}) {
  orientationSpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <CaveOrientationPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('CaveOrientationPanel', () => {
  it('draws the rose and the dip histogram beside it', async () => {
    show(surveyed);

    expect(screen.getByTestId('chart-rose')).toBeTruthy();
    const frame = await screen.findByTestId('chart-dip');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(screen.getByText(/Mean steepness 7°/)).toBeTruthy();
  });

  it('refuses steepness with its reason when the line work carries no altitudes', () => {
    show({ ...surveyed, hasAltitudes: false, dip: null });

    expect(screen.getByTestId('dip-refused')).toBeTruthy();
    expect(screen.getByText('Steepness cannot be worked out')).toBeTruthy();
    // Absent, not nought: a plan drawing shown as a histogram piled on level would be a claim
    // about the cave rather than about the drawing.
    expect(screen.getByText(/carries no altitudes/)).toBeTruthy();
    expect(screen.queryByTestId('chart-dip')).toBeNull();
    // The rose still stands: a plan drawing says nothing about steepness but plenty about trend.
    expect(screen.getByTestId('chart-rose')).toBeTruthy();
  });

  it('says the trends were approximated when there is no compiled survey', () => {
    show({ ...surveyed, basis: 'skeletonHeuristic', isApproximation: true, surveyModelId: null });

    expect(screen.getByText(/Approximated from the stored centerline/)).toBeTruthy();
    expect(screen.queryByText(/using the surveyor's own flags/)).toBeNull();
  });

  it('shows nothing measured rather than an empty set of rings', () => {
    show({
      ...surveyed,
      basis: 'unavailable',
      isApproximation: false,
      surveyModelId: null,
      hasAltitudes: false,
      segmentCount: 0,
      totalLengthM: 0,
      dip: null,
      bins: roseBins().map((b) => ({ ...b, count: 0, lengthM: 0, countFraction: 0, lengthFraction: 0 })),
    });

    expect(screen.getByText(/no line work to measure/)).toBeTruthy();
    expect(screen.queryByTestId('chart-rose')).toBeNull();
    expect(screen.queryByTestId('dip-refused')).toBeNull();
  });

  it('shows nothing at all when the cave may not be read', () => {
    const { container } = show(undefined, { isError: true });
    expect(container.textContent).toBe('');
  });
});
