// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TrackingEvent, TrackingRemovedEvent } from '../../api/hooks.ts';

const ANA = '11111111-1111-1111-1111-111111111111';

const removedQuery = vi.fn();
const restoreEvent = vi.fn();
const destroyEvent = vi.fn();
const refetch = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_REMOVED_PAGE_SIZE: 20,
  useTripTrackingRemovedEvents: (...asked: unknown[]) => removedQuery(...asked),
  useRestoreTrackingEvent: () => ({ mutateAsync: restoreEvent, isPending: false }),
  useDestroyTrackingEvent: () => ({ mutateAsync: destroyEvent, isPending: false }),
}));

const { default: TrackingRemovedReports } = await import('./TrackingRemovedReports.tsx');

function report(overrides: Partial<TrackingEvent> = {}): TrackingEvent {
  return {
    id: 'event-1',
    caverId: ANA,
    teamId: null,
    kind: 'atStation',
    surveyModelId: null,
    stationName: 'P12',
    depthEnteredM: null,
    note: 'at the big pitch',
    recordedAt: '2026-09-12T07:00:00Z',
    corrected: false,
    outsideDeclaredParts: false,
    depthPlacement: null,
    ...overrides,
  } as TrackingEvent;
}

function holding(items: TrackingRemovedEvent[], totalItems = items.length) {
  removedQuery.mockReturnValue({
    data: { items, page: 1, pageSize: 20, totalItems },
    isPending: false,
    isPlaceholderData: false,
    error: null,
    refetch,
  });
}

/** The place as the log's own function would draw it: the station, or the word for a withholding. */
const placeOf = (row: TrackingEvent) =>
  row.stationName ?? <span data-testid="withheld-mark">withheld</span>;

function show() {
  return render(
    <App>
      <TrackingRemovedReports
        tripLogId="trip-1"
        nameOf={(caverId) => (caverId === ANA ? 'Ana Popescu' : 'somebody')}
        placeOf={placeOf}
        when={(value) => value ?? '—'}
        controlSize="small"
      />
    </App>,
  );
}

/** Opens the fold, as somebody who wants a report back does. */
function open() {
  fireEvent.click(screen.getByTestId('trip-tracking-removed-count'));
}

beforeEach(() => {
  removedQuery.mockReset();
  restoreEvent.mockReset().mockResolvedValue(report());
  destroyEvent.mockReset().mockResolvedValue(undefined);
  refetch.mockReset();
});

afterEach(async () => {
  cleanup();
  await i18n.changeLanguage('en');
});

describe('TrackingRemovedReports', () => {
  it('draws nothing at all while no report has been taken off the log', () => {
    holding([]);
    show();

    expect(screen.queryByTestId('trip-tracking-removed')).toBeNull();
  });

  it('says how many reports are removed without being opened, and reads the list again when it is', () => {
    holding([{ report: report(), removedAt: '2026-09-12T08:00:00Z' }], 3);
    show();

    // The count is the server's total, not the length of the page that happens to be held.
    expect(screen.getByTestId('trip-tracking-removed-count')).toHaveTextContent(
      'Removed reports: 3',
    );
    expect(refetch).not.toHaveBeenCalled();

    open();
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('shows a removed report with its place, and puts it back by its own id', async () => {
    holding([{ report: report(), removedAt: '2026-09-12T08:00:00Z' }]);
    show();
    open();

    const row = await screen.findByTestId('trip-tracking-removed-event-1');
    expect(row).toHaveTextContent('Ana Popescu');
    expect(row).toHaveTextContent('at the big pitch');
    expect(within(row).getByTestId('trip-tracking-removed-place-event-1')).toHaveTextContent('P12');
    expect(row).toHaveTextContent('Taken off the log: 2026-09-12T08:00:00Z');

    fireEvent.click(within(row).getByTestId('trip-tracking-removed-restore-event-1'));

    await waitFor(() => expect(restoreEvent).toHaveBeenCalledTimes(1));
    expect(restoreEvent.mock.calls[0][0]).toEqual({ tripLogId: 'trip-1', eventId: 'event-1' });
    expect(await screen.findByText('The report is back on the log, as it was.')).toBeTruthy();
    // Putting back is not destroying, whatever else happened.
    expect(destroyEvent).not.toHaveBeenCalled();
  });

  it('draws a place the reader may not be told through the log\'s own withholding, beside one they may', async () => {
    holding([
      // A station report with no station is a withholding and nothing else.
      { report: report({ id: 'event-hidden', stationName: null }), removedAt: '2026-09-12T08:10:00Z' },
      { report: report({ id: 'event-open', stationName: 'P7' }), removedAt: '2026-09-12T08:00:00Z' },
    ]);
    show();
    open();

    const hidden = await screen.findByTestId('trip-tracking-removed-place-event-hidden');
    expect(within(hidden).getByTestId('withheld-mark')).toBeTruthy();
    expect(hidden).not.toHaveTextContent('P12');
    // The positive case in the same breath: the mark is the row's own, not the surface's.
    const shown = screen.getByTestId('trip-tracking-removed-place-event-open');
    expect(shown).toHaveTextContent('P7');
    expect(within(shown).queryByTestId('withheld-mark')).toBeNull();
  });

  it('destroys a report only behind a confirmation that says it cannot be undone', async () => {
    holding([{ report: report(), removedAt: '2026-09-12T08:00:00Z' }]);
    show();
    open();

    fireEvent.click(await screen.findByTestId('trip-tracking-removed-destroy-event-1'));
    // Pressing the button asked a question and destroyed nothing.
    const confirm = await screen.findByText(/Delete this report for good\?/);
    expect(confirm).toHaveTextContent('This cannot be undone');
    expect(destroyEvent).not.toHaveBeenCalled();

    // The confirming button says what it does rather than "OK".
    const popup = confirm.closest('.ant-popover') as HTMLElement;
    fireEvent.click(within(popup).getByRole('button', { name: 'Delete for good' }));

    await waitFor(() => expect(destroyEvent).toHaveBeenCalledTimes(1));
    expect(destroyEvent.mock.calls[0][0]).toEqual({ tripLogId: 'trip-1', eventId: 'event-1' });
    expect(await screen.findByText('The report has been deleted for good.')).toBeTruthy();
    expect(restoreEvent).not.toHaveBeenCalled();
  });

  it('words a refusal to destroy a report somebody has put back in the meantime', async () => {
    holding([{ report: report(), removedAt: '2026-09-12T08:00:00Z' }]);
    destroyEvent.mockRejectedValue(new ApiError(409, 'tracking.event_not_removed', 'server words'));
    show();
    open();

    fireEvent.click(await screen.findByTestId('trip-tracking-removed-destroy-event-1'));
    const confirm = await screen.findByText(/Delete this report for good\?/);
    const popup = confirm.closest('.ant-popover') as HTMLElement;
    fireEvent.click(within(popup).getByRole('button', { name: 'Delete for good' }));

    expect(await screen.findByText(/That report is on the log again/)).toBeTruthy();
  });

  it('says the same in Romanian', async () => {
    await i18n.changeLanguage('ro');
    holding([{ report: report(), removedAt: '2026-09-12T08:00:00Z' }], 2);
    show();

    expect(screen.getByTestId('trip-tracking-removed-count')).toHaveTextContent(
      'Rapoarte scoase: 2',
    );
    open();
    expect(await screen.findByRole('button', { name: /Pune înapoi/ })).toBeTruthy();
    fireEvent.click(screen.getByTestId('trip-tracking-removed-destroy-event-1'));
    expect(await screen.findByText(/Ștergi definitiv acest raport\?/)).toHaveTextContent(
      'nu poate fi anulată',
    );
  });
});
