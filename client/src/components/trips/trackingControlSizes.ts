// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';

/**
 * What the two lists a finger has to land in are worth under one.
 *
 * Both are drawn in a portal at the end of the document, out of reach of any selector the card or
 * the dialog that opens them could write, and both size themselves from tokens rather than from the
 * `size` given to the control that opens them — so a chooser grown to forty pixels still opens onto
 * a calendar of twenty-four-pixel days and a list of thirty-two-pixel options. Given as tokens
 * rather than as heights pushed into a stylesheet because every other measurement of those panels
 * is derived from these by the component library: where a cell's text sits in it, how wide a month
 * comes out, how tall the column of hours has to be to hold twenty-four of them.
 *
 * `timeColumnWidth` is the one that is *not* grown. The hours, minutes and seconds stand side by
 * side, and widening them is what pushes the panel off a phone; their height is what a finger
 * misses, and that is `timeCellHeight`.
 *
 * Shared by every surface that records a report, because the calendar behind "when it was said" is
 * one control drawn in two places and a second set of numbers for it would be a second thing to
 * measure on a phone.
 */
export const COARSE_SELECT = { optionHeight: 40 };
export const COARSE_DATE_PICKER = {
  cellHeight: 40,
  cellWidth: 40,
  withoutTimeCellHeight: 48,
  timeCellHeight: 40,
};

/**
 * The theme the portalled panels of a reporting surface are built from.
 *
 * Held still across renders: a fresh object is a fresh theme, and each one has the whole calendar's
 * and the whole list's styles derived again.
 */
export function useTrackingPanelTheme(coarse: boolean) {
  return useMemo(
    () => ({
      components: coarse
        ? { Select: COARSE_SELECT, DatePicker: COARSE_DATE_PICKER }
        : { Select: {}, DatePicker: {} },
    }),
    [coarse],
  );
}
