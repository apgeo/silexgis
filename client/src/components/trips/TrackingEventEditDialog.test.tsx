// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';

const update = vi.fn();

// The kinds and the station rules are the real ones — what is stubbed is only the write, so a
// change to what a valid report looks like is felt here rather than mocked away.
vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return { ...actual, useUpdateTrackingEvent: () => ({ mutateAsync: update, isPending: false }) };
});

// The pointer, which is what every control's size follows. False by default — the desk this suite
// is read on; the cases about a finger set it and say so.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

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
  corrected: false,
  outsideDeclaredParts: false,
};

beforeEach(() => {
  coarse = false;
  update.mockReset().mockResolvedValue({ ...REPORT });
});
afterEach(cleanup);

/**
 * antd names a deprecated prop through `console.error`, once per render — and in development the
 * application's own error sweep files every one of those as a defect. So every case in this file
 * also checks that nothing it drew spoke that way; a notice written with the old spelling of a
 * prop fails here rather than in somebody's diagnostics.
 */
let consoleError: MockInstance<typeof console.error>;
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error');
});
afterEach(() => {
  const deprecations = consoleError.mock.calls
    .map(([first]) => String(first))
    .filter((line) => /\[antd: [^\]]+\].*deprecated/.test(line));
  consoleError.mockRestore();
  expect(deprecations).toEqual([]);
});

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

  it('saves nothing and rejects nothing when a required field is left empty', async () => {
    // Clearing the station and pressing Save is an ordinary act, and what it must produce is the
    // field's own complaint and nothing else. The validation's refusal used to escape the click
    // handler as an unhandled rejection carrying the form's values — which the browser is watched
    // for, and reported as a defect.
    const escaped = vi.fn();
    process.on('unhandledRejection', escaped);
    try {
      render(
        <App>
          <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      fireEvent.change(screen.getByTestId('trip-tracking-edit-station'), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

      expect(await screen.findByText(/say which station/i)).toBeInTheDocument();
      // A rejection is reported on a later turn of the event loop than the click that caused it,
      // so one is waited out before the absence of one is taken to mean anything.
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(update).not.toHaveBeenCalled();
      expect(escaped).not.toHaveBeenCalled();
    } finally {
      process.off('unhandledRejection', escaped);
    }
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

  /**
   * Built for a finger where there is one.
   *
   * This is the dialog opened from a phone to fix an hour typed off a call, and it is one form with
   * the dialog a station press opens: the same touch target, the same enlarged calendar and list,
   * the same stylesheet that keeps its buttons on screen. What is asserted is that it asks for all
   * of that — the geometry itself is the stylesheet's and cannot be measured here.
   */
  describe('drawn for a finger', () => {
    it('builds every control a finger presses at forty-four pixels', () => {
      coarse = true;
      render(
        <App>
          <TrackingEventEditDialog
            tripLogId="trip-1"
            report={REPORT}
            teams={[{ id: 'team-1', title: 'One' }]}
            onClose={() => {}}
          />
        </App>,
      );

      const styles = Array.from(document.querySelectorAll('style'))
        .map((style) => style.textContent ?? '')
        .join('\n');
      expect(styles).toContain('--ant-control-height-lg:44px');
      expect(screen.getByTestId('trip-tracking-edit-team')).toHaveClass('ant-select-lg');
      expect(screen.getByRole('button', { name: /save the correction/i })).toHaveClass('ant-btn-lg');
      expect(screen.getByTestId('trip-tracking-edit-when-15')).toHaveClass('ant-btn-lg');
    });

    it('wears the stylesheet that keeps the dialog on the screen', () => {
      render(
        <App>
          <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      // The class is the whole of the contract with the stylesheet: no wider than the screen, and
      // a body that scrolls under a footer that never leaves it.
      expect(document.querySelector('.ant-modal.tracking-report-dialog')).not.toBeNull();
    });

    it('marks its calendar to be pinned to the screen, since its fields scroll inside a capped body', async () => {
      coarse = true;
      render(
        <App>
          <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      fireEvent.mouseDown(screen.getByTestId('trip-tracking-edit-recorded-at'));
      fireEvent.click(screen.getByTestId('trip-tracking-edit-recorded-at'));

      const popup = await waitFor(() => {
        const found = document.querySelector('.tracking-report-when-popup');
        expect(found).not.toBeNull();
        return found!;
      });
      expect(popup).toHaveClass('tracking-report-when-popup-pinned');
    });

    it('leaves a mouse the dense chrome it has everywhere else', () => {
      render(
        <App>
          <TrackingEventEditDialog tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      expect(screen.getByRole('button', { name: /save the correction/i })).not.toHaveClass('ant-btn-lg');
    });
  });
});
