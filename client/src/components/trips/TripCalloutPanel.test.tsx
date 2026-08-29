// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripCalloutState } from '../../api/hooks.ts';

const standDown = vi.fn();
const arrange = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useStandDownTripCallout: () => ({ mutateAsync: standDown, isPending: false }),
  useArrangeTripCallout: () => ({ mutateAsync: arrange, isPending: false }),
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
  standDown.mockReset().mockResolvedValue({});
  arrange.mockReset().mockResolvedValue({});
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
