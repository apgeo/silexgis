// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackedCaver } from '../caveview/trackedCavers.ts';
import { toMapCoordinate, type ImageSize } from './coordinates.ts';
import type { MapStationMarker } from './mapPoints.ts';

/**
 * Where a watch's party stands on one scanned map sheet — the single home for the rule
 * that decides it.
 *
 * <b>A caver reaches a sheet through a station pin and through no other path.</b> The
 * owner's rule (2026-09-22): a caver whose reported station has a point defined on this
 * map is drawn at that point; a caver whose station has none is absent with a message,
 * never guessed. No nearest-pin snapping, no centroid, no interpolation — a pin is a
 * calibration-grade claim somebody authored, and a marker invented anywhere else would
 * be this application claiming a measurement nobody made.
 *
 * Everything here is arithmetic over plain objects, exactly like the folds beside it: a
 * test drives the whole placement contract without a map, a viewer or a network.
 */

/** One person drawn on the sheet: their record, and the pin that places them. */
export interface SheetCaverMarker {
  caver: TrackedCaver;
  /** The pinned station that placed them — always the caver's own reported station. */
  station: string;
  /** The pin's stored point, in fractions of the drawn picture (top-left origin). */
  x: number;
  y: number;
  /**
   * Pixel offset from the pin, y-up, fanning a shared pin out so each person's dot is
   * separately visible and separately pressable. `[0, 0]` for a caver standing alone.
   * Pixels rather than map units on purpose: the fan must not grow as the sheet zooms,
   * and a finger-driven screen needs the same spread at every zoom.
   */
  offsetPx: readonly [number, number];
}

/**
 * The offsets a party sharing one pin is fanned out by: one person sits on the pin's own
 * point, more stand on a ring around it, starting at the top. The ring never encodes
 * position — every dot's geometry stays the pin — it only keeps dots and their tap
 * targets from burying each other.
 */
export function fanOffsets(count: number, radiusPx: number): readonly [number, number][] {
  if (count <= 1) {
    return [[0, 0]];
  }
  return Array.from({ length: count }, (_, i) => {
    const angle = Math.PI / 2 - (i * 2 * Math.PI) / count;
    return [radiusPx * Math.cos(angle), radiusPx * Math.sin(angle)] as [number, number];
  });
}

/**
 * The party as this sheet can draw it: everybody whose reported station has a point on
 * the rendering on screen, at that point, in the watch's own order within a shared pin.
 *
 * Only `position.kind === 'station'` records can reach a pin — a withheld position names
 * no station to look up, a depth was never asked of any drawing, and both stay listed
 * rather than drawn, exactly as the 3D scene treats them.
 */
export function placedSheetCavers(
  cavers: readonly TrackedCaver[],
  markers: readonly MapStationMarker[],
  fanRadiusPx: number,
): SheetCaverMarker[] {
  const pinByStation = new Map(markers.map((marker) => [marker.station, marker]));

  const groups = new Map<string, TrackedCaver[]>();
  for (const caver of cavers) {
    if (caver.position.kind !== 'station' || !pinByStation.has(caver.position.station)) {
      continue;
    }
    const station = caver.position.station;
    const held = groups.get(station);
    if (held === undefined) {
      groups.set(station, [caver]);
    } else {
      held.push(caver);
    }
  }

  const placed: SheetCaverMarker[] = [];
  for (const [station, members] of groups) {
    const pin = pinByStation.get(station)!;
    const offsets = fanOffsets(members.length, fanRadiusPx);
    members.forEach((caver, i) => {
      placed.push({ caver, station, x: pin.x, y: pin.y, offsetPx: offsets[i] });
    });
  }
  return placed;
}

/**
 * The reported stations this sheet holds no point for — the set the overlay's
 * "no point on this map" wording is driven by.
 *
 * Stations rather than people, for the same reason the 3D panel's unplaced answer is
 * stations: the replay swaps the whole party under the panel while the table above goes
 * on naming the live one, and a station name means the same thing at every moment of the
 * trip. Only reported stations are asked about — a withheld or unreported position names
 * no station, and its own wording already answers for it.
 */
export function stationsWithoutPoint(
  cavers: readonly TrackedCaver[],
  markers: readonly MapStationMarker[],
): ReadonlySet<string> {
  const pinned = new Set(markers.map((marker) => marker.station));
  const missing = new Set<string>();
  for (const caver of cavers) {
    if (caver.position.kind === 'station' && !pinned.has(caver.position.station)) {
      missing.add(caver.position.station);
    }
  }
  return missing;
}

/**
 * The caver dot a click landed on, within the pointer's own tolerance — displacement
 * aware, which is what the station-pin hit test next door does not need to be: a fanned
 * dot is drawn a fixed pixel offset from its pin, so where it *is* on screen depends on
 * the resolution the sheet is being read at.
 *
 * The nearest dot inside the reach wins, so a press between two fanned dots answers the
 * one it was closest to rather than whichever the list happened to order first.
 */
export function sheetCaverHit(
  placed: readonly SheetCaverMarker[],
  size: ImageSize,
  coordinate: readonly number[],
  resolution: number,
  tolerancePx: number,
): SheetCaverMarker | null {
  const reach = tolerancePx * resolution;
  let best: SheetCaverMarker | null = null;
  let bestDistance = Infinity;
  for (const marker of placed) {
    const [px, py] = toMapCoordinate(marker, size);
    // The extent's y axis points up and the offsets are y-up too, so both add directly.
    const cx = px + marker.offsetPx[0] * resolution;
    const cy = py + marker.offsetPx[1] * resolution;
    const distance = Math.hypot(coordinate[0] - cx, coordinate[1] - cy);
    if (distance <= reach && distance < bestDistance) {
      best = marker;
      bestDistance = distance;
    }
  }
  return best;
}
