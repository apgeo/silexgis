// SPDX-License-Identifier: AGPL-3.0-or-later
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
  return {
    ...themeConfig,
    algorithm: [
      dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
      ...(appearance.density === 'compact' ? [theme.compactAlgorithm] : []),
    ],
    token: { ...themeConfig.token, motion: !appearance.reduceMotion },
  };
}
