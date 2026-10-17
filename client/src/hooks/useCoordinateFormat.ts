// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback } from 'react';
import { formatApproximateCoordinate, formatCoordinate, formatCoordinateForCopy } from '../geo/coordinateFormat.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

/**
 * Writes a position as text, in the format chosen and with the person's settings for it.
 * `'approximate'` marks a position the server snapped to its location-protection grid, which is
 * then written no finer than that grid.
 */
export type CoordinateFormatter = (lon: number, lat: number, precision?: 'approximate') => string;

/**
 * Positions written the way the signed-in person chose to read them — every display of a
 * coordinate, and every copy of one to the clipboard, goes through this, so that a choice made in
 * the settings holds everywhere a position appears.
 */
export function useCoordinateFormatter(): CoordinateFormatter {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  return useCallback(
    (lon, lat, precision) =>
      precision === 'approximate'
        ? formatApproximateCoordinate(lon, lat, format, settings)
        : formatCoordinate(lon, lat, format, settings),
    [format, settings],
  );
}

/**
 * A position as it goes on the clipboard, in the format chosen — a protected one exactly as coarse
 * as it is shown, never finer.
 */
export function useCoordinateCopyFormatter(): CoordinateFormatter {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  return useCallback(
    (lon, lat, precision) => formatCoordinateForCopy(lon, lat, format, settings, precision === 'approximate'),
    [format, settings],
  );
}
