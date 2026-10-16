// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CSSProperties } from 'react';
import { theme, type ThemeConfig } from 'antd';
import type { Appearance } from './stores/uiPrefsStore.ts';

// All theming through antd tokens — no scattered inline styles.
export const themeConfig: ThemeConfig = {
  token: {
    colorPrimary: '#146262',
    borderRadius: 4,
  },
  components: {
    Layout: {
      // A shorter bar than antd's 64px default. The header holds one row of controls and no
      // wrapping content, so the height it saves goes to the map and the tables underneath —
      // which is most of what anyone has this application open to look at.
      //
      // Set here rather than as a height on the element: antd lays the header out from this
      // token, and an element style that disagreed with it would leave the bar the right size
      // with its contents still centred against the old one.
      headerHeight: 48,
      headerPadding: '0 16px',
    },
  },
};

/**
 * Token overrides that bring text and controls up to readable contrast — 4.5:1 for text, 3:1 for
 * the outline of a control and for the focus ring — against the page and against the raised
 * surfaces (dialogs, menus) it sits on.
 *
 * antd's own defaults miss these in several places, and two of the misses come from the brand
 * colour rather than from antd: the palette generated from a teal this dark has a grey-green as
 * its lightest step, which antd puts behind the selected row of every menu, list and picker — so
 * the selected page in the navigation read at 2.7:1 — and the dark algorithm darkens the brand
 * colour further, which left the focus ring at 1.2:1 against the dark page, i.e. not there. The
 * seed stays the brand colour in both modes; only derived values are replaced, and where a value
 * is itself a seed (link, error) it is chosen so that what the algorithm makes of it lands where
 * it must.
 *
 * Each mode has its own set because the two fail in different places: the darker amber that
 * rescues warning text on the light page is exactly the one that fails on the dark page.
 * `theme.test.ts` recomputes every ratio from the tokens antd derives, so a change here that
 * drops a pair below its threshold fails there rather than on somebody's screen.
 */
/**
 * A coloured tag writes its words in the seventh step of its colour's palette on the first step;
 * for the warm and green palettes that is under 4.5:1 on the light theme (green 3.4, gold 2.8,
 * yellow 2.1), and for two cool ones on the dark theme. The words take the first darker (light
 * theme) or lighter (dark theme) step that reads; the tag's background and outline keep antd's.
 * The status tags (success, warning, info, error) write in the status colour itself, which is a
 * fill colour; they take the status's text colour instead.
 */
const lightTagText = {
  volcano7: '#ad2102',
  orange7: '#ad4e00',
  gold7: '#874d00',
  yellow7: '#876800',
  lime7: '#3f6600',
  green7: '#237804',
  cyan7: '#006d75',
  colorSuccess: '#237804',
  colorWarning: '#874d00',
  colorInfo: '#0958d9',
} satisfies NonNullable<ThemeConfig['components']>['Tag'];

const darkTagText = {
  geekblue7: '#7f9ef3',
  purple7: '#ab7ae0',
  colorError: '#ff7875',
  colorInfo: '#69b1ff',
} satisfies NonNullable<ThemeConfig['components']>['Tag'];

const lightContrast: ThemeConfig = {
  token: {
    colorPrimaryBg: '#e3efee',
    colorPrimaryBgHover: '#cde3e1',
    colorLink: '#0958d9',
    colorTextTertiary: 'rgba(0, 0, 0, 0.56)',
    colorTextPlaceholder: 'rgba(0, 0, 0, 0.54)',
    colorError: '#d32f2f',
    colorWarningText: '#874d00',
    colorSuccessText: '#237804',
    colorInfoText: '#0958d9',
    // The outline is all that marks out an empty field or a default button.
    colorBorder: '#8c8c8c',
  },
  components: { Tag: lightTagText },
};

/**
 * The brand teal as the dark theme draws it as TEXT — selected items, the active page number, a
 * checked button, an outlined or text-only primary button — and its hover and pressed shades.
 *
 * The dark algorithm darkens the brand colour to a fill that carries white text well (8.4:1) but
 * reads at 2.2:1 as text on the dark page, and no single teal can do both: a colour light enough
 * to be read on the page is too light to carry white text. So the fill keeps antd's derived
 * colour and every place antd draws the brand colour as text is pointed at these instead. They
 * are also published as antd's `colorPrimaryText` family, which no antd component reads, so that
 * the stylesheet can reach them as `--ant-color-primary-text*` where no component token exists.
 */
export const DARK_PRIMARY_TEXT = { base: '#5cbdbd', hover: '#7fd0d0', active: '#3fa8a8' } as const;

const darkContrast: ThemeConfig = {
  token: {
    colorPrimaryBorder: '#3fa8a8',
    colorPrimaryText: DARK_PRIMARY_TEXT.base,
    colorPrimaryTextHover: DARK_PRIMARY_TEXT.hover,
    colorPrimaryTextActive: DARK_PRIMARY_TEXT.active,
    colorLink: '#69b1ff',
    colorTextTertiary: 'rgba(255, 255, 255, 0.5)',
    colorTextPlaceholder: 'rgba(255, 255, 255, 0.5)',
    colorError: '#f04040',
    colorErrorText: '#ff7875',
    colorInfoText: '#69b1ff',
    colorBorder: '#6b6b6b',
  },
  components: {
    // The places antd draws the brand colour as text rather than as a fill. Where a component
    // has a token for that text it is set; where it reads the brand colour itself (a checked
    // radio button, the steps' icons, the active page's border) the brand colour is replaced for
    // that component alone, which leaves the fills that read the derived component tokens — the
    // radio dot, the primary button — on antd's own darker teal.
    Menu: { itemSelectedColor: DARK_PRIMARY_TEXT.base },
    Tabs: {
      itemSelectedColor: DARK_PRIMARY_TEXT.base,
      itemActiveColor: DARK_PRIMARY_TEXT.base,
      inkBarColor: DARK_PRIMARY_TEXT.base,
    },
    Pagination: {
      itemActiveColor: DARK_PRIMARY_TEXT.base,
      itemActiveColorHover: DARK_PRIMARY_TEXT.hover,
      colorPrimary: DARK_PRIMARY_TEXT.base,
      colorPrimaryHover: DARK_PRIMARY_TEXT.hover,
    },
    Radio: {
      colorPrimary: DARK_PRIMARY_TEXT.base,
      colorPrimaryHover: DARK_PRIMARY_TEXT.hover,
      colorPrimaryActive: DARK_PRIMARY_TEXT.active,
    },
    // The current step is a circle of the brand colour with its number on it; once that circle
    // is the light teal, the number has to be dark to be read.
    Steps: {
      colorPrimary: DARK_PRIMARY_TEXT.base,
      colorPrimaryHover: DARK_PRIMARY_TEXT.hover,
      colorTextLightSolid: '#141414',
    },
    // A default button's text turns brand-coloured under the pointer. The outlined and text-only
    // primary buttons cannot be reached from here — one token feeds both their text and the
    // solid primary button's fill — and are re-pointed in the stylesheet instead.
    Tag: darkTagText,
    Button: {
      defaultHoverColor: DARK_PRIMARY_TEXT.hover,
      defaultHoverBorderColor: DARK_PRIMARY_TEXT.hover,
      defaultActiveColor: DARK_PRIMARY_TEXT.active,
      defaultActiveBorderColor: DARK_PRIMARY_TEXT.active,
    },
  },
};


/**
 * How the stack of records inside one day of a month grid is allowed to grow — an override of the
 * calendar's own shipped sizing, kept here with the rest of the theming rather than in the page
 * that draws the grid.
 *
 * **The default hides records without saying so, and that is the whole reason this exists.** The
 * calendar component sizes a day cell's content at exactly three rows and scrolls the rest inside
 * itself, so a Saturday carrying four trips shows three of them and puts the fourth behind a
 * scrollbar that is a few pixels wide and inside a table cell. A month grid whose promise is "the
 * club's month at a glance" cannot hide the fourth thing happening on a day; a reader scanning for
 * a clash would never learn there was one. So the height becomes the content's own — the cell
 * grows to hold what is in it, and the row of cells beside it grows with it — with a floor so that
 * a month of empty days still reads as a grid rather than as ragged strips.
 *
 * **Why it is written as a style and not as a theme token.** The component computes that
 * three-row height itself, last, from its own font and margin tokens, and merges it over anything
 * supplied from outside — so a value handed to the theme provider under this name is both
 * rejected by the component's public token type and, if forced past it, overwritten before any
 * style is generated. The component does, however, take a style for that exact element as part of
 * its public interface, and applies it to the element it would otherwise have sized itself. That
 * is the route the grid takes: no markup of the component's is reproduced anywhere, only this
 * height is handed to it.
 *
 * The floor is three rows at the default text size, matching what the grid used to reserve, so
 * turning the clip off changes what a busy day shows and not what an empty month looks like.
 */
export const calendarDayContentStyle: CSSProperties = {
  height: 'auto',
  minHeight: 86,
  maxHeight: 'none',
  overflowY: 'visible',
};

/**
 * How tall the calendar's list of records may grow before it scrolls inside itself, for each of
 * the two ways its rows are drawn.
 *
 * The list opens with the line marking today a few rows down it, so that the last things that
 * happened and the next things coming are on screen together. That takes a body of its own: it
 * is the body that is moved to today, never the page, and a body with no height of its own is
 * just part of the page. The height is what holds about five rows either side of the line, which
 * is the view the list is asked for — a dozen rows of cells, or eleven of the taller rows that
 * each hold a whole entry. A list shorter than this is simply as tall as its rows.
 */
export const calendarListBodyHeight: Record<'record' | 'agenda', number> = {
  record: 564,
  agenda: 780,
};

/** Whether the operating system is currently asking for a dark interface. */
export function prefersDark(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function resolveDark(preference: Appearance['theme']): boolean {
  return preference === 'system' ? prefersDark() : preference === 'dark';
}

/**
 * The token set for a given appearance choice, rebuilt on every render of the root from the
 * locally mirrored preference — so switching theme or density takes effect as it is picked
 * rather than after a round trip.
 *
 * All three knobs are antd's own: the light/dark algorithms, the compact algorithm, and the
 * motion token that turns component animation off.
 */
export function buildThemeConfig(appearance: Appearance): ThemeConfig {
  const dark = resolveDark(appearance.theme);
  const contrast = dark ? darkContrast : lightContrast;
  return {
    ...themeConfig,
    algorithm: [
      dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
      ...(appearance.density === 'compact' ? [theme.compactAlgorithm] : []),
    ],
    token: { ...themeConfig.token, ...contrast.token, motion: !appearance.reduceMotion },
    components: { ...themeConfig.components, ...contrast.components },
  };
}
