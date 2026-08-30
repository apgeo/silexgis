// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The sections the selection panel can show, and the order they start in.
 *
 * The ids are a stored contract. They are written into every user's preferences and into every
 * saved layout, so one is chosen once and never renamed — a rename is a silent reset of everybody's
 * arrangement, which looks like the feature losing their settings rather than like a rename.
 *
 * The list is the whole vocabulary, including sections the panel does not render yet. Knowing an id
 * exists costs nothing and means adding the section later is a line here rather than a migration of
 * everyone's stored order.
 */
export const PANEL_SECTION_IDS = [
  'details',
  'tags',
  'attachments',
  'links',
  'text',
  'history',
  'permissions',
] as const;

export type PanelSectionId = (typeof PANEL_SECTION_IDS)[number];

/**
 * The order a panel starts in when nothing has said otherwise: what the thing *is* first, then what
 * has been filed against it, then what it is connected to, then what happened to it.
 */
export const DEFAULT_SECTION_ORDER: PanelSectionId[] = [
  'details',
  'tags',
  'attachments',
  'links',
  'text',
  'history',
];

/**
 * Sections that start closed.
 *
 * History is the explicit reason this whole arrangement exists: it is long, it is rarely read, and
 * left open it pushes the fields people do read below the fold. Attachments start closed too —
 * a gallery is tall, and with lazy loading a closed section costs nothing at all.
 */
export const DEFAULT_COLLAPSED: PanelSectionId[] = ['history', 'attachments', 'text'];

/**
 * Sections not shown until somebody turns them on. Permissions is a modal everywhere else in the
 * application and stays one here; the id exists so the ordering list is complete.
 */
export const DEFAULT_HIDDEN: PanelSectionId[] = ['permissions'];

export function isPanelSectionId(value: string): value is PanelSectionId {
  return (PANEL_SECTION_IDS as readonly string[]).includes(value);
}

/**
 * A stored order made usable: unknown ids dropped, known ids that the stored order never mentioned
 * appended in their default position.
 *
 * Both halves matter and neither is defensive noise. An id disappears when a section is retired, and
 * a stored order still naming it would leave a gap somebody cannot fill; an id appears when a
 * section is added, and a stored order that predates it would hide the new section from every
 * existing user — who would report it as not shipped.
 */
export function resolveSectionOrder(stored: readonly string[] | undefined): PanelSectionId[] {
  const known = (stored ?? []).filter(isPanelSectionId);
  const seen = new Set(known);
  const merged = [...known];
  for (const id of DEFAULT_SECTION_ORDER) {
    if (!seen.has(id)) {
      // Appended rather than inserted at its default index: a person who dragged history to the
      // top meant it, and quietly slipping a new section above it would undo that.
      merged.push(id);
    }
  }

  return merged;
}
