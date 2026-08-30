// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * What one view is asked to show, and how a view says what it is.
 *
 * These two shapes are the whole vocabulary between the reader that follows a hyperlink and the
 * map, scene or model viewer that answers it. They live in a file of their own, below both, so
 * that neither has to import the other: the reader must not know what a map is, and a map must
 * not know that annotated text exists — the next thing to send a reveal is a PDF with links
 * drawn on it, and the map should not have to be edited for that to work.
 */

/**
 * A resource, or a part of one — deliberately the same vocabulary a resource-link member uses.
 *
 * A navigation event *is* a link member: the same target type, the same identifier, the same
 * anchor. Inventing a second vocabulary for "the thing to navigate to" would mean translating
 * between them at every boundary, and the translation is where a survey station becomes a cave
 * and stops selecting anything.
 */
export interface ResourceRef {
  /** Wire spelling of the target's world: 'feature', 'document', 'surveyModel', 'geofile', … */
  targetType: string;
  targetId: string;
  /** Which part of it, in the anchor vocabulary; 'whole' when the whole thing is meant. */
  anchorKind?: string;
  /** The anchor's payload, as authored. Free-form: a view reads what it understands. */
  anchor?: unknown;
  /** What to call it in a message when a view cannot show it. Never used to decide anything. */
  label?: string;
}

/** The kinds of view that can be asked to show something. Append only — a stored preference
 * naming a control is keyed on this. */
export const VIEW_CONTROL_KINDS = ['map2d', 'scene3d', 'caveview', 'imageView'] as const;

export type ViewControlKind = (typeof VIEW_CONTROL_KINDS)[number];

/**
 * One view control as it appears in a roster — including a roster gathered across windows, which
 * is why there is no function on it and why the label is a translation key rather than a string.
 *
 * A pop-out window is a separate realm with its own copy of every module and, in principle, its
 * own reader language; a label resolved in the window that owns the control and shipped as text
 * would be rendered in that window's language inside a menu drawn in another's.
 */
export interface ViewControlDescriptor {
  /**
   * Globally unique address: the owning window's id and the control's own, joined. A control is
   * addressed rather than merely counted, because "show it here and nowhere else" names one.
   */
  address: string;
  kind: ViewControlKind;
  /** i18n key naming this control — 'viewLinks.controls.map2d' and friends. */
  labelKey: string;
  /** True for a control in the window doing the asking. Filled in by the reader, not sent. */
  local?: boolean;
}
