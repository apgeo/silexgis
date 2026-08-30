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
