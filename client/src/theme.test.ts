// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildThemeConfig, resolveDark, calendarDayContentStyle } from './theme.ts';
import { DEFAULT_APPEARANCE } from './stores/uiPrefsStore.ts';
import { readFileSync } from 'node:fs';
import path from 'node:path';

// Read as text, from the client package the test runner is rooted at: the test environment does
// not load stylesheets.
const mapPageCss = readFileSync(path.resolve(process.cwd(), 'src/pages/MapPage.css'), 'utf8');
const indexCss = readFileSync(path.resolve(process.cwd(), 'src/index.css'), 'utf8');

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

  it('the brand colour drawn as text reads at 4.5:1', () => {
    for (const colour of [token.colorPrimaryText, token.colorPrimaryTextHover, token.colorPrimaryTextActive]) {
      expect(contrast(colour, surfaces.page)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(colour, surfaces.raised)).toBeGreaterThanOrEqual(4.5);
    }
  });

  // The places antd draws the brand colour as text, each read the way that component resolves it:
  // its own token where it has one, else the brand colour as overridden for that component alone,
  // else the global brand colour.
  const brandFor = (component: 'Pagination' | 'Radio' | 'Steps') =>
    (config.components?.[component] as { colorPrimary?: string } | undefined)?.colorPrimary ?? token.colorPrimary;

  it("the active page's number reads at 4.5:1", () => {
    const pagination = config.components?.Pagination;
    expect(contrast(pagination?.itemActiveColor ?? token.colorPrimary, surfaces.page)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(pagination?.itemActiveColorHover ?? token.colorPrimaryHover, surfaces.page)).toBeGreaterThanOrEqual(4.5);
    // Its border marks which page is current alongside the bold number.
    expect(contrast(brandFor('Pagination'), surfaces.page)).toBeGreaterThanOrEqual(3);
  });

  it('a checked radio button reads at 4.5:1', () => {
    expect(contrast(brandFor('Radio'), surfaces.page)).toBeGreaterThanOrEqual(4.5);
  });

  it("the steps' icons read at 4.5:1", () => {
    const steps = config.components?.Steps;
    // A finished step: the brand colour on its pale circle.
    expect(contrast(brandFor('Steps'), token.colorPrimaryBg)).toBeGreaterThanOrEqual(4.5);
    // The current step: its number on a circle of the brand colour.
    expect(contrast(steps?.colorTextLightSolid ?? token.colorTextLightSolid, brandFor('Steps'))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['red', 'volcano', 'orange', 'gold', 'yellow', 'lime', 'green', 'cyan', 'blue', 'geekblue', 'purple', 'magenta'])(
    'a %s tag reads at 4.5:1 on its own background',
    (colour) => {
      const tag = (config.components?.Tag ?? {}) as Record<string, string | undefined>;
      const palette = token as unknown as Record<string, string>;
      expect(contrast(tag[`${colour}7`] ?? palette[`${colour}7`], palette[`${colour}1`])).toBeGreaterThanOrEqual(4.5);
    },
  );

  it.each(['Success', 'Warning', 'Error', 'Info'])('a %s status tag reads at 4.5:1 on its own background', (status) => {
    const tag = (config.components?.Tag ?? {}) as Record<string, string | undefined>;
    const palette = token as unknown as Record<string, string>;
    expect(contrast(tag[`color${status}`] ?? palette[`color${status}`], palette[`color${status}Bg`])).toBeGreaterThanOrEqual(4.5);
  });

  it('an empty table says so at 4.5:1', () => {
    const empty = (config.components?.Table as { colorTextDisabled?: string } | undefined)?.colorTextDisabled;
    expect(contrast(empty ?? token.colorTextDisabled, surfaces.page)).toBeGreaterThanOrEqual(4.5);
  });

  it("a default button's text under the pointer reads at 4.5:1", () => {
    const button = config.components?.Button;
    expect(contrast(button?.defaultHoverColor ?? token.colorPrimaryHover, surfaces.page)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(button?.defaultActiveColor ?? token.colorPrimaryActive, surfaces.page)).toBeGreaterThanOrEqual(4.5);
  });
});

/** The custom properties a CSS rule sets or reads, in the order they appear in it. */
function propertiesIn(css: string, selector: string): string[] {
  const start = css.indexOf(selector);
  expect(start, `${selector} is in the stylesheet`).toBeGreaterThanOrEqual(0);
  const body = css.slice(css.indexOf('{', start), css.indexOf('}', start));
  return [...body.matchAll(/var\((--ant-[a-z-]+)/g)].map((match) => match[1]);
}

/** An antd token's name from the custom property antd publishes it as. */
function tokenOf(property: string): string {
  return property.replace(/^--ant-/, '').replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase());
}

describe.each(['light', 'dark'] as const)('rings and overrides drawn by the stylesheet, %s theme', (mode) => {
  const token = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode })) as unknown as Record<
    string,
    string
  >;

  /**
   * The map's focus ring is two tones drawn inside the map, so what has to stand out is the outer
   * tone against the inner one — read from the stylesheet itself, so a ring re-pointed at a token
   * the theme does not hold at contrast (the brand colour, in the dark theme) fails here.
   */
  it("the map's focus ring stands out at 3:1 from its own inner tone", () => {
    const [inner, outer] = propertiesIn(mapPageCss, '.map-canvas:focus-visible::after');
    expect(contrast(token[tokenOf(outer)], token[tokenOf(inner)])).toBeGreaterThanOrEqual(3);
  });

  it('the text shades the stylesheet gives non-solid primary buttons read at 4.5:1', () => {
    const properties = propertiesIn(indexCss, ".ant-btn-color-primary:not(.ant-btn-variant-solid)");
    expect(properties).toHaveLength(3);
    for (const property of properties) {
      expect(contrast(token[tokenOf(property)], token.colorBgContainer)).toBeGreaterThanOrEqual(4.5);
    }
  });
});
