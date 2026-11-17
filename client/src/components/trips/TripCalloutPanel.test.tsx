// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripCalloutState } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';

const standDown = vi.fn();
const arrange = vi.fn();
const readTripAgain = vi.fn();

// The version on file for the trip, as the transport would answer it at this moment.
const versionOnFile = vi.fn<(path: string) => string | undefined>(() => undefined);

vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  lastReadETag: (path: string) => versionOnFile(path),
}));

vi.mock('../../api/hooks.ts', () => ({
  useStandDownTripCallout: () => ({ mutateAsync: standDown, isPending: false }),
  useArrangeTripCallout: () => ({ mutateAsync: arrange, isPending: false }),
  useReadTripLogAgain: () => readTripAgain,
}));

const { default: TripCalloutPanel } = await import('./TripCalloutPanel.tsx');

const HOUR = 60 * 60 * 1000;

function show(
  state: TripCalloutState,
  {
    lastChecked = new Date(Date.now() - 60_000).toISOString(),
    canStandDown = true,
    canEdit = false,
  }: { lastChecked?: string | null; canStandDown?: boolean; canEdit?: boolean } = {},
) {
  return render(
    <App>
      <TripCalloutPanel
        tripId="trip-1"
        state={state}
        expectedReturnAt={new Date(Date.now() + 2 * HOUR).toISOString()}
        calloutAlarmAt={new Date(Date.now() + 4 * HOUR).toISOString()}
        calloutLastCheckedAt={lastChecked}
        canStandDown={canStandDown}
        canEdit={canEdit}
      />
    </App>,
  );
}

beforeEach(() => {
  versionOnFile.mockReset().mockReturnValue(undefined);
  standDown.mockReset().mockResolvedValue({});
  arrange.mockReset().mockResolvedValue({});
  readTripAgain.mockReset();
});

afterEach(cleanup);

describe('TripCalloutPanel', () => {
  it('says nothing at all about a trip nobody arranged a callout for', () => {
    show('none');

    expect(screen.queryByTestId('trip-callout')).toBeNull();
    // Nor does it offer to arrange one to somebody who could not save it.
    expect(screen.queryByTestId('trip-callout-arrange')).toBeNull();
  });

  // Without this the whole feature is unreachable: no trip ever leaves "none arranged", so the
  // pass finds nothing, no alarm can fire, and the tap that stands one down is never drawn.
  it('offers whoever may change the trip somewhere to arrange a callout that has none', () => {
    show('none', { canEdit: true });

    expect(screen.getByTestId('trip-callout-arrange')).toHaveTextContent('Arrange a callout');
  });

  it('sends both times to the route that arms the check', async () => {
    show('none', { canEdit: true });
    fireEvent.click(screen.getByTestId('trip-callout-arrange'));

    fireEvent.click(await screen.findByText('Save'));

    await waitFor(() => expect(arrange).toHaveBeenCalledTimes(1));
    expect(arrange.mock.calls[0][0]).toMatchObject({ id: 'trip-1' });
    expect(arrange.mock.calls[0][0]).toHaveProperty('expectedReturnAt');
    expect(arrange.mock.calls[0][0]).toHaveProperty('calloutAlarmAt');
  });

  it('lets somebody who may change the trip change a callout that is already armed', () => {
    show('armed', { canEdit: true });

    expect(screen.getByTestId('trip-callout-arrange')).toHaveTextContent('Change the callout');
  });

  it('says when the check last ran, so an armed alarm is not read as a kept promise', () => {
    show('armed');

    expect(screen.getByTestId('trip-callout-last-checked')).not.toBeNull();
    expect(screen.queryByTestId('trip-callout-unchecked')).toBeNull();
  });

  // The rule this whole component exists for. Silence is the good news in a callout, so a
  // watcher that has stopped and a party that is fine look identical from the page — and the
  // page must come down on the side of saying nobody has looked.
  it('reads an alarm nothing has checked lately as unchecked and never as safe', () => {
    show('armed', { lastChecked: new Date(Date.now() - 6 * HOUR).toISOString() });

    const warning = screen.getByTestId('trip-callout-unchecked');
    expect(warning).toHaveTextContent(/not run lately/i);
    expect(warning).toHaveTextContent(/unchecked, not as safe/i);
    // The words that must not be there: nothing on this panel may report an unwatched alarm as
    // an all-clear.
    expect(warning).not.toHaveTextContent(/all is well/i);
  });

  // A desk left on the trip page is the use a callout is for, and nothing else redraws the panel
  // while the reader only watches it: the hour passing has to be enough on its own.
  it('starts saying unchecked when the hour passes on a page nobody touches', () => {
    vi.useFakeTimers();
    try {
      show('armed', { lastChecked: new Date(Date.now() - HOUR + 30_000).toISOString() });
      expect(screen.queryByTestId('trip-callout-unchecked')).toBeNull();

      act(() => {
        vi.advanceTimersByTime(2 * 60_000);
      });

      expect(screen.getByTestId('trip-callout-unchecked')).not.toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('treats a check nothing has ever run as the strongest form of the same warning', () => {
    show('armed', { lastChecked: null });

    expect(screen.getByTestId('trip-callout-unchecked')).not.toBeNull();
    expect(screen.getByTestId('trip-callout-last-checked')).toHaveTextContent('Never');
  });

  // An alarm that fired and then stopped being watched is no more trustworthy than one that
  // never fired, so the warning is not suppressed by the louder message beside it.
  it('warns about a stale check even while the party is already reported overdue', () => {
    show('overdue', { lastChecked: null });

    expect(screen.getByTestId('trip-callout-overdue')).not.toBeNull();
    expect(screen.getByTestId('trip-callout-unchecked')).not.toBeNull();
  });

  it('offers the tap only to somebody on the trip, and sends it for the trip it is drawn for', () => {
    const { unmount } = show('armed', { canStandDown: false });
    expect(screen.queryByTestId('trip-callout-stand-down')).toBeNull();
    unmount();

    show('armed');
    fireEvent.click(screen.getByTestId('trip-callout-stand-down'));
    expect(standDown).toHaveBeenCalledWith({ id: 'trip-1' });
  });

  it('stops offering the tap once the party has said it is out', () => {
    show('stoodDown');

    expect(screen.getByTestId('trip-callout')).toHaveTextContent('Party is out');
    expect(screen.queryByTestId('trip-callout-stand-down')).toBeNull();
    // Nothing is being watched any more, so there is no promise to report on.
    expect(screen.queryByTestId('trip-callout-last-checked')).toBeNull();
  });
});

/**
 * The dialog is filled when it opens, while the trip page behind it goes on reading the trip — a
 * return from the phone app the coordinator used to ring the party is a read — and every read files
 * a newer version. A save carrying that one would put the alarm the dialog opened with back over
 * the one another coordinator set meanwhile, on a safety arrangement, with nobody told.
 */
describe('arranging from a dialog opened before the trip was read again', () => {
  const path = '/api/v1/trip-logs/trip-1';

  function panel(alarmAt: string) {
    return (
      <App>
        <TripCalloutPanel
          tripId="trip-1"
          state="armed"
          expectedReturnAt={new Date(Date.now() + 2 * HOUR).toISOString()}
          calloutAlarmAt={alarmAt}
          calloutLastCheckedAt={new Date(Date.now() - 60_000).toISOString()}
          canStandDown
          canEdit
        />
      </App>
    );
  }

  it('sends the version the dialog opened with', async () => {
    versionOnFile.mockImplementation((asked) => (asked === path ? '"1"' : undefined));
    const view = render(panel(new Date(Date.now() + 4 * HOUR).toISOString()));
    fireEvent.click(screen.getByTestId('trip-callout-arrange'));
    await screen.findByText('Save');

    versionOnFile.mockImplementation((asked) => (asked === path ? '"2"' : undefined));
    view.rerender(panel(new Date(Date.now() + 6 * HOUR).toISOString()));

    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(arrange).toHaveBeenCalledTimes(1));
    expect(arrange.mock.calls[0][0]).toMatchObject({ id: 'trip-1', ifMatch: '"1"' });
  });

  // Nothing but the refusal has moved: the page has not read the trip since the dialog opened, so
  // its copy and the version on file are the ones the refused save was made from. Refilled from
  // them, the next save would be refused in turn, over and over, on a safety arrangement.
  it('says nothing of its own over the conflict warning, and refills from the trip read again', async () => {
    arrange.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    versionOnFile.mockImplementation((asked) => (asked === path ? '"1"' : undefined));
    const theirAlarm = new Date(Date.now() + 6 * HOUR);
    readTripAgain.mockResolvedValue({
      trip: { expectedReturnAt: new Date(Date.now() + 3 * HOUR).toISOString(), calloutAlarmAt: theirAlarm.toISOString() },
      version: '"2"',
    });
    render(panel(new Date(Date.now() + 4 * HOUR).toISOString()));
    fireEvent.click(screen.getByTestId('trip-callout-arrange'));
    await screen.findByText('Save');

    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(arrange).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(readTripAgain).toHaveBeenCalledWith('trip-1'));
    // A message is drawn a moment after it is asked for; this is long enough for one to appear.
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(screen.queryByText(/could not be saved|failed/i)).toBeNull();

    // Saved again, it carries the version of the read and the times that read found.
    arrange.mockReset().mockResolvedValue({});
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(arrange).toHaveBeenCalledTimes(1));
    expect(arrange.mock.calls[0][0]).toMatchObject({ ifMatch: '"2"' });
    expect(arrange.mock.calls[0][0].calloutAlarmAt).toBe(theirAlarm.toISOString());
  });
});
