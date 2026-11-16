// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render } from '@testing-library/react';
import type { EChartsOption } from 'echarts';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { histogram, powerLawExponent } from './distributions.ts';

/**
 * What the charts are asked to draw, read as numbers.
 *
 * The rendered-element tests beside this file can say a chart drew an axis and some labels; they
 * cannot say a bar has the right height or a fitted line the right slope. The drawing hook is
 * replaced here by one that keeps the option it was handed, so the axis floor and the series
 * values are asserted directly.
 */
let drawn: EChartsOption | null = null;
vi.mock('./useECharts.ts', () => ({
  useECharts: (option: EChartsOption | null) => {
    drawn = option;
    return { current: null };
  },
}));

const { HistogramChart } = await import('./DistributionCharts.tsx');
const { RegistryDistributionChart } = await import('./RegistryDistributionChart.tsx');

afterEach(() => {
  cleanup();
  drawn = null;
});

type Axis = { min?: number; type?: string };
type Series = { type: string; data: unknown[] };

const yAxisOf = (option: EChartsOption | null) => option?.yAxis as Axis;
const seriesOf = (option: EChartsOption | null) => (option?.series ?? []) as Series[];

// A long tail: most caves short, a few long, one bin holding exactly one cave.
const lengths = [
  10, 11, 12, 12, 13, 14, 15, 16, 18, 20, 22, 25, 28, 30, 35, 40, 48, 55, 70, 90, 120, 160, 240,
  400, 900, 2400,
];

describe('the histogram on a logarithmic count axis', () => {
  it('draws a bin holding one cave as a bar of some height, not as an empty bin', () => {
    render(<HistogramChart values={lengths} logCount xLabel="Length (m)" />);

    const bins = histogram(lengths, 12);
    const single = bins.findIndex((bin) => bin.count === 1);
    expect(single).toBeGreaterThan(-1);

    const axisMin = yAxisOf(drawn).min ?? 0;
    const bars = seriesOf(drawn).find((series) => series.type === 'bar')!;
    expect(bars.data[single]).toBe(1);
    // A bar is drawn up from the axis floor, so a floor of one gives a count of one no height.
    expect(axisMin).toBeLessThan(1);
    expect(axisMin).toBeGreaterThan(0);
  });

  it('draws the fitted power law as the counts it predicts for each bin of the fitted tail', () => {
    render(<HistogramChart values={lengths} xLabel="Length (m)" />);

    const bins = histogram(lengths, 12);
    const xMin = bins[0].to;
    const alpha = powerLawExponent(lengths, xMin)!;
    const tail = lengths.filter((value) => value >= xMin).length;

    const line = seriesOf(drawn).find((series) => series.type === 'line')!;
    // The fit covers values from the end of the first bin on; the first bin is outside it.
    expect(line.data[0]).toBeNull();
    for (let i = 1; i < bins.length; i++) {
      // The fitted density integrated over the bin: a count, falling as x^-alpha.
      const expected =
        tail * (Math.pow(bins[i].from / xMin, 1 - alpha) - Math.pow(bins[i].to / xMin, 1 - alpha));
      expect(line.data[i] as number).toBeCloseTo(expected, 6);
    }
  });

  it('leaves the fitted line out where it falls below a logarithmic axis rather than flattening it', () => {
    render(<HistogramChart values={lengths} logCount xLabel="Length (m)" />);

    const axisMin = yAxisOf(drawn).min ?? 0;
    const line = seriesOf(drawn).find((series) => series.type === 'line')!;
    for (const value of line.data) {
      expect(value === null || (value as number) >= axisMin).toBe(true);
    }
    // Some of the far tail predicts less than the floor, and those points are left out rather
    // than all drawn at the floor, which would read as a fitted plateau.
    expect(line.data.filter((value) => value === axisMin)).toHaveLength(0);
  });
});

describe('the registry distribution on a logarithmic count axis', () => {
  it('draws an interval holding one cave as a bar of some height', () => {
    render(
      <RegistryDistributionChart
        logCount
        xLabel="Length (m)"
        bars={[
          { lowerBound: 0, upperBound: 100, count: 40, merged: false, label: '0' },
          { lowerBound: 100, upperBound: 200, count: 1, merged: false, label: '100' },
        ]}
      />,
    );

    const axisMin = yAxisOf(drawn).min ?? 0;
    expect(axisMin).toBeLessThan(1);
    expect(axisMin).toBeGreaterThan(0);
    const bars = seriesOf(drawn)[0].data as { value: number | null }[];
    expect(bars[1].value).toBe(1);
  });
});
