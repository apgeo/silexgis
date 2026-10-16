// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider, theme } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { KarstDensityLoadState } from '../../map/karstDensityLayer.ts';
import type { KarstIndexLoadState } from '../../map/karstIndexLayer.ts';
import { KarstDensityLegend, KarstIndexLegend } from './KarstLegend.tsx';

// The two karst keys, asserted as text: a legend is only worth its words and numbers.

function density(over: Partial<KarstDensityLoadState>): KarstDensityLoadState {
  return {
    status: 'ok',
    cellMetres: 10000,
    minimumCellMetres: 5000,
    bandMaxima: [],
    significanceZ: 1.96,
    mode: 'density',
    ...over,
  };
}

function index(over: Partial<KarstIndexLoadState>): KarstIndexLoadState {
  return { status: 'ok', areaCount: 3, truncated: false, maxAreas: 500, ...over };
}

afterEach(() => cleanup());

describe('KarstDensityLegend', () => {
  it('states the value range of every band, lowest first', async () => {
    await i18n.changeLanguage('en');
    render(<KarstDensityLegend state={density({ bandMaxima: [0.2, 0.4, 0.6, 0.8, 1] })} />);
    const rows = screen.getAllByTestId('map-karst-density-legend-row').map((row) => row.textContent);
    expect(rows).toHaveLength(5);
    expect(rows[0]).toBe('0 – 0.2');
    expect(rows[4]).toBe('0.8 – 1');
  });

  it('names hot and cold spots by the threshold the server stated', async () => {
    await i18n.changeLanguage('en');
    render(<KarstDensityLegend state={density({ mode: 'hotSpot', significanceZ: 2.58 })} />);
    const text = screen.getByTestId('map-karst-density-legend').textContent;
    expect(text).toContain('2.58');
    expect(text).toMatch(/Hot spot.*Cold spot/);
  });

  it('says no caves are counted rather than drawing an empty key, and nothing before an answer', async () => {
    await i18n.changeLanguage('en');
    const { rerender } = render(<KarstDensityLegend state={density({ status: 'loading' })} />);
    expect(screen.queryByTestId('map-karst-density-legend')).toBeNull();
    rerender(<KarstDensityLegend state={density({ bandMaxima: [] })} />);
    expect(screen.getByTestId('map-karst-density-legend-empty')).toBeTruthy();
  });
});

describe('KarstIndexLegend', () => {
  it('lists every class with the unmeasurable one last, in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    render(<KarstIndexLegend state={index({})} />);
    const rows = screen.getAllByTestId('map-karst-index-legend-row').map((row) => row.textContent);
    expect(rows).toHaveLength(6);
    expect(rows[0]).toBe(i18n.t('karstArea.class.veryLow'));
    expect(rows[5]).toBe(i18n.t('karstArea.class.unknown'));
    await i18n.changeLanguage('en');
  });

  it('says some areas are not shown only when the answer was truncated', async () => {
    await i18n.changeLanguage('en');
    const { rerender } = render(<KarstIndexLegend state={index({})} />);
    expect(screen.queryByTestId('map-karst-index-truncated')).toBeNull();
    rerender(<KarstIndexLegend state={index({ truncated: true, maxAreas: 500 })} />);
    expect(screen.getByTestId('map-karst-index-truncated').textContent).toMatch(/not shown.*500/);
  });
});

describe('legend swatches', () => {
  // The fills are translucent, so on the dark panel a swatch laid straight on the background would
  // take the panel's colour; each one is ringed with the theme's own border colour and painted over
  // a light underlay, in both keys.
  it('ring every swatch with the dark theme border and paint it over an underlay', async () => {
    await i18n.changeLanguage('en');
    const darkBorder = theme.getDesignToken({ algorithm: theme.darkAlgorithm }).colorBorder;
    render(
      <ConfigProvider theme={{ algorithm: theme.darkAlgorithm }}>
        <KarstIndexLegend state={index({})} />
        <KarstDensityLegend state={density({ bandMaxima: [0.2, 0.4, 0.6, 0.8, 1] })} />
      </ConfigProvider>,
    );
    const swatches = screen.getAllByTestId('map-karst-legend-swatch');
    expect(swatches).toHaveLength(11);
    for (const swatch of swatches) {
      expect(swatch.style.boxShadow).toBe(`0 0 0 1px ${darkBorder}`);
      // Read off the attribute: jsdom does not round-trip a layered background through
      // style.background.
      expect(swatch.getAttribute('style')).toMatch(/background: linear-gradient\([^;]*\), rgb\(255, 255, 255\);/);
    }
  });
});
