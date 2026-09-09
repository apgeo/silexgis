// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';

import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import { buildThemeConfig } from '../../theme.ts';
import { RegistryDistributionChart } from './RegistryDistributionChart.tsx';
import type { DistributionBar } from './registryDistribution.ts';

function renderThemed(node: React.ReactNode) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

afterEach(cleanup);

const bars: DistributionBar[] = [
  { lowerBound: 0, upperBound: 100, count: 6, merged: false, label: '0' },
  { lowerBound: 100, upperBound: 400, count: 3, merged: true, label: '100–400 joined' },
  { lowerBound: 400, upperBound: 500, count: 5, merged: false, label: '400' },
];

describe('the registry distribution chart', () => {
  it('draws the intervals it was given and names the joined one on the axis', async () => {
    renderThemed(<RegistryDistributionChart bars={bars} xLabel="Surveyed length (m)" />);

    const frame = await screen.findByTestId('chart-registry-distribution');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());

    const labels = Array.from(frame.querySelectorAll('text')).map((node) => node.textContent);
    expect(labels).toContain('Surveyed length (m)');
    // The label says it was joined, on the axis, where somebody reading the bar widths is
    // looking — not only in a tooltip nobody hovers.
    expect(labels).toContain('100–400 joined');
  });

  /**
   * Asserted from a chart that has demonstrably drawn. The frame is empty for a tick after
   * mounting whatever it is given, so a zero count taken on a bare mount passes before anything
   * could have been drawn — including for a component that goes on to draw a full set of axes
   * over no bars, which is the state this is meant to rule out.
   */
  it('draws nothing at all when there are no intervals', async () => {
    const { rerender } = renderThemed(
      <RegistryDistributionChart bars={bars} xLabel="Surveyed length (m)" />,
    );

    const frame = await screen.findByTestId('chart-registry-distribution');
    await waitFor(() => expect(frame.querySelectorAll('text').length).toBeGreaterThan(0));

    rerender(
      <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' })}>
        <App>
          <RegistryDistributionChart bars={[]} xLabel="Surveyed length (m)" />
        </App>
      </ConfigProvider>,
    );

    await waitFor(() => expect(frame.querySelectorAll('text').length).toBe(0));
  });
});
