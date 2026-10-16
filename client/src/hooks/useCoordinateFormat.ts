// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback } from 'react';
import { formatCoordinate, formatCoordinateForCopy } from '../geo/coordinateFormat.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

/** Writes a position as text, in the format chosen; `decimals` is the decimal-degree precision. */
export type CoordinateFormatter = (lon: number, lat: number, decimals?: number) => string;

/**
 * Positions written the way the signed-in person chose to read them — every display of a
 * coordinate, and every copy of one to the clipboard, goes through this, so that a choice made in
 * the settings holds everywhere a position appears.
 */
export function useCoordinateFormatter(): CoordinateFormatter {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  return useCallback((lon, lat, decimals) => formatCoordinate(lon, lat, format, decimals), [format]);
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
