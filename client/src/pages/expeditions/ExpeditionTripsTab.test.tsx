// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo } from '../../api/hooks.ts';

const { tripsSpy } = vi.hoisted(() => ({ tripsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: (...args: unknown[]) => tripsSpy(...args),
}));

// The roll-up is a component of its own with its own tests; what matters here is that the tab
// mounts it against the camp, so the totals and the sentence explaining them arrive together.
vi.mock('../../components/statistics/TripStatisticsPanel.tsx', () => ({
  default: ({ subject, id }: { subject: string; id: string }) => (
    <div data-testid="rollup">{`${subject}:${id}`}</div>
  ),
}));

const { default: ExpeditionTripsTab } = await import('./ExpeditionTripsTab.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

function trip(): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Down the shaft',
    tripDate: '2026-07-19',
    tripDateEnd: null,
    state: 'published',
  } as unknown as TripLogInfo;
}

function show() {
  return render(
    <MemoryRouter>
      <ExpeditionTripsTab expeditionId={CAMP} />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  tripsSpy.mockReturnValue({
    data: { items: [trip()], page: 1, pageSize: 50, totalItems: 1 },
    isPending: false,
  });
});

describe('the trips gathered into a camp', () => {
  it('asks for the trips of this camp and links each one to its page', () => {
    show();

    expect(tripsSpy).toHaveBeenCalledWith(expect.objectContaining({ expeditionId: CAMP }));
    const link = screen.getByText('Down the shaft').closest('a');
    expect(link?.getAttribute('href')).toBe('/trip-logs/trip-1');
  });

  it('shows the roll-up for the camp beside the list', () => {
    show();

    expect(screen.getByTestId('rollup').textContent).toBe(`expedition:${CAMP}`);
  });

  it('says an empty list is about the reader, not about the camp', () => {
    // The camp's trip list is filtered like every other listing, so two people see different
    // lists of the same camp and both are right. A bare "no trips" on a camp somebody knows ran
    // for a fortnight reads as data loss; this says whose answer it is.
    tripsSpy.mockReturnValue({
      data: { items: [], page: 1, pageSize: 50, totalItems: 0 },
      isPending: false,
    });
    show();

    expect(screen.getByText(/that you may read/)).toBeTruthy();
    // And the roll-up still appears: a camp with nothing readable in it has totals of its own to
    // state, with the sentence saying they are the reader's totals.
    expect(screen.getByTestId('rollup')).toBeTruthy();
  });

  it('says so when it is showing only part of what the camp gathered', () => {
    tripsSpy.mockReturnValue({
      data: { items: [trip()], page: 1, pageSize: 50, totalItems: 84 },
      isPending: false,
    });
    show();

    expect(screen.getByText(/Showing 1 of 84/)).toBeTruthy();
  });
});
