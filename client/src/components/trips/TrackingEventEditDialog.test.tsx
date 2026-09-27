// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const update = vi.fn();

// The kinds and the station rules are the real ones — what is stubbed is only the write, so a
// change to what a valid report looks like is felt here rather than mocked away.
vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return { ...actual, useUpdateTrackingEvent: () => ({ mutateAsync: update, isPending: false }) };
});

const { default: TrackingEventEditDialog } = await import('./TrackingEventEditDialog.tsx');
type TrackingEvent = import('../../api/hooks.ts').TrackingEvent;

const REPORT: TrackingEvent = {
  id: 'ev-1',
  caverId: 'caver-1',
  teamId: null,
  kind: 'atStation',
  surveyModelId: 'model-1',
  stationName: 'cave.upper.2',
  depthEnteredM: null,
  note: 'first call',
  recordedAt: '2026-09-12T10:00:00Z',
};

beforeEach(() => update.mockReset().mockResolvedValue({ ...REPORT }));
afterEach(cleanup);

/**
 * Correcting a report already on the log.
 *
 * The cases here are the ones where a correction could quietly send something other than what the
 * reader typed — a field left over from the kind the report used to be, or the previous row's
 * values still in a form that is mounted once and reused.
 */
describe('TrackingEventEditDialog', () => {
  it('opens on the report it was given, with its own values in the fields', () => {
    render(
      <App>
        <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');
    expect(screen.getByTestId('trip-tracking-edit-note')).toHaveValue('first call');
  });

  it('says that a correction changes what the log records, rather than doing it silently', () => {
    render(
      <App>
        <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    // The replay and the published page both follow the log, so a reader correcting one is
    // changing what a public surface says happened. That is the point of the feature and is worth
    // stating; a dialog that changed it silently would be the same feature with a worse conscience.
    expect(screen.getByText(/changes what the log says happened/i)).toBeInTheDocument();
  });

  it('keeps the report it corrects, and never offers to change whose report it is', () => {
    render(
      <App>
        <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    // A report about a different person is a different report — the delete beside this control is
    // what that means. A caver chooser here would offer to rewrite a record's subject in place.
    expect(screen.queryByTestId('trip-tracking-edit-caver')).not.toBeInTheDocument();
  });

  it('sends the place belonging to the kind, and clears the one that does not', async () => {
    render(
      <App>
        <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );

    // A station report corrected into an exit must not still carry the station: the server measures
    // the whole report again and refuses a place on a kind that has none, so a leftover field is a
    // correction that cannot be saved and says nothing about why.
    const kindField = screen.getByTestId('trip-tracking-edit-kind');
    fireEvent.mouseDown(kindField.querySelector('.ant-select-selector') ?? kindField);
    await act(async () => {
      fireEvent.click(document.querySelector('.ant-select-item-option[title="Came out"]')!);
    });
    fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

    await waitFor(() => expect(update).toHaveBeenCalled());
    const sent = update.mock.calls[0][0];
    expect(sent.eventId).toBe('ev-1');
    expect(sent.kind).toBe('exited');
    expect(sent.stationName).toBeNull();
    expect(sent.depthM).toBeNull();
  });

  it('fills itself from whichever report is opened, not from the one before it', async () => {
    const { rerender } = render(
      <App>
        <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');

    // One dialog serves every row, so this is the fault that would otherwise offer one report's
    // place as a correction to another's — and it would look like working software.
    rerender(
      <App>
        <TrackingEventEditDialog
          tripLogId="trip-1"
          report={{ ...REPORT, id: 'ev-2', stationName: 'cave.deep.3', note: 'second call' }}
          teams={[]}
          onClose={() => {}}
        />
      </App>,
    );
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.deep.3'),
    );
    expect(screen.getByTestId('trip-tracking-edit-note')).toHaveValue('second call');
  });
});
