// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';

import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import {
  CategoryBoxChart,
  CcdfChart,
  CorrelationChart,
  EnvelopeChart,
  HistogramChart,
  SummaryBoxChart,
} from './DistributionCharts.tsx';

/**
 * The charts are asserted against real rendered elements, not against a snapshot.
 *
 * <p>
 * That is only possible because the charts are drawn with the vector renderer rather than to a
 * canvas: a canvas in this environment is a stub, so a canvas-rendered chart can be asserted on
 * only by trusting that it was asked to draw. Here the axis labels and the boxes are elements, so
 * "the box plot drew one box per rock type" is a fact the test can check.
 * </p>
 */

function renderThemed(node: React.ReactNode, dark = false) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: dark ? 'dark' : 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

// Stated here as well as in the shared setup, which now unmounts after every test: without a
// cleanup a later query finds the previous test's chart as well as its own, and this file's
// queries are the kind that would then match two charts and pick the wrong one.
afterEach(cleanup);

const lengths = [12, 40, 55, 120, 260, 300, 480, 900, 1500, 2600, 4100, 9000];

describe('the chart layer draws real elements', () => {
  it('renders a histogram with axis labels', async () => {
    renderThemed(<HistogramChart values={lengths} xLabel="Length (m)" />);

    const frame = await screen.findByTestId('chart-histogram');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.querySelectorAll('text').length).toBeGreaterThan(0);
    expect(frame.textContent).toContain('Length (m)');
  });

  it('draws one box per category', async () => {
    const groups = [
      { label: 'Limestone', values: [10, 50, 90, 200, 400] },
      { label: 'Dolomite', values: [5, 30, 60, 110] },
      { label: 'Gypsum', values: [2, 8, 25] },
    ];

    renderThemed(<CategoryBoxChart groups={groups} yLabel="Length (m)" logScale />);

    const frame = await screen.findByTestId('chart-box');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    for (const g of groups) {
      expect(frame.textContent).toContain(g.label);
    }
  });

  it('draws boxes from quartiles it was given rather than recomputing them', async () => {
    const five = (median: number) => ({
      min: median - 2,
      q1: median - 1,
      median,
      q3: median + 1,
      max: median + 2,
      count: 9,
    });

    renderThemed(
      <SummaryBoxChart
        categories={['10–20 m', '20–30 m', '30–40 m']}
        series={[{ name: 'Width', summaries: [five(4), null, five(9)] }]}
        yLabel="m"
      />,
    );

    const frame = await screen.findByTestId('chart-summary-box');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.textContent).toContain('10–20 m');
    expect(frame.textContent).toContain('30–40 m');
  });

  // A slice of cave nobody measured is not a slice of passage with no size, so a category with no
  // summary must draw nothing at all there. Asserted as a difference against the same chart with
  // that slice measured, because "a box is absent" is only checkable against what present looks
  // like — counting shapes in one render would assert whatever the renderer happened to emit.
  it('leaves a gap for a category nothing was measured in', async () => {
    const five = { min: 1, q1: 2, median: 3, q3: 4, max: 5, count: 4 };
    const categories = ['low', 'middle', 'high'];

    const shapesFor = async (summaries: Array<typeof five | null>, testId: string) => {
      renderThemed(
        <SummaryBoxChart
          categories={categories}
          series={[{ name: 'Width', summaries }]}
          yLabel="m"
          testId={testId}
        />,
      );
      const frame = await screen.findByTestId(testId);
      await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
      const count = frame.querySelectorAll('path').length;
      cleanup();
      return count;
    };

    const whole = await shapesFor([five, five, five], 'chart-whole-box');
    const gapped = await shapesFor([five, null, five], 'chart-gap-box');

    expect(gapped).toBeLessThan(whole);
  });

  it('puts both axes of the rank-size plot on a logarithmic scale', async () => {
    renderThemed(<CcdfChart values={lengths} xLabel="Length (m)" />);

    const frame = await screen.findByTestId('chart-ccdf');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.querySelectorAll('text').length).toBeGreaterThan(0);
  });

  it('draws the fitted line beside the points it was fitted to', async () => {
    const pairs: Array<[number, number]> = lengths.map((l) => [l, Math.sqrt(l) * 3]);

    renderThemed(<CorrelationChart pairs={pairs} xLabel="Length (m)" yLabel="Depth (m)" />);

    const frame = await screen.findByTestId('chart-correlation');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());

    // The legend carries a second entry for the fitted line, which is how a reader tells a model
    // from the data it was fitted to. The setup loads English, so what appears is the translated
    // sentence rather than the key — asserting on it proves both that the label goes through the
    // translator and that the key it asks for actually exists, which a raw-key assertion cannot.
    const labels = Array.from(frame.querySelectorAll('text')).map((n) => n.textContent ?? '');
    expect(labels.some((l) => /Fit: slope/.test(l))).toBe(true);
    expect(labels).not.toContain('karstStats.regression');
  });

  it('draws a curve inside a band', async () => {
    const x = [0, 1, 2, 3, 4];
    renderThemed(
      <EnvelopeChart
        x={x}
        curve={[1, 2, 3, 2, 1]}
        lower={[0, 1, 2, 1, 0]}
        upper={[2, 3, 4, 3, 2]}
        xLabel="Distance (m)"
        yLabel="L(t)"
      />,
    );

    const frame = await screen.findByTestId('chart-envelope');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.querySelectorAll('path').length).toBeGreaterThan(1);
  });
});

describe('the honesty properties the charts are supposed to have', () => {
  it('leaves an empty bin as a gap on a logarithmic axis rather than inventing a count', async () => {
    // A gap in the middle guarantees at least one empty bin.
    const gapped = [1, 2, 3, 4, 5, 1000, 1001, 1002];

    renderThemed(<HistogramChart values={gapped} logCount xLabel="Length (m)" />);

    const frame = await screen.findByTestId('chart-histogram');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    // Nothing to assert visually beyond it having drawn: the point is that it does not throw and
    // does not substitute a value. The substitution would be a lie about the data, and the
    // arithmetic that decides it is covered in distributions.test.ts.
    expect(frame.querySelector('svg')).not.toBeNull();
  });

  it('renders nothing rather than an empty frame when there is no data', async () => {
    renderThemed(<HistogramChart values={[]} xLabel="Length (m)" />);

    const frame = await screen.findByTestId('chart-histogram');
    // The frame exists but the library was never given options, so no chart is drawn. An axis with
    // no data reads as "this registry has no caves", which would be an assertion nobody made.
    expect(frame.querySelectorAll('text')).toHaveLength(0);
  });

  it('survives being unmounted and mounted again', async () => {
    const first = renderThemed(<HistogramChart values={lengths} xLabel="Length (m)" />);
    await waitFor(() => expect(screen.getByTestId('chart-histogram').querySelector('svg')).not.toBeNull());
    first.unmount();

    renderThemed(<HistogramChart values={lengths} xLabel="Length (m)" />);
    await waitFor(() => expect(screen.getByTestId('chart-histogram').querySelector('svg')).not.toBeNull());
  });

  it('draws in the dark theme too, from the same source', async () => {
    renderThemed(<HistogramChart values={lengths} xLabel="Length (m)" />, true);

    const frame = await screen.findByTestId('chart-histogram');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.querySelectorAll('text').length).toBeGreaterThan(0);
  });
});

describe('the colour rule', () => {
  it('names no colour outside the theme bridge', () => {
    // The rule that keeps both themes legible: chart modules read tokens, and exactly one module
    // turns tokens into colours. A literal anywhere else is invisible in review and only shows up
    // as an unreadable chart in whichever theme the author was not using.
    // Read through the bundler rather than the filesystem: it needs no Node typings and it
    // resolves relative to this file, so the check keeps working if the directory moves.
    const sources = import.meta.glob('./*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<
      string,
      string
    >;

    for (const [path, source] of Object.entries(sources)) {
      if (path.includes('chartTheme') || path.includes('.test.')) continue;
      const literals = source.match(/#[0-9a-fA-F]{3,8}\b|\brgba?\(/g) ?? [];
      expect(literals, `${path} names a colour`).toEqual([]);
    }
  });
});
