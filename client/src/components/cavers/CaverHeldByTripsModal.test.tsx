// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { CaverHeldByTrips } from './caverHeldByTrips.ts';

const removeReports = vi.fn();
const deleteCaver = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useRemoveTrackingReportsOf: () => ({ mutateAsync: removeReports, isPending: false }),
  // The confirmation asks for the number only where it was not handed one. Here the refusal
  // counted the reports, so the read is switched off — answered as a switched-off read answers.
  useTrackingReportsHeldOf: (...asked: unknown[]) => reportsHeldOf(...asked),
  useDeleteCaver: () => ({ mutateAsync: deleteCaver, isPending: false }),
}));

const reportsHeldOf = vi.fn((..._asked: unknown[]) => ({ data: undefined, isPending: true }));

const { CaverHeldByTripsModal } = await import('./CaverHeldByTripsModal.tsx');

const ANA = { id: 'caver-ana', name: 'Ana Popescu' };

const reportsOnly = {
  id: 'trip-reports',
  title: 'Autumn trip',
  tripDate: '2026-09-12',
  onRoster: false,
  reports: 3,
  reportsRemovable: true,
};
const onRoster = {
  id: 'trip-roster',
  title: 'Spring trip',
  tripDate: '2026-04-02',
  onRoster: true,
  reports: 2,
  reportsRemovable: false,
};

const onClose = vi.fn();
const onMerge = vi.fn();

function show(held: CaverHeldByTrips) {
  return render(
    <MemoryRouter>
      <App>
        <CaverHeldByTripsModal caver={ANA} held={held} onClose={onClose} onMerge={onMerge} />
      </App>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  removeReports.mockReset().mockResolvedValue({ removed: 3 });
  deleteCaver.mockReset().mockResolvedValue(undefined);
  onClose.mockReset();
  onMerge.mockReset();
});

afterEach(async () => {
  cleanup();
  await i18n.changeLanguage('en');
});

describe('CaverHeldByTripsModal', () => {
  it('lists each trip as a link, with what holds the person there', () => {
    show({ trips: [reportsOnly, onRoster], heldElsewhere: false });

    expect(screen.getByRole('link', { name: 'Autumn trip' }).getAttribute('href')).toBe(
      '/trip-logs/trip-reports',
    );
    expect(screen.getByRole('link', { name: 'Spring trip' }).getAttribute('href')).toBe(
      '/trip-logs/trip-roster',
    );
    expect(screen.getByTestId('caver-held-reports-trip-reports').textContent).toBe('Tracking reports: 3');
    expect(screen.queryByTestId('caver-held-roster-trip-reports')).toBeNull();
    expect(screen.getByTestId('caver-held-roster-trip-roster').textContent).toBe('On the roster');

    // The removal is offered only where the server said it may be done from here; the other trip
    // says why not instead of showing a button that can only be refused.
    expect(screen.getByTestId('caver-held-remove-trip-reports').textContent).toBe(
      'Remove their reports from this trip: 3',
    );
    expect(screen.queryByTestId('caver-held-remove-trip-roster')).toBeNull();
    expect(screen.getByTestId('caver-held-not-removable-trip-roster')).toBeTruthy();

    // Nothing is said about things out of sight when there are none, and the delete is not
    // offered while something listed still holds the person.
    expect(screen.queryByTestId('caver-held-elsewhere')).toBeNull();
    expect(screen.queryByTestId('caver-held-delete')).toBeNull();
  });

  it('removes the reports only behind a confirmation that names the count and says what changes and what stays', async () => {
    show({ trips: [reportsOnly], heldElsewhere: false });

    fireEvent.click(screen.getByTestId('caver-held-remove-trip-reports'));
    expect(removeReports).not.toHaveBeenCalled();

    const body = await screen.findByTestId('remove-reports-of-body');
    expect(body.textContent).toContain('replay');
    expect(body.textContent).toContain('published page');
    expect(body.textContent).toContain('roster');
    expect(body.textContent).toContain('history');
    expect(
      screen.getByText('Remove every report about Ana Popescu from this trip, for good? Reports: 3'),
    ).toBeTruthy();
    // The number is the refusal's own, so nothing is asked again and nothing is waited for.
    expect(reportsHeldOf).toHaveBeenLastCalledWith('trip-reports', 'caver-ana', false);
    expect(screen.getByTestId('remove-reports-of-confirm')).toBeEnabled();

    fireEvent.click(screen.getByTestId('remove-reports-of-confirm'));
    await waitFor(() =>
      expect(removeReports).toHaveBeenCalledWith({ tripLogId: 'trip-reports', caverId: 'caver-ana' }),
    );

    // The trip held them only by those reports, so it leaves the list — and the delete is then
    // offered, not done.
    await waitFor(() => expect(screen.queryByTestId('caver-held-trip-trip-reports')).toBeNull());
    expect(screen.getByTestId('caver-held-nothing-left')).toBeTruthy();
    expect(deleteCaver).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('caver-held-delete'));
    await waitFor(() => expect(deleteCaver).toHaveBeenCalledWith('caver-ana'));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('keeps a trip whose roster still names the person after their reports are gone', async () => {
    show({ trips: [{ ...onRoster, reportsRemovable: true }], heldElsewhere: false });

    fireEvent.click(screen.getByTestId('caver-held-remove-trip-roster'));
    fireEvent.click(await screen.findByTestId('remove-reports-of-confirm'));

    await waitFor(() => expect(screen.queryByTestId('caver-held-reports-trip-roster')).toBeNull());
    expect(screen.getByTestId('caver-held-roster-trip-roster')).toBeTruthy();
    expect(screen.queryByTestId('caver-held-delete')).toBeNull();
  });

  it('says that something out of sight holds the person, and then never offers the delete', () => {
    show({ trips: [], heldElsewhere: true });

    expect(screen.getByTestId('caver-held-elsewhere')).toBeTruthy();
    expect(screen.queryByTestId('caver-held-nothing-left')).toBeNull();
    expect(screen.queryByTestId('caver-held-delete')).toBeNull();
  });

  it('shows the list again when the delete is refused once more', async () => {
    deleteCaver.mockRejectedValue(
      new ApiError(400, 'caver.referenced_by_trips', 'held', {
        code: 'caver.referenced_by_trips',
        trips: [onRoster],
        heldElsewhere: false,
      }),
    );
    show({ trips: [], heldElsewhere: false });

    fireEvent.click(screen.getByTestId('caver-held-delete'));

    expect(await screen.findByTestId('caver-held-trip-trip-roster')).toBeTruthy();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('keeps merging as the other way out', () => {
    show({ trips: [reportsOnly], heldElsewhere: false });

    fireEvent.click(screen.getByTestId('caver-held-merge'));

    expect(onMerge).toHaveBeenCalled();
  });

  it('says all of it in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    show({ trips: [reportsOnly, onRoster], heldElsewhere: true });

    expect(screen.getByTestId('caver-held-reports-trip-reports').textContent).toBe(
      'Rapoarte de urmărire: 3',
    );
    expect(screen.getByTestId('caver-held-roster-trip-roster').textContent).toBe('Pe listă');
    expect(screen.getByTestId('caver-held-elsewhere').textContent).toContain('administrator');
  });
});
