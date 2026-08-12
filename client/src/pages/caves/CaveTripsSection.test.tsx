// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo, TripLogListParams } from '../../api/hooks.ts';

let page: { items: TripLogInfo[]; page: number; pageSize: number; totalItems: number };
let asked: TripLogListParams | null = null;

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: (params: TripLogListParams) => {
    asked = params;
    return { data: page, isFetching: false };
  },
  useTripTypes: () => ({
    data: [{ id: 1, code: 'survey', name: 'Survey / mapping', isSeeded: true }],
  }),
}));

const { default: CaveTripsSection } = await import('./CaveTripsSection.tsx');

function trip(
  id: string,
  title: string,
  participants: TripLogInfo['participants'] = [],
): TripLogInfo {
  return {
    id,
    title,
    tripDate: '2026-05-04',
    tripDateEnd: null,
    state: 'published',
    tripTypeId: 1,
    participants,
  } as unknown as TripLogInfo;
}

function person(caverId: string, roleId: number): TripLogInfo['participants'][number] {
  return { caverId, roleId, name: caverId } as unknown as TripLogInfo['participants'][number];
}

function show() {
  return render(
    <MemoryRouter>
      <CaveTripsSection caveId="cave-1" />
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  asked = null;
});

describe('CaveTripsSection', () => {
  it('asks the trip list for this cave only, and shows what comes back', () => {
    page = { items: [trip('a', 'Survey push'), trip('b', 'Rebolting')], page: 1, pageSize: 10, totalItems: 2 };
    show();

    expect(asked).toMatchObject({ caveId: 'cave-1', page: 1 });
    expect(screen.getByText('Survey push')).toBeTruthy();
    expect(screen.getByText('Rebolting')).toBeTruthy();
  });

  /**
   * The server answers a caller who may read the cave but not place it with an empty page,
   * because listing a cave's trips would place it through their sketches. That arrives here as
   * an ordinary empty result and must render as one — a section that guessed at the reason and
   * said something about protection would be announcing, to the one caller it is kept from,
   * that there is something to keep.
   */
  it('renders an empty page as an ordinary empty list, saying nothing about why', () => {
    page = { items: [], page: 1, pageSize: 10, totalItems: 0 };
    show();

    expect(screen.getByText('No trip logs name this cave.')).toBeTruthy();
    expect(screen.queryByText('Survey push')).toBeNull();
  });

  /**
   * A roster row is a person and the job they did, so the leader who also surveyed is two rows.
   * The column says how many people were underground, and two of them were.
   */
  it('counts a person holding two jobs once', () => {
    page = {
      items: [trip('a', 'Survey push', [person('ana', 1), person('ana', 3), person('bogdan', 1)])],
      page: 1,
      pageSize: 10,
      totalItems: 1,
    };
    show();

    expect(screen.getByText('2')).toBeTruthy();
    expect(screen.queryByText('3')).toBeNull();
  });
});
