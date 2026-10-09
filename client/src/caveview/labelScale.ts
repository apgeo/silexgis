// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How large a model's own writing is drawn — the names of its stations and the comments on them —
 * as a multiple of the size the viewer gives them.
 *
 * A survey names every station by its whole path, and where stations are close together the
 * names at the viewer's size cover the passages they name. How small is still readable depends on
 * the survey, the screen and the eyes in front of it, so it is the reader's to choose, and what
 * they chose is kept in their browser for every model they open.
 */

/** The sizes offered, smallest first. 1 is the viewer's own. */
export const MODEL_LABEL_SCALES = [0.4, 0.6, 0.8, 1, 1.25, 1.5] as const;

export const DEFAULT_MODEL_LABEL_SCALE = 1;

/**
 * The scale to draw at for whatever was stored: one of the sizes offered, or the viewer's own.
 *
 * Storage is outside the application's control — another version, a hand in the developer tools —
 * and the viewer refuses anything that is not a number above zero with a warning on every model
 * opened. A stored value that is not one of the sizes offered is therefore not passed on.
 */
export function modelLabelScale(stored: unknown): number {
  return typeof stored === 'number' && (MODEL_LABEL_SCALES as readonly number[]).includes(stored)
    ? stored
    : DEFAULT_MODEL_LABEL_SCALE;
}
