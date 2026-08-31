// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The settings sections, in nav order. One list so the desktop nav, the phone picker and the
 * tests cannot drift apart. Labels are not here — they come from `settings.nav.<key>`.
 */
export const SETTINGS_SECTIONS = [
  'profile',
  'account',
  'emails',
  'notifications',
  'security',
  'accessibility',
  'sync',
] as const;

export type SettingsSection = (typeof SETTINGS_SECTIONS)[number];

export const DEFAULT_SETTINGS_SECTION: SettingsSection = 'profile';

/** The section a `/settings/...` path is showing, falling back to the first one. */
export function sectionFromPath(pathname: string): SettingsSection {
  const candidate = pathname.split('/')[2];
  return SETTINGS_SECTIONS.includes(candidate as SettingsSection)
    ? (candidate as SettingsSection)
    : DEFAULT_SETTINGS_SECTION;
}
