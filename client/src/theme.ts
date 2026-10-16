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
};

const darkContrast: ThemeConfig = {
  token: {
    colorPrimaryBorder: '#3fa8a8',
    colorLink: '#69b1ff',
    colorTextTertiary: 'rgba(255, 255, 255, 0.5)',
    colorTextPlaceholder: 'rgba(255, 255, 255, 0.5)',
    colorError: '#f04040',
    colorErrorText: '#ff7875',
    colorInfoText: '#69b1ff',
    colorBorder: '#6b6b6b',
  },
  components: {
    // The brand colour as text is too dark to read on the dark page; these are the places antd
    // draws it as text rather than as a fill.
    Menu: { itemSelectedColor: '#5cbdbd' },
    Tabs: { itemSelectedColor: '#5cbdbd', itemActiveColor: '#5cbdbd', inkBarColor: '#5cbdbd' },
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
