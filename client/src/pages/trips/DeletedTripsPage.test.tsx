// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { DeletedTripLog } from '../../api/hooks.ts';

const { listSpy, configSpy, restoreSpy } = vi.hoisted(() => ({
  listSpy: vi.fn(),
  configSpy: vi.fn(),
  restoreSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useDeletedTripLogs: (page: number) => listSpy(page),
  useTripLogConfig: () => configSpy(),
  useRestoreTripLog: () => ({ mutateAsync: restoreSpy, isPending: false, variables: undefined }),
}));

const { default: DeletedTripsPage } = await import('./DeletedTripsPage.tsx');

const DAY = 24 * 60 * 60 * 1000;

function deleted(overrides: Partial<DeletedTripLog> = {}): DeletedTripLog {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    tripDate: '2026-09-05',
    tripDateEnd: null,
    deletedAt: new Date(Date.now() - 2 * DAY).toISOString(),
    // Half a day past the round figure, so the count below does not turn on the millisecond
    // between building this row and drawing it.
    restorableUntil: new Date(Date.now() + 28 * DAY + DAY / 2).toISOString(),
    deletedByUserId: '22222222-2222-2222-2222-222222222222',
    deletedByName: 'Ana Popescu',
    ...overrides,
  };
}

function answer(items: DeletedTripLog[], totalItems = items.length) {
  listSpy.mockReturnValue({
    data: { items, page: 1, pageSize: 50, totalItems },
    isFetching: false,
    isError: false,
  });
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/trip-logs/deleted']}>
        <Routes>
          <Route path="/trip-logs/deleted" element={<DeletedTripsPage />} />
          <Route path="/trip-logs/:id" element={<div data-testid="trip-page" />} />
        </Routes>
      </MemoryRouter>
    </AntApp>,
  );
}

/** Presses Restore on the one row and then the confirmation's own button. */
async function restoreTheRow() {
  fireEvent.click(screen.getByTestId('deleted-trip-restore'));
  const dialog = await screen.findByRole('tooltip');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  configSpy.mockReset().mockReturnValue({ data: { deletedRetentionDays: 30 } });
  restoreSpy.mockReset().mockResolvedValue({ id: '11111111-1111-1111-1111-111111111111' });
  answer([deleted()]);
});

describe('deleted trips', () => {
  it('lists a deleted trip with who deleted it and how long it has left', () => {
    show();

    const table = screen.getByTestId('deleted-trips');
    expect(within(table).getByText('Coiba Mare recce')).toBeTruthy();
    expect(within(table).getByText('Ana Popescu')).toBeTruthy();
    expect(within(table).getByText('in 28 days')).toBeTruthy();
  });

  /**
   * The window is the installation's, and the page says the number the server gave rather than
   * one it carries itself: an operator who shortened it to a week must not be contradicted by
   * the page that lists what is about to go.
   */
  it('says the window the server reported, whatever it is', () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: 7 } });
    show();

    expect(screen.getByText(/kept for 7 days and then removed for good/)).toBeTruthy();
  });

  it('makes no claim about the window before the server has answered', () => {
    configSpy.mockReturnValue({ data: undefined });
    show();

    expect(screen.queryByText(/removed for good\./)).toBeNull();
    expect(screen.queryByText(/until an administrator decides otherwise/)).toBeNull();
  });

  /**
   * An installation that sets no window removes nothing, so no trip has a date to go by. Saying
   * "in 0 days" there would announce a removal that is never coming.
   */
  it('says a trip is not scheduled to go where the installation keeps deleted trips', () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: null } });
    answer([deleted({ restorableUntil: null })]);
    show();

    expect(screen.getByText(/until an administrator decides otherwise/)).toBeTruthy();
    expect(within(screen.getByTestId('deleted-trips')).getByText('Not scheduled')).toBeTruthy();
  });

  it('says a trip with less than a day left goes within a day, not in no days', () => {
    answer([deleted({ restorableUntil: new Date(Date.now() + DAY / 4).toISOString() })]);
    show();

    expect(within(screen.getByTestId('deleted-trips')).getByText('within a day')).toBeTruthy();
  });

  it('restores only after the confirmation, then opens the trip', async () => {
    show();

    fireEvent.click(screen.getByTestId('deleted-trip-restore'));
    expect(restoreSpy).not.toHaveBeenCalled();

    const dialog = await screen.findByRole('tooltip');
    expect(within(dialog).getByText('Restore this trip?')).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));

    await waitFor(() => expect(restoreSpy).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111'));
    expect(await screen.findByTestId('trip-page')).toBeTruthy();
  });

  /**
   * Each refusal the route names is said in its own words and leaves the reader where they are:
   * a trip whose window ran out between the list being drawn and the button being pressed is not
   * an error to retry.
   */
  it.each([
    [new ApiError(409, 'trip_log.restore_window_passed'), 'That trip was deleted too long ago to be restored.'],
    [new ApiError(409, 'trip_log.not_deleted'), 'That trip is no longer among the deleted ones.'],
    [new ApiError(404, 'trip_log.not_found'), 'That trip is no longer among the deleted ones.'],
    [new ApiError(403, 'acl.forbidden'), 'You may not restore that trip.'],
  ])('says why a restore was refused and stays on the list (%#)', async (error, sentence) => {
    restoreSpy.mockRejectedValue(error);
    show();

    await restoreTheRow();

    expect(await screen.findByText(sentence)).toBeTruthy();
    expect(screen.queryByTestId('trip-page')).toBeNull();
  });

  it('tells an empty list from one that could not be read', () => {
    answer([]);
    const first = show();
    expect(screen.getByTestId('deleted-trips-empty').textContent).toContain('Nothing here');
    first.unmount();

    listSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    show();
    expect(screen.getByTestId('deleted-trips-empty').textContent).toContain('could not be loaded');
  });
});
