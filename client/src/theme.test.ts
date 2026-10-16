// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildThemeConfig, resolveDark, calendarDayContentStyle } from './theme.ts';
import { DEFAULT_APPEARANCE } from './stores/uiPrefsStore.ts';

function systemPrefersDark(dark: boolean) {
  vi.spyOn(window, 'matchMedia').mockImplementation((query: string) => ({
    matches: dark,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }) as MediaQueryList);
}

afterEach(() => vi.restoreAllMocks());

describe('buildThemeConfig', () => {
  it('uses the dark algorithm when dark is chosen', () => {
    const config = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark' });

    expect(config.algorithm).toContain(theme.darkAlgorithm);
  });

  it('uses the light algorithm when light is chosen, whatever the system says', () => {
    systemPrefersDark(true);

    const config = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'light' });

    expect(config.algorithm).toContain(theme.defaultAlgorithm);
    expect(config.algorithm).not.toContain(theme.darkAlgorithm);
  });

  it('follows the system when asked to', () => {
    systemPrefersDark(true);
    expect(resolveDark('system')).toBe(true);

    systemPrefersDark(false);
    expect(resolveDark('system')).toBe(false);
  });

  it('adds the compact algorithm alongside the theme, not instead of it', () => {
    const config = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark', density: 'compact' });

    expect(config.algorithm).toContain(theme.darkAlgorithm);
    expect(config.algorithm).toContain(theme.compactAlgorithm);
  });

  it('turns component motion off when motion is reduced', () => {
    expect(buildThemeConfig({ ...DEFAULT_APPEARANCE, reduceMotion: true }).token?.motion).toBe(false);
    expect(buildThemeConfig(DEFAULT_APPEARANCE).token?.motion).toBe(true);
  });

  it('keeps the brand colour whatever the appearance', () => {
    expect(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark' }).token?.colorPrimary).toBe('#146262');
  });

  /**
   * The calendar's shipped day cell is three rows tall and scrolls the rest of the day away, so a
   * Saturday with four records shows three of them and hides the fourth behind a scrollbar inside
   * a table cell. This is the override that stops it, and it is pinned because the failure it
   * prevents is invisible: a clipped cell looks exactly like a day with less on it.
   */
  it('lets a day cell grow to hold everything on that day', () => {
    expect(calendarDayContentStyle.height).toBe('auto');
    expect(calendarDayContentStyle.overflowY).not.toBe('auto');
    expect(calendarDayContentStyle.overflowY).not.toBe('scroll');
    expect(calendarDayContentStyle.maxHeight).toBe('none');
    // A floor, so that a month of quiet days still reads as a grid rather than as ragged strips.
    expect(calendarDayContentStyle.minHeight).toBeGreaterThan(0);
  });
});

/** WCAG relative luminance contrast of a colour drawn over an opaque background. */
function contrast(foreground: string, background: string): number {
  const parse = (colour: string): number[] => {
    const value = colour.trim();
    if (value.startsWith('#')) {
      const hex = value.length === 4 ? value.slice(1).split('').map((c) => c + c).join('') : value.slice(1);
      const n = parseInt(hex.slice(0, 6), 16);
      const alpha = hex.length === 8 ? parseInt(hex.slice(6), 16) / 255 : 1;
      return [(n >> 16) & 255, (n >> 8) & 255, n & 255, alpha];
    }
    const parts = /rgba?\(([^)]+)\)/.exec(value)![1].split(',').map((p) => parseFloat(p));
    return [parts[0], parts[1], parts[2], parts[3] ?? 1];
  };
  const back = parse(background);
  const fore = parse(foreground);
  const flattened = [0, 1, 2].map((i) => fore[i] * fore[3] + back[i] * (1 - fore[3]));
  const luminance = ([r, g, b]: number[]) => {
    const channel = (v: number) => {
      const s = v / 255;
      return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
  };
  const a = luminance(flattened);
  const b = luminance(back);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/**
 * Every pair is measured on the tokens antd actually derives, not on the values written in the
 * theme, because the algorithms rewrite seeds: the dark algorithm darkens the brand colour and the
 * link colour, and a ratio computed from what was written would pass while the screen failed.
 */
describe.each(['light', 'dark'] as const)('contrast in the %s theme', (mode) => {
  const config = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode });
  const token = theme.getDesignToken(config);
  const surfaces = { page: token.colorBgContainer, raised: token.colorBgElevated };

  it.each([
    ['body text', token.colorText],
    ['secondary text', token.colorTextSecondary],
    ['description text', token.colorTextDescription],
    ['placeholder text', token.colorTextPlaceholder],
    ['links', token.colorLink],
    ['error text', token.colorErrorText],
    ['warning text', token.colorWarningText],
    ['success text', token.colorSuccessText],
    ['info text', token.colorInfoText],
  ])('%s reads at 4.5:1 on the page and on raised surfaces', (_, colour) => {
    expect(contrast(colour, surfaces.page)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(colour, surfaces.raised)).toBeGreaterThanOrEqual(4.5);
  });

  it('description text also reads on the grey layout background', () => {
    expect(contrast(token.colorTextDescription, token.colorBgLayout)).toBeGreaterThanOrEqual(4.5);
  });

  it('text on a primary or a danger button reads at 4.5:1', () => {
    expect(contrast(token.colorTextLightSolid, token.colorPrimary)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token.colorTextLightSolid, token.colorError)).toBeGreaterThanOrEqual(4.5);
  });

  it('the focus ring and a field outline stand out at 3:1', () => {
    expect(contrast(token.colorPrimaryBorder, surfaces.page)).toBeGreaterThanOrEqual(3);
    expect(contrast(token.colorPrimaryBorder, surfaces.raised)).toBeGreaterThanOrEqual(3);
    expect(contrast(token.colorBorder, surfaces.page)).toBeGreaterThanOrEqual(3);
    expect(contrast(token.colorBorder, surfaces.raised)).toBeGreaterThanOrEqual(3);
  });

  it('the selected item of a menu reads at 4.5:1 on its highlight', () => {
    const selectedText = config.components?.Menu?.itemSelectedColor ?? token.colorPrimary;
    expect(contrast(selectedText, token.colorPrimaryBg)).toBeGreaterThanOrEqual(4.5);
  });

  it('the selected tab reads at 4.5:1', () => {
    const selectedTab = config.components?.Tabs?.itemSelectedColor ?? token.colorPrimary;
    expect(contrast(selectedTab, surfaces.page)).toBeGreaterThanOrEqual(4.5);
  });
});
