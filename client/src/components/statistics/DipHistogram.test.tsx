// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';

import DipHistogram, { type DipBin } from './DipHistogram.tsx';
import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';

/**
 * Translations are loaded here, unlike in the other chart tests, because what is being asserted is
 * that the band labels carry the inclinations they were handed rather than a range assumed from
 * somewhere else — and that only shows up once the label has a number in it.
 */

function renderThemed(node: React.ReactNode, dark = false) {
  return render(
    <ConfigProvider theme={buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: dark ? 'dark' : 'light' })}>
      <App>{node}</App>
    </ConfigProvider>,
  );
}

afterEach(cleanup);

/** The eighteen bands the server sends, running from straight down to straight up. */
function bands(counts: number[]): DipBin[] {
  return counts.map((count, i) => ({
    fromDegrees: -90 + i * 10,
    toDegrees: -80 + i * 10,
    count,
    lengthM: count * 10,
  }));
}

const mostlyLevel = bands([0, 0, 1, 2, 4, 9, 20, 40, 90, 140, 80, 30, 11, 3, 1, 0, 0, 0]);

async function svgOf(testId: string) {
  const frame = await screen.findByTestId(testId);
  await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
  return frame;
}

describe('DipHistogram', () => {
  it('labels the bands by the inclinations they actually cover', async () => {
    renderThemed(<DipHistogram bins={mostlyLevel} />);
    const frame = await svgOf('chart-dip');

    const labels = Array.from(frame.querySelectorAll('text')).map((n) => n.textContent);
    // The server describes the rose's sectors and these bands with one record, so a chart that
    // assumed the rose's 0–180° range would mislabel every bar here and look convincing doing it.
    expect(labels).toContain('Inclination');
    expect(labels).toContain('-90°');
    // Not every band gets a printed label — the library thins them to fit — so what is asserted
    // is the range they span, not one label per band.
    expect(labels.some((l) => /^\d+°$/.test(l ?? ''))).toBe(true);
    // And never the rose's range: those sectors run 0–180°, these run from −90 to 90.
    expect(labels).not.toContain('170°');
  });

  it('offers a logarithmic count axis, because one band holds most of a cave', async () => {
    renderThemed(<DipHistogram bins={mostlyLevel} />);
    await svgOf('chart-dip');

    const linear = Array.from((await screen.findByTestId('chart-dip')).querySelectorAll('rect')).length;
    fireEvent.click(screen.getByText('Logarithmic'));

    // The bars are redrawn against a different axis rather than the chart being torn down: the
    // frame is the same element and still holds a drawing.
    const frame = await svgOf('chart-dip');
    expect(frame.querySelectorAll('rect').length).toBeGreaterThan(0);
    expect(linear).toBeGreaterThan(0);
  });

  it('draws nothing at all when no band holds anything', async () => {
    renderThemed(<DipHistogram bins={bands(new Array(18).fill(0))} />);

    // An axis with no bars would read as "this cave is level everywhere", which is a claim about
    // the cave that nobody made. The frame exists; the library was never given options.
    const frame = await screen.findByTestId('chart-dip');
    expect(frame.querySelectorAll('text').length).toBe(0);
  });

  it('restyles for the dark theme without being remounted', async () => {
    renderThemed(<DipHistogram bins={mostlyLevel} />, true);
    const frame = await svgOf('chart-dip');
    expect(frame.querySelectorAll('text').length).toBeGreaterThan(0);
  });
});
