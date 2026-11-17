// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TripLogInfo } from '../../api/hooks.ts';

const read = vi.fn();
const mutate = vi.fn();
const refetch = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useTripChecklist: () => read(),
  useSetTripChecklistItem: () => ({ mutate, isPending: false }),
}));

const { default: TripChecklistTab } = await import('./TripChecklistTab.tsx');
const { TRIP_CHECKLIST_PROBLEM_MESSAGE_KEYS } = await import('./tripChecklistProblems.ts');

const trip = { id: 'trip-1', title: 'Digging weekend' } as unknown as TripLogInfo;

const list = {
  tripLogId: 'trip-1',
  checklistId: 'list-1',
  title: 'Before we go',
  description: null,
  ticked: 1,
  total: 2,
  items: [
    { id: 'item-1', text: 'Rope checked', ticked: true, tickedAt: '2026-03-01T10:00:00Z' },
    { id: 'item-2', text: 'Callout set', ticked: false, tickedAt: null },
  ],
};

function show(canEdit = true) {
  return render(
    <App>
      <TripChecklistTab trip={trip} canEdit={canEdit} />
    </App>,
  );
}

beforeEach(() => {
  read.mockReset();
  mutate.mockReset();
  refetch.mockReset();
});

afterEach(() => {
  cleanup();
});

describe('TripChecklistTab', () => {
  it('says the trip works through no list when the server says so', () => {
    read.mockReturnValue({ data: { ...list, checklistId: null, items: [] }, isLoading: false, isError: false, refetch });
    show();
    expect(screen.getByText(i18n.t('trips.checklistNone'))).toBeTruthy();
  });

  it('shows how much of the list is settled', () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: false, refetch });
    show();
    expect(screen.getByText(i18n.t('trips.checklistReadiness', { ticked: 1, total: 2 }))).toBeTruthy();
  });

  // A read that did not land says nothing about whether the trip has a list. Showing "no list"
  // for it tells the party there is nothing to settle before they set off.
  it('says the list could not be read, and offers to ask again, when the read fails', () => {
    read.mockReturnValue({ data: undefined, isLoading: false, isError: true, error: new Error('offline'), refetch });
    show();
    expect(screen.queryByText(i18n.t('trips.checklistNone'))).toBeNull();
    expect(screen.getByTestId('trip-checklist-load-failed')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('keeps a list already shown when a later read fails, and says it may be stale', () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: true, error: new Error('offline'), refetch });
    show();
    expect(screen.getByText('Rope checked')).toBeTruthy();
    expect(screen.getByTestId('trip-checklist-refresh-failed')).toBeTruthy();
  });

  it('ticks a line with the trip and the line it names', () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: false, refetch });
    show();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Callout set' }));
    expect(mutate).toHaveBeenCalledWith(
      { tripLogId: 'trip-1', itemId: 'item-2', ticked: true },
      expect.anything(),
    );
  });

  it('words a refused tick in the reader\'s language rather than the server\'s sentence', async () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: false, refetch });
    mutate.mockImplementation((_vars, options: { onError: (e: unknown) => void }) =>
      options.onError(new ApiError(403, 'trip_checklist.tick_forbidden', 'Server sentence in English')),
    );
    show();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Callout set' }));
    expect(await screen.findByText(i18n.t('trips.checklistProblems.tickForbidden'))).toBeTruthy();
    expect(screen.queryByText('Server sentence in English')).toBeNull();
  });

  it('words a trip that went away in the one sentence the trip area already has for it', async () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: false, refetch });
    mutate.mockImplementation((_vars, options: { onError: (e: unknown) => void }) =>
      options.onError(new ApiError(404, 'trip_log.not_found')),
    );
    show();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Callout set' }));
    expect(await screen.findByText(i18n.t('trips.tracking.problems.tripNotFound'))).toBeTruthy();
  });

  it('falls back to the general sentence for a failure with no code and no detail', async () => {
    read.mockReturnValue({ data: list, isLoading: false, isError: false, refetch });
    mutate.mockImplementation((_vars, options: { onError: (e: unknown) => void }) =>
      options.onError(new ApiError(500)),
    );
    show();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Callout set' }));
    expect(await screen.findByText(i18n.t('trips.checklistTickFailed'))).toBeTruthy();
  });

  it('holds every refusal sentence in both languages', () => {
    for (const key of Object.values(TRIP_CHECKLIST_PROBLEM_MESSAGE_KEYS)) {
      expect(i18n.exists(key, { lng: 'en' }), `${key} in English`).toBe(true);
      expect(i18n.exists(key, { lng: 'ro' }), `${key} in Romanian`).toBe(true);
    }
  });
});
