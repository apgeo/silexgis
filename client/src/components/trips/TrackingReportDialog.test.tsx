// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const recordEvents = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
}));

// What decides how big every target in this dialog is drawn. False by default: the machine this
// suite is read on has a mouse.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingReportDialog } = await import('./TrackingReportDialog.tsx');

const onClose = vi.fn();
const onRecorded = vi.fn();

const CAVERS = [
  { caverId: 'caver-1', name: 'Ana' },
  { caverId: 'caver-2', name: 'Bogdan' },
];

function show(props: { station?: string; defaultCaverIds?: string[]; teams?: { id: string; title: string }[] } = {}) {
  return render(
    <App>
      <TrackingReportDialog
        open
        tripLogId="trip-1"
        station={props.station ?? 'p.g.42'}
        cavers={CAVERS}
        teams={props.teams ?? []}
        defaultCaverIds={props.defaultCaverIds ?? ['caver-1']}
        onClose={onClose}
        onRecorded={onRecorded}
      />
    </App>,
  );
}

/** Presses the dialog's own accept button and lets everything behind it settle. */
async function accept() {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: /Record for/ }));
  });
}

beforeEach(() => {
  coarse = false;
  recordEvents.mockReset().mockResolvedValue([{}]);
  onClose.mockReset();
  onRecorded.mockReset();
});

afterEach(cleanup);

/**
 * The dialog a station press opens.
 *
 * Its whole reason for existing is speed — it is filled in one-handed while somebody is still on
 * the phone — so what is tested is that the press really does the work: the station arrives named,
 * the likely party arrives chosen, and the thing that is sent is the same thing the card under the
 * watch sends.
 */
describe('TrackingReportDialog', () => {
  it('arrives with the pressed station and the ticked party already in it', () => {
    show();

    expect(screen.getByTestId('trip-tracking-dialog-station')).toHaveValue('p.g.42');
    expect(screen.getByRole('button', { name: /Record for 1/ })).toBeInTheDocument();
  });

  it('sends a station report and closes', async () => {
    show();
    fireEvent.change(screen.getByTestId('trip-tracking-dialog-note'), {
      target: { value: '  waiting at the pitch head  ' },
    });

    await accept();

    expect(recordEvents).toHaveBeenCalledWith({
      tripLogId: 'trip-1',
      caverIds: ['caver-1'],
      kind: 'atStation',
      stationName: 'p.g.42',
      // A depth belongs to a depth report; the server refuses a request carrying the other one
      // rather than quietly ignoring it.
      depthM: null,
      teamId: null,
      // Trimmed, because a note of spaces is not a note — the same reading the card has.
      note: 'waiting at the pitch head',
      recordedAt: null,
    });
    await waitFor(() => expect(onRecorded).toHaveBeenCalled());
    expect(onClose).toHaveBeenCalled();
  });

  /**
   * The station is a field and not a fact.
   *
   * The viewer's spelling of a station and the server's are not guaranteed to be the same string
   * for every survey format — the server prefixes some models with the root survey's name and the
   * viewer's reader does not. When they differ the server answers that it has no station by that
   * name, and a read-only field would leave the reader holding a refusal with nothing to do about
   * it.
   */
  it('lets the pressed station be corrected, and refuses an empty one', async () => {
    show();
    const station = screen.getByTestId('trip-tracking-dialog-station');

    fireEvent.change(station, { target: { value: '' } });
    await accept();
    expect(recordEvents).not.toHaveBeenCalled();
    expect(await screen.findByText('Say which station.')).toBeInTheDocument();

    fireEvent.change(station, { target: { value: 'pestera.p.g.42' } });
    await accept();
    expect(recordEvents).toHaveBeenCalledWith(
      expect.objectContaining({ stationName: 'pestera.p.g.42' }),
    );
  });

  it('refuses a report about nobody rather than sending one', async () => {
    // The same rule the card states with a warning above its buttons: one report is recorded for
    // everybody it names at once, so a report naming nobody is not a report.
    show({ defaultCaverIds: [] });

    await accept();

    expect(recordEvents).not.toHaveBeenCalled();
    expect(await screen.findByText('Say who this report is about.')).toBeInTheDocument();
  });

  it('keeps the dialog standing when the server refuses the report', async () => {
    // Closing on a refusal would take the only copy of what somebody typed away with it, at the
    // moment they most need to change one field of it and try again.
    recordEvents.mockRejectedValue(new Error('nope'));
    show();

    await accept();

    expect(onClose).not.toHaveBeenCalled();
    expect(onRecorded).not.toHaveBeenCalled();
  });

  /**
   * Built larger than the surface behind it, and deliberately.
   *
   * This is the one place in the feature where somebody is filling a form in while looking at a
   * model they have just pressed, one-handed, with the other hand holding a phone. The card under
   * the watch keeps the forty pixels it was measured at — those measurements were taken against a
   * calendar whose geometry depends on them — and this surface, which has no such history, is
   * simply built bigger.
   */
  it('builds every control a finger presses at forty-four pixels', () => {
    coarse = true;
    show({ teams: [{ id: 'team-1', title: 'One' }] });

    const styles = Array.from(document.querySelectorAll('style'))
      .map((style) => style.textContent ?? '')
      .join('\n');
    expect(styles).toContain('--ant-control-height-lg:44px');
    expect(screen.getByTestId('trip-tracking-dialog-team')).toHaveClass('ant-select-lg');
    expect(screen.getByRole('button', { name: /Record for/ })).toHaveClass('ant-btn-lg');
  });

  it('leaves a mouse the dense chrome it has everywhere else', () => {
    show();

    expect(screen.getByRole('button', { name: /Record for/ })).not.toHaveClass('ant-btn-lg');
  });

  /**
   * The dialog opens over the tab the card is in, so both are in the document at once — and the
   * "when it was said" field is one component drawn in both. Under one set of names a locator for
   * it matches two elements and picks neither, which is a defect this application has met before
   * and answered the same way: the surface names its own controls.
   */
  it('names its copy of the shared time field apart from the card’s', () => {
    show();

    expect(screen.getByTestId('trip-tracking-dialog-recorded-at')).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-dialog-when-15')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-recorded-at')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-when-15')).toBeNull();
  });

  /**
   * The calendar behind "when it was said", opened from inside a modal.
   *
   * <b>The card under the watch makes room for that panel by scrolling the field to the top of the
   * screen. The dialog cannot.</b> Its fields scroll inside the modal's body, which is capped so the
   * buttons that finish the report never leave the screen, and the whole of that body's scroll range
   * is 197px. Measured on the live instance on a 412x839 phone with the body scrolled as far as it
   * goes: the field stops at y=353, the panel opens at y=391, and its footer — holding "Now" and the
   * "OK" that is the only way to commit a time — sits at 1086–1133, 291px below the bottom of the
   * screen, with nothing left on the page that moves it. Landscape is the same defect smaller, at
   * 38px. Nothing wrong is recorded, because the field simply stays empty and the server stamps its
   * own clock; the field is just dead.
   *
   * So on this surface the panel is anchored to the screen instead of to the field. That is the
   * stylesheet's work and there is no layout in this environment to measure, so what is asserted is
   * that the dialog's panel is marked for it and the card's is not — the two surfaces really do want
   * different answers, and marking both would move a panel that has room for itself.
   */
  it('marks its calendar to be pinned to the screen, which the card’s is not', async () => {
    coarse = true;
    show();

    fireEvent.mouseDown(screen.getByTestId('trip-tracking-dialog-recorded-at'));
    fireEvent.click(screen.getByTestId('trip-tracking-dialog-recorded-at'));

    const popup = await waitFor(() => {
      const found = document.querySelector('.tracking-report-when-popup');
      expect(found).not.toBeNull();
      return found!;
    });
    expect(popup).toHaveClass('tracking-report-when-popup-pinned');
  });

  it('sends the moment a quick answer names', async () => {
    vi.useFakeTimers();
    const now = new Date('2026-09-12T14:00:00.000Z');
    vi.setSystemTime(now);
    try {
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-dialog-when-30'));
      await accept();

      const body = recordEvents.mock.calls[0][0] as { recordedAt: string };
      expect(new Date(body.recordedAt).getTime()).toBe(now.getTime() - 30 * 60_000);
    } finally {
      vi.useRealTimers();
    }
  });
});
