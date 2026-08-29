// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';

import RoseDiagram from './RoseDiagram.tsx';
import type { RoseBin } from './roseGeometry.ts';
import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';

/**
 * The rose is hand-drawn, so unlike the library charts its elements can be asserted on directly:
 * a wedge is a path this component wrote, and which wedges exist is the claim the diagram makes.
 *
 * <p>
 * Translations are not loaded here, so what appears is the key itself — and asserting on the key
 * is the stronger check, because it proves the sentence was asked for through the translator
 * rather than written out in English at the call site.
 * </p>
 */

function renderThemed(node: React.ReactNode, dark = false) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: dark ? 'dark' : 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

// No auto-cleanup is configured in this project, so each test tidies up after itself.
afterEach(cleanup);

function bin(fromDegrees: number, count: number, lengthM: number): RoseBin {
  return { fromDegrees, toDegrees: fromDegrees + 10, count, lengthM, countFraction: 0, lengthFraction: 0 };
}

function withFractions(bins: RoseBin[]): RoseBin[] {
  const totalCount = bins.reduce((sum, b) => sum + b.count, 0);
  const totalLength = bins.reduce((sum, b) => sum + b.lengthM, 0);
  return bins.map((b) => ({
    ...b,
    countFraction: totalCount > 0 ? b.count / totalCount : 0,
    lengthFraction: totalLength > 0 ? b.lengthM / totalLength : 0,
  }));
}

/** A chamber surveyed in many short legs, against one long straight gallery across it. */
const chamberAndGallery = withFractions([bin(0, 40, 120), bin(90, 4, 900)]);

describe('the passage rose', () => {
  it('draws one measured sector as two opposed wedges', () => {
    renderThemed(<RoseDiagram bins={withFractions([bin(40, 5, 100)])} />);

    const wedges = screen.getByTestId('chart-rose').querySelectorAll('path[data-petal]');
    expect(wedges).toHaveLength(2);
    expect(wedges[0].getAttribute('data-petal')).toBe('measured');
    expect(wedges[0].getAttribute('data-from')).toBe('40');
    expect(wedges[1].getAttribute('data-petal')).toBe('mirrored');
    expect(wedges[1].getAttribute('data-from')).toBe('220');
  });

  it('draws nothing at all rather than empty rings when no direction was measured', () => {
    // A ringed circle with no wedges reads as "the passages of this cave run nowhere", which is a
    // claim the data does not make. Whoever mounts the diagram says why it is absent instead.
    const { container } = renderThemed(<RoseDiagram bins={withFractions([bin(0, 0, 0), bin(90, 0, 0)])} />);

    expect(screen.queryByTestId('chart-rose')).toBeNull();
    expect(container.querySelector('svg')).toBeNull();
  });

  it('names the weighting in force, and changes it when the reader asks', () => {
    renderThemed(<RoseDiagram bins={chamberAndGallery} />);

    const description = () => screen.getByTestId('chart-rose').querySelector('desc')?.textContent;
    expect(description()).toBe('statistics.orientation.roseDescriptionLength');

    fireEvent.click(screen.getByRole('radio', { name: 'statistics.orientation.byCount' }));
    expect(description()).toBe('statistics.orientation.roseDescriptionCount');
  });

  it('opens on the weighting it was told to open on', () => {
    renderThemed(<RoseDiagram bins={chamberAndGallery} defaultWeighting="count" />);

    expect(screen.getByTestId('chart-rose').querySelector('desc')?.textContent).toBe(
      'statistics.orientation.roseDescriptionCount',
    );
  });

  it('is reachable as one image with a title and a description', () => {
    renderThemed(<RoseDiagram bins={chamberAndGallery} />);

    const svg = screen.getByRole('img');
    const labelled = (svg.getAttribute('aria-labelledby') ?? '').split(' ');
    expect(labelled).toHaveLength(2);
    expect(svg.querySelector('title')?.id).toBe(labelled[0]);
    expect(svg.querySelector('desc')?.id).toBe(labelled[1]);
    expect(svg.querySelector('title')?.textContent).toBe('statistics.orientation.rose');
  });

  it('puts north at the top of the drawing', () => {
    renderThemed(<RoseDiagram bins={chamberAndGallery} />);

    const labels = Array.from(screen.getByTestId('chart-rose').querySelectorAll('text'));
    const north = labels.find((n) => n.textContent === 'statistics.orientation.north');
    const south = labels.find((n) => n.textContent === 'statistics.orientation.south');
    const east = labels.find((n) => n.textContent === 'statistics.orientation.east');

    expect(north).toBeDefined();
    expect(Number(north?.getAttribute('y'))).toBeLessThan(Number(south?.getAttribute('y')));
    expect(Number(east?.getAttribute('x'))).toBeGreaterThan(Number(north?.getAttribute('x')));
  });

  it('shows the mean trend of the weighting in force, and only where there is one', () => {
    renderThemed(<RoseDiagram bins={chamberAndGallery} meanAxis={{ length: 88, count: null }} />);

    expect(screen.getByTestId('rose-mean-axis')).toBeTruthy();
    expect(screen.getByText('statistics.orientation.meanAxis')).toBeTruthy();

    fireEvent.click(screen.getByRole('radio', { name: 'statistics.orientation.byCount' }));
    expect(screen.queryByTestId('rose-mean-axis')).toBeNull();
  });

  it('draws in the dark theme without being remounted', () => {
    const { rerender } = render(
      <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' })}>
        <App>
          <RoseDiagram bins={chamberAndGallery} />
        </App>
      </ConfigProvider>,
    );
    const lightFill = screen.getByTestId('chart-rose').querySelector('path[data-petal]')?.getAttribute('fill');

    rerender(
      <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark' })}>
        <App>
          <RoseDiagram bins={chamberAndGallery} />
        </App>
      </ConfigProvider>,
    );
    const darkFill = screen.getByTestId('chart-rose').querySelector('path[data-petal]')?.getAttribute('fill');

    expect(lightFill).toBeTruthy();
    expect(darkFill).toBeTruthy();
    // The colours come from the theme bridge, so a theme change restyles what is already drawn.
    expect(screen.getAllByTestId('chart-rose')).toHaveLength(1);
  });
});
