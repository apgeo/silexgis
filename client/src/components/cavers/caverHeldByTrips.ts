// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/** The refusal a person's delete gets while trips hold them. */
export const CAVER_HELD_BY_TRIPS_CODE = 'caver.referenced_by_trips';

/** The refusal a person's delete gets while a camp's roster holds them. */
export const CAVER_HELD_BY_CAMP_CODE = 'caver.referenced_by_expeditions';

/** One trip that holds a person, as the refusal names it — always a trip this reader may open. */
export interface CaverHeldByTrip {
  id: string;
  title: string;
  /** The trip's first day, `YYYY-MM-DD`. */
  tripDate: string;
  /** The trip's roster names the person; that is undone by editing the trip. */
  onRoster: boolean;
  /** How many reports of the trip's tracking log are about the person, as far as this reader is told. */
  reports: number;
  /** Whether this reader may remove those reports from here. */
  reportsRemovable: boolean;
}

export interface CaverHeldByTrips {
  trips: CaverHeldByTrip[];
  /**
   * Something this list does not show also holds the person — a trip this reader may not open, a
   * deleted trip, more trips than a refusal names. Deliberately a yes or no and never a number.
   */
  heldElsewhere: boolean;
}

function tripOf(value: unknown): CaverHeldByTrip | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const row = value as Record<string, unknown>;
  if (typeof row.id !== 'string' || typeof row.title !== 'string' || typeof row.tripDate !== 'string') {
    return null;
  }
  return {
    id: row.id,
    title: row.title,
    tripDate: row.tripDate,
    onRoster: row.onRoster === true,
    reports: typeof row.reports === 'number' && row.reports > 0 ? row.reports : 0,
    reportsRemovable: row.reportsRemovable === true,
  };
}

/**
 * What a refused delete says holds the person, or null when the error is not that refusal.
 *
 * The members are the server's and arrive untyped — a refusal's own members are not part of the
 * generated contract — so each is narrowed here, once. A row that does not have the shape is left
 * out and counted as "something else holds them": dropping it silently would let the dialog say
 * "nothing left" while the server still refuses.
 */
export function caverHeldByTrips(error: unknown): CaverHeldByTrips | null {
  if (!(error instanceof ApiError) || error.code !== CAVER_HELD_BY_TRIPS_CODE) {
    return null;
  }
  const listed = error.problem?.trips;
  const rows = Array.isArray(listed) ? listed : [];
  const trips = rows.map(tripOf).filter((trip): trip is CaverHeldByTrip => trip !== null);
  return {
    trips,
    heldElsewhere:
      error.problem?.heldElsewhere === true || trips.length < rows.length || !Array.isArray(listed),
  };
}

/** The list after one trip's reports about the person have been removed. */
export function afterReportsRemoved(held: CaverHeldByTrips, tripLogId: string): CaverHeldByTrips {
  return {
    ...held,
    trips: held.trips
      .map((trip) =>
        trip.id === tripLogId ? { ...trip, reports: 0, reportsRemovable: false } : trip,
      )
      // A trip that held the person only by their reports no longer holds them at all.
      .filter((trip) => trip.onRoster || trip.reports > 0),
  };
}
