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
 * The colours the screens are drawn in are antd's own derivation from the brand colour, in both
 * themes, with one exception: the dark theme's keyboard focus ring. Pinned value by value, so that
 * a token override slipping in (or the library's derivation moving under an upgrade) changes the
 * look on purpose rather than silently.
 */
describe('the look the theme draws', () => {
  const restored = {
    light: {
      colorBorder: '#d9d9d9',
      colorTextTertiary: 'rgba(0,0,0,0.45)',
      colorTextDescription: 'rgba(0,0,0,0.45)',
      colorTextPlaceholder: 'rgba(0,0,0,0.25)',
      colorTextDisabled: 'rgba(0,0,0,0.25)',
      colorPrimaryBg: '#97a19f',
      colorPrimaryBgHover: '#7c9491',
      colorPrimaryBorder: '#5c8783',
      colorPrimaryText: '#146262',
      colorLink: '#1677ff',
      colorError: '#ff4d4f',
      colorErrorText: '#ff4d4f',
      colorWarningText: '#faad14',
      colorSuccessText: '#52c41a',
      colorInfoText: '#1677ff',
      gold7: '#d48806',
      yellow7: '#d4b106',
      green7: '#389e0d',
    },
    dark: {
      colorBorder: '#424242',
      colorTextTertiary: 'rgba(255,255,255,0.45)',
      colorTextPlaceholder: 'rgba(255,255,255,0.25)',
      colorTextDisabled: 'rgba(255,255,255,0.25)',
      colorPrimary: '#145656',
      colorPrimaryText: '#145656',
      colorPrimaryTextHover: '#256562',
      colorPrimaryTextActive: '#144747',
      colorLink: '#1668dc',
      colorError: '#dc4446',
      colorErrorText: '#dc4446',
      colorErrorTextHover: '#e86e6b',
      colorErrorTextActive: '#ad393a',
      colorInfoText: '#1668dc',
      geekblue7: '#5273e0',
      purple7: '#854eca',
      // The one replaced value: the keyboard focus ring.
      colorPrimaryBorder: '#3fa8a8',
    },
  } as const;

  it.each(['light', 'dark'] as const)("the %s theme draws antd's own colours", (mode) => {
    const token = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode })) as unknown as Record<
      string,
      string
    >;
    for (const [name, value] of Object.entries(restored[mode])) {
      expect(token[name], name).toBe(value);
    }
  });

  it.each(['light', 'dark'] as const)('the %s theme overrides no component colour beyond the slider', (mode) => {
    const components = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode }).components ?? {};
    expect(Object.keys(components).sort()).toEqual(mode === 'dark' ? ['Layout', 'Slider'] : ['Layout']);
  });

  it("the dark theme's slider keeps the colour the dark algorithm gives it, not the focus ring's", () => {
    const slider = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark' }).components?.Slider;
    const derived = theme.getDesignToken({ token: { colorPrimary: '#146262' }, algorithm: theme.darkAlgorithm });
    expect(slider?.trackBg).toBe(derived.colorPrimaryBorder);
    expect(slider?.handleColor).toBe(derived.colorPrimaryBorder);
    expect(slider?.dotActiveBorderColor).toBe(derived.colorPrimaryBorder);
  });

  /**
   * The two other things antd draws from the replaced colour, handed back in the stylesheet: read
   * from it, so a value that drifts from what the dark algorithm derives — or a rule that goes —
   * fails here.
   */
  it("the dark theme's pressed primary button and preset tags keep the colour the dark algorithm gives them", () => {
    const derived = theme.getDesignToken({ token: { colorPrimary: '#146262' }, algorithm: theme.darkAlgorithm });
    expect(derived.colorPrimaryBorder).not.toBe(restored.dark.colorPrimaryBorder);
    const declared = (selector: string, property: string) => {
      const start = indexCss.indexOf(selector);
      expect(start, `${selector} is in the stylesheet`).toBeGreaterThanOrEqual(0);
      const body = indexCss.slice(indexCss.indexOf('{', start) + 1, indexCss.indexOf('}', start));
      return new RegExp(`${property}:\\s*([^;]+);`).exec(body)?.[1]?.trim();
    };
    expect(declared("html[data-theme='dark']:not([data-contrast='high']) .ant-btn.ant-btn-color-primary", '--ant-btn-color-light-active'))
      .toBe(derived.colorPrimaryBorder);
    expect(declared("html[data-theme='dark']:not([data-contrast='high']) .ant-picker-dropdown .ant-picker-ranges .ant-picker-preset > .ant-tag-blue", 'border-color'))
      .toBe(derived.colorPrimaryBorder);
  });

  it('draws the map workspace panel on the fixed white it always had in the light theme', () => {
    const start = mapPageCss.indexOf('\n.map-workspace-panel {');
    expect(start).toBeGreaterThanOrEqual(0);
    expect(mapPageCss.slice(start, mapPageCss.indexOf('}', start))).toMatch(/background:\s*#fff;/);
  });

  it("draws the map workspace panel on the dark theme's container background, where its light text reads", () => {
    expect(propertiesIn(mapPageCss, "html[data-theme='dark'] .map-workspace-panel {")).toEqual(['--ant-color-bg-container']);
    const dark = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark' }));
    expect(contrast(dark.colorText, dark.colorBgContainer)).toBeGreaterThanOrEqual(4.5);
    // What the dark rule is there to prevent: the dark theme's text on the light theme's white.
    expect(contrast(dark.colorText, '#ffffff')).toBeLessThan(4.5);
  });
});

/**
 * The keyboard focus ring is held at 3:1 against the page and the raised surfaces in both themes.
 * It is the one colour kept from the contrast work: it appears only while moving through a screen
 * with the keyboard, and without it the dark theme shows no focus at all.
 */
describe.each(['light', 'dark'] as const)('the focus ring in the %s theme', (mode) => {
  const token = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode }));

  it('stands out at 3:1 from the page and from raised surfaces', () => {
    expect(contrast(token.colorPrimaryBorder, token.colorBgContainer)).toBeGreaterThanOrEqual(3);
    expect(contrast(token.colorPrimaryBorder, token.colorBgElevated)).toBeGreaterThanOrEqual(3);
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

describe.each(['light', 'dark'] as const)('the ring the stylesheet draws on the map, %s theme', (mode) => {
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
});

/**
 * Every pair is measured on the tokens antd actually derives, not on the values written in the
 * theme, because the algorithms rewrite seeds: the dark algorithm darkens the brand colour and the
 * link colour, and a ratio computed from what was written would pass while the screen failed.
 */
describe.each(['light', 'dark'] as const)('higher contrast in the %s theme', (mode) => {
  const config = buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode, highContrast: true });
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

describe('the higher-contrast switch', () => {
  it.each([
    ['light', '#d9d9d9', '#8c8c8c'],
    ['dark', '#424242', '#6b6b6b'],
  ] as const)('changes the %s theme\'s field outline from the standard colour to the readable one', (mode, standard, high) => {
    const off = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode }));
    const on = theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode, highContrast: true }));
    expect(off.colorBorder).toBe(standard);
    expect(on.colorBorder).toBe(high);
  });

  it('is off unless chosen', () => {
    expect(DEFAULT_APPEARANCE.highContrast).toBe(false);
  });

  it("keeps the dark theme's slider on the readable teal rather than handing it back", () => {
    expect(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark', highContrast: true }).components?.Slider).toBeUndefined();
  });
});

describe('overrides the stylesheet draws for the higher-contrast dark theme', () => {
  const token = theme.getDesignToken(
    buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: 'dark', highContrast: true }),
  ) as unknown as Record<string, string>;

  it.each([
    "html[data-theme='dark'][data-contrast='high'] .ant-btn-color-primary:not(.ant-btn-variant-solid)",
    "html[data-theme='dark'][data-contrast='high'] .ant-btn-color-dangerous:not(.ant-btn-variant-solid)",
  ])('the text shades of %s read at 4.5:1, at rest, hovered and pressed', (selector) => {
    const properties = propertiesIn(indexCss, selector);
    expect(properties).toHaveLength(3);
    for (const property of properties) {
      expect(contrast(token[tokenOf(property)], token.colorBgContainer), property).toBeGreaterThanOrEqual(4.5);
    }
  });
});
