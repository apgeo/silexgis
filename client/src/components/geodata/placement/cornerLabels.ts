// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CornerId } from '../../../geodata/placement/placement.ts';

/** What each corner of the sheet is called on screen. */
export const CORNER_LABEL_KEYS: Readonly<Record<CornerId, string>> = {
  nw: 'geodata.placement.corners.nw',
  ne: 'geodata.placement.corners.ne',
  se: 'geodata.placement.corners.se',
  sw: 'geodata.placement.corners.sw',
};
