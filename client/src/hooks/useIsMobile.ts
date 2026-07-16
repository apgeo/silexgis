// SPDX-License-Identifier: AGPL-3.0-or-later
import { Grid } from 'antd';

/**
 * True on phone-width viewports — below 768px, antd's `md` — where the map workspace
 * swaps its resizable docks for drawers and the edit toolbar for a scrolling strip.
 *
 * Width only. Touch *capability* is a separate axis and is not this hook's business: a
 * phone held in landscape is >= 768px yet still needs finger-sized hit tolerances, so
 * those are selected from `(hover: none), (pointer: coarse)` instead.
 */
export function useIsMobile(): boolean {
  const screens = Grid.useBreakpoint();
  // `md` is undefined on the first render, before the responsive observer has reported;
  // treating "not known to be >= md" as mobile would flash the phone layout on desktop,
  // so only an explicit false counts.
  return screens.md === false;
}
