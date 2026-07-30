// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme, type ThemeConfig } from 'antd';
import type { Appearance } from './stores/uiPrefsStore.ts';

// All theming through antd tokens — no scattered inline styles.
export const themeConfig: ThemeConfig = {
  token: {
    colorPrimary: '#146262',
    borderRadius: 4,
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
