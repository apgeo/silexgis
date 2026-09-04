// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';

import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import { TripBreakdownChart, TripYearChart } from './TripInsightCharts.tsx';

/**
 * The trip charts against real rendered elements, the same way the other charts are checked: the
 * vector renderer draws elements, so an axis label is a fact a test can read rather than a
 * bitmap it has to trust.
 */
function renderThemed(node: React.ReactNode, dark = false) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: dark ? 'dark' : 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

// Without this a later query finds the previous test's chart as well as its own, and every query
// here is the kind that would then match two and pick whichever came first.
afterEach(cleanup);

const years = [
  { year: 2022, trips: 8, areasSoFar: 3 },
  { year: 2023, trips: 0, areasSoFar: 3 },
  { year: 2024, trips: 14, areasSoFar: 5 },
  { year: 2025, trips: 11, areasSoFar: 5 },
];

describe('trips over the years', () => {
  it('draws both the volume and the ground covered, and names them', async () => {
    renderThemed(<TripYearChart years={years} />);

    const frame = await screen.findByTestId('chart-trip-years');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    // Both series are named on the chart itself, because a bar row and a curve sharing an x axis
    // and nothing else is exactly the pair a reader would otherwise misread as one quantity.
    expect(frame.textContent).toContain('Trips');
    expect(frame.textContent).toContain('Distinct areas reached');
    // Every year of the span is on the axis, including the one nobody went anywhere: a quiet year
    // closed up would make the curve look like it flattened a year later than it did.
    expect(frame.textContent).toContain('2023');
  });

  it('draws no axes at all rather than an empty grid when there are no years', async () => {
    // The chart mounts either way — the drawing surface belongs to the hook — but nothing is
    // asked to be drawn on it, so there is no axis and no year suggesting a span that is not
    // there. An empty grid reads as a span in which nothing happened, which is a different claim.
    renderThemed(<TripYearChart years={[]} />);

    const frame = await screen.findByTestId('chart-trip-years');
    expect(frame.querySelectorAll('text').length).toBe(0);
  });

  it('draws in the dark theme too, from the same source', async () => {
    renderThemed(<TripYearChart years={years} />, true);

    const frame = await screen.findByTestId('chart-trip-years');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.querySelectorAll('text').length).toBeGreaterThan(0);
  });
});

describe('a breakdown of the counted trips', () => {
  const values = [
    { label: 'Ana Pop', count: 12 },
    { label: 'Bogdan Ilie', count: 9 },
    { label: 'Not recorded', count: 2 },
  ];

  it('draws one row per value, labelled', async () => {
    renderThemed(<TripBreakdownChart values={values} testId="chart-trip-people" />);

    const frame = await screen.findByTestId('chart-trip-people');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    for (const value of values) {
      expect(frame.textContent).toContain(value.label);
    }
  });

  it('is taller for more categories, so forty bars are not a smear', async () => {
    const many = Array.from({ length: 40 }, (_, i) => ({ label: `Person ${i}`, count: 40 - i }));
    const { container: few } = renderThemed(
      <TripBreakdownChart values={values} testId="chart-trip-people" />,
    );
    const short = (few.querySelector('[data-testid="chart-trip-people"]') as HTMLElement).style.height;
    cleanup();
    const { container: lots } = renderThemed(
      <TripBreakdownChart values={many} testId="chart-trip-areas" />,
    );
    const tall = (lots.querySelector('[data-testid="chart-trip-areas"]') as HTMLElement).style.height;
    expect(parseInt(tall, 10)).toBeGreaterThan(parseInt(short, 10));
  });

  it('cuts a name too long to leave the bars anywhere to go', async () => {
    const long = 'Peștera cu numele foarte lung de tot care nu încape';
    renderThemed(<TripBreakdownChart values={[{ label: long, count: 3 }]} testId="chart-trip-areas" />);

    const frame = await screen.findByTestId('chart-trip-areas');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    expect(frame.textContent).not.toContain(long);
    expect(frame.textContent).toContain(long.slice(0, 20));
  });
});
