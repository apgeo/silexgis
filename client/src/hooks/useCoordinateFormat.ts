// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback } from 'react';
import { formatApproximateCoordinate, formatCoordinate, formatCoordinateForCopy } from '../geo/coordinateFormat.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

/**
 * Writes a position as text, in the format chosen. The precision is the decimal-degree places to
 * print, or `'approximate'` for a position the server snapped to its location-protection grid,
 * which is then written no finer than that grid.
 */
export type CoordinateFormatter = (lon: number, lat: number, precision?: number | 'approximate') => string;

/**
 * Positions written the way the signed-in person chose to read them — every display of a
 * coordinate, and every copy of one to the clipboard, goes through this, so that a choice made in
 * the settings holds everywhere a position appears.
 */
export function useCoordinateFormatter(): CoordinateFormatter {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  return useCallback(
    (lon, lat, precision) =>
      precision === 'approximate'
        ? formatApproximateCoordinate(lon, lat, format)
        : formatCoordinate(lon, lat, format, precision),
    [format],
  );
}

/** A position as it goes on the clipboard, in the format chosen. */
export function useCoordinateCopyFormatter(): (lon: number, lat: number) => string {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  return useCallback((lon, lat) => formatCoordinateForCopy(lon, lat, format), [format]);
}

/** The same, outside a component: for a module that builds text once, on demand. */
export function formatCoordinateNow(lon: number, lat: number, decimals?: number): string {
  return formatCoordinate(lon, lat, useUiPrefsStore.getState().coordinateFormat, decimals);
}
