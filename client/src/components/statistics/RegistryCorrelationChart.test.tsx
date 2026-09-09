// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';

import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import { buildThemeConfig } from '../../theme.ts';
import { RegistryCorrelationChart } from './RegistryCorrelationChart.tsx';

function renderThemed(node: React.ReactNode) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

afterEach(cleanup);

describe('the registry correlation chart', () => {
  it('draws the fitted line between the ends it was handed', async () => {
    renderThemed(
      <RegistryCorrelationChart
        line={[
          [10, 3],
          [1000, 30],
        ]}
        logarithmic
        xLabel="Surveyed length (m)"
        yLabel="Depth (m)"
      />,
    );

    const frame = await screen.findByTestId('chart-registry-correlation');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());

    const labels = Array.from(frame.querySelectorAll('text')).map((node) => node.textContent);
    expect(labels).toContain('Surveyed length (m)');
    expect(labels).toContain('Depth (m)');
    // One drawn stroke for the fit itself, whatever else the axes draw.
    await waitFor(() =>
      expect(frame.querySelectorAll('path[stroke]').length).toBeGreaterThan(0),
    );
  });

  /**
   * The one thing this chart must never do: stand there with axes and no line, which reads as a
   * relationship that is flat rather than as a relationship nobody could fit.
   *
   * Asserted from a chart that has demonstrably drawn, and not from a freshly mounted one. The
   * frame is empty for a tick after mounting whatever it is given — the drawing happens in an
   * effect — so a zero count taken on a bare mount is satisfied before anything could have been
   * drawn, and would hold just as well for a component that went on to draw a full set of axes.
   */
  it('draws nothing at all — not even axes — where there was no fit', async () => {
    const { rerender } = renderThemed(
      <RegistryCorrelationChart
        line={[
          [10, 3],
          [1000, 30],
        ]}
        logarithmic
        xLabel="Surveyed length (m)"
        yLabel="Depth (m)"
      />,
    );

    const frame = await screen.findByTestId('chart-registry-correlation');
    await waitFor(() => expect(frame.querySelectorAll('text').length).toBeGreaterThan(0));

    rerender(
      <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' })}>
        <App>
          <RegistryCorrelationChart
            line={null}
            logarithmic
            xLabel="Surveyed length (m)"
            yLabel="Depth (m)"
          />
        </App>
      </ConfigProvider>,
    );

    await waitFor(() => expect(frame.querySelectorAll('text').length).toBe(0));
    expect(frame.querySelectorAll('path').length).toBe(0);
  });
});
