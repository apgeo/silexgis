// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { afterReportsRemoved, caverHeldByTrips } from './caverHeldByTrips.ts';

const trip = (overrides: Record<string, unknown> = {}) => ({
  id: 'trip-1',
  title: 'Autumn trip',
  tripDate: '2026-09-12',
  onRoster: false,
  reports: 3,
  reportsRemovable: true,
  ...overrides,
});

const refusal = (members: Record<string, unknown>, code = 'caver.referenced_by_trips') =>
  new ApiError(400, code, 'held', { code, ...members });

describe('caverHeldByTrips', () => {
  it('reads the trips and the yes-or-no off the refusal', () => {
    const held = caverHeldByTrips(
      refusal({ trips: [trip(), trip({ id: 'trip-2', onRoster: true, reports: 0, reportsRemovable: false })], heldElsewhere: false }),
    );

    expect(held).toEqual({
      trips: [
        { id: 'trip-1', title: 'Autumn trip', tripDate: '2026-09-12', onRoster: false, reports: 3, reportsRemovable: true },
        { id: 'trip-2', title: 'Autumn trip', tripDate: '2026-09-12', onRoster: true, reports: 0, reportsRemovable: false },
      ],
      heldElsewhere: false,
    });
  });

  it('is not that refusal for another code, or for something that is not a refusal at all', () => {
    expect(caverHeldByTrips(refusal({ trips: [trip()] }, 'caver.referenced_by_expeditions'))).toBeNull();
    expect(caverHeldByTrips(new Error('offline'))).toBeNull();
    expect(caverHeldByTrips(undefined)).toBeNull();
  });

  it('counts what it cannot read as something else holding the person, never as nothing', () => {
    // A row without its shape, and a refusal that names no list at all: in neither case may the
    // dialog go on to say that nothing is left.
    expect(caverHeldByTrips(refusal({ trips: [trip(), { id: 7 }], heldElsewhere: false }))).toEqual({
      trips: [expect.objectContaining({ id: 'trip-1' })],
      heldElsewhere: true,
    });
    expect(caverHeldByTrips(refusal({}))).toEqual({ trips: [], heldElsewhere: true });
    expect(caverHeldByTrips(refusal({ trips: [], heldElsewhere: true }))).toEqual({
      trips: [],
      heldElsewhere: true,
    });
  });

  it('never offers a removal the server did not say is allowed', () => {
    const held = caverHeldByTrips(refusal({ trips: [trip({ reportsRemovable: 'yes', reports: -2 })] }));

    expect(held?.trips[0]).toMatchObject({ reports: 0, reportsRemovable: false });
  });
});

describe('afterReportsRemoved', () => {
  const held = {
    trips: [
      { id: 'a', title: 'A', tripDate: '2026-09-12', onRoster: false, reports: 2, reportsRemovable: true },
      { id: 'b', title: 'B', tripDate: '2026-09-13', onRoster: true, reports: 4, reportsRemovable: true },
    ],
    heldElsewhere: true,
  };

  it('drops a trip that held the person only by their reports', () => {
    expect(afterReportsRemoved(held, 'a').trips.map((row) => row.id)).toEqual(['b']);
  });

  it('keeps a trip whose roster still names them, with nothing left to remove', () => {
    const after = afterReportsRemoved(held, 'b');

    expect(after.trips.find((row) => row.id === 'b')).toMatchObject({
      onRoster: true,
      reports: 0,
      reportsRemovable: false,
    });
    // Somebody else's trip is untouched, and so is what the list could not show.
    expect(after.trips.find((row) => row.id === 'a')).toMatchObject({ reports: 2, reportsRemovable: true });
    expect(after.heldElsewhere).toBe(true);
  });
});
