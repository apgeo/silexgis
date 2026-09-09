// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Which rail destination a path stands under.
 *
 * Kept out of the shell so it can be checked against the rail itself: the list below and the
 * destinations {@link buildNavItems} offers are two halves of one arrangement, and a page added
 * to one and not the other still navigates — only the highlight is wrong, which is the kind of
 * mistake nobody reports and everybody sees.
 */
// "settings" and "notifications" are listed so an unmatched path does not fall through to
// highlighting the map; neither matches a menu item, so nothing lights up while one is open,
// which is deliberate — neither is a sidebar destination. Every other entry here is one,
// including a camp: the list is a destination and a camp's own page stays under it, so opening
// one keeps the camps item lit.
export const NAV_SECTIONS = [
  'map3d', 'dashboard', 'work-areas', 'caves', 'features', 'geodata', 'catalogue/speologie',
  'gallery', 'albums', 'photo-library', 'cabinets',
  'uploads', 'documents', 'calendar', 'events',
  // The three registry explorers. Each is its own rail destination, so each is listed: a bare
  // 'statistics' prefix would resolve all three to a key no menu item carries, and nothing would
  // light up at all.
  'statistics/distribution', 'statistics/correlation', 'statistics/regions',
  // Before the trip list, because the list's own prefix matches this path too and the first
  // match is the one taken. Behind it, the reviewer reading a spreadsheet is shown the rail
  // highlighting the trip list — a destination they are not on.
  'trip-logs/import', 'trip-logs', 'expeditions', 'checklists',
  'caving-groups', 'cavers',
  'admin/audit', 'admin/notification-health', 'admin/messaging', 'admin/message-templates',
  'admin/permission-groups',
  'admin/feature-sets', 'admin/document-types', 'admin/relation-types', 'admin/term-rules',
  'admin/terrain',
  // The three the rail offers under configuration. Missing here, they matched nothing and
  // fell through to the map, so opening trip purposes lit the map item instead.
  'admin/trip-types', 'admin/participant-roles', 'admin/report-templates',
  'settings', 'notifications',
] as const;

/**
 * Which rail destination a path stands under, or the map when it stands under none.
 *
 * The one home for that decision, so the list above can be checked against the rail itself rather
 * than against a handful of remembered paths: a destination the list does not recognise still
 * navigates, and lights the map item while the reader is plainly somewhere else.
 */
export function sectionFor(pathname: string): string {
  return NAV_SECTIONS.find((s) => pathname.startsWith(`/${s}`)) ?? 'map';
}
