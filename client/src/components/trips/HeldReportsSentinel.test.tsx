// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { HeldReport } from './trackingOutbox.ts';

const sendHeld = vi.fn();
vi.mock('../../api/hooks.ts', () => ({
  useSendHeldTrackingReport: () => sendHeld,
}));

// Signing in again is a redirect out of the page, so it is stood in for: what is under test is
// that it is offered, and where it is told to come back to.
const signInAgain = vi.fn<(returnTo: string) => Promise<void>>();
vi.mock('../../auth/renewSignIn.ts', () => ({
  renewSignIn: () => Promise.resolve('lapsed'),
  onSignInRenewed: () => () => undefined,
  signInAgain: (returnTo: string) => signInAgain(returnTo),
}));

const { default: HeldReportsSentinel } = await import('./HeldReportsSentinel.tsx');
const { holdReport, heldReportsOf, refreshHeldReports } = await import('./trackingOutbox.ts');
const { resetHeldReportsDrain } = await import('./trackingOutboxDrain.ts');

const ANA = 'account-ana';

let made = 0;
function held(over: Partial<HeldReport> = {}, body: Partial<HeldReport['body']> = {}): HeldReport {
  made += 1;
  const entry: HeldReport = {
    clientKey: `key-${made}`,
    accountId: ANA,
    tripLogId: 'trip-1',
    body: {
      caverIds: ['caver-1'],
      kind: 'entered',
      stationName: null,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt: `2026-05-01T10:00:0${made}.000Z`,
      ...body,
    },
    composedAt: `2026-05-01T10:00:0${made}.000Z`,
    state: 'held',
    problemCode: null,
    attempts: 1,
    ...over,
  };
  act(() => {
    holdReport(entry);
  });
  return entry;
}

/**
 * Whether the list is open to its reader.
 *
 * Not simply whether it is in the document: antd keeps a dialog there through a leave animation
 * that never finishes where there are no transitions, and marks it as leaving for the duration.
 */
function listIsOpen(): boolean {
  const dialog = document.querySelector('.ant-modal');
  return dialog !== null && !dialog.className.includes('-leave');
}

function Where() {
  const location = useLocation();
  return <div data-testid="where">{location.pathname + location.search}</div>;
}

function shell(accountId: string | null = ANA) {
  return render(
    <MemoryRouter initialEntries={['/map?layer=caves']}>
      <App>
        <HeldReportsSentinel accountId={accountId} />
        <Routes>
          <Route path="*" element={<Where />} />
        </Routes>
      </App>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  made = 0;
  window.localStorage.clear();
  refreshHeldReports();
  resetHeldReportsDrain();
  // The server is away for the whole of this file unless a test says otherwise, so that what is
  // held stays held and the count has something to count.
  sendHeld.mockReset().mockRejectedValue(new TypeError('Failed to fetch'));
  signInAgain.mockReset().mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  window.localStorage.clear();
});

describe('held reports in the page header', () => {
  it('draws nothing while nothing is held', () => {
    shell();

    expect(screen.queryByTestId('held-reports')).toBeNull();
  });

  it('counts what is held on every trip, lists every one of them and leads to the trip of each', async () => {
    held({ tripLogId: 'trip-7', composedAt: '2026-05-01T12:00:00.000Z' }, { note: 'cold at the sump' });
    const oldest = held(
      { tripLogId: 'trip-3', composedAt: '2026-05-01T09:00:00.000Z' },
      { caverIds: ['caver-1', 'caver-2'], kind: 'atDepth', depthM: 120 },
    );
    const newest = held({ tripLogId: 'trip-7', composedAt: '2026-05-01T12:30:00.000Z' });
    shell();
    await waitFor(() => expect(sendHeld).toHaveBeenCalled());

    const button = screen.getByTestId('held-reports');
    expect(button).toHaveAccessibleName(
      'Held reports: 3 — kept in this browser until they are sent. Show them.',
    );
    // Nothing is listed until it is asked for, and asking goes nowhere.
    expect(listIsOpen()).toBe(false);

    fireEvent.click(button);
    await waitFor(() => expect(listIsOpen()).toBe(true));

    const list = within(await screen.findByTestId('held-reports-list'));
    expect(screen.getByTestId('where')).toHaveTextContent('/map?layer=caves');
    // In words, where the text is.
    expect(list.getByText(/kept in this browser, for this account only/)).toBeInTheDocument();
    expect(list.getByText('cold at the sump')).toBeInTheDocument();
    // No roster here to name anybody from: counted, with what was reported.
    const row = within(list.getByTestId(`held-reports-${oldest.clientKey}`));
    expect(row.getByText('People: 2')).toBeInTheDocument();
    expect(row.getByText('At a depth')).toBeInTheDocument();
    expect(row.getByText('120 m')).toBeInTheDocument();

    fireEvent.click(list.getByTestId(`held-reports-trip-${newest.clientKey}`));

    expect(screen.getByTestId('where')).toHaveTextContent('/trip-logs/trip-7?tab=tracking');
    await waitFor(() => expect(listIsOpen()).toBe(false));
  });

  /**
   * A trip deleted while the phone had no signal answers as not found, and so does one its author
   * may no longer read. Its page then draws nothing of the trip — so this list, which needs no
   * trip, is the only place the report can be seen and thrown away from.
   */
  it('lets a report refused because its trip is gone be discarded, and then draws nothing', async () => {
    sendHeld.mockReset().mockRejectedValue(new ApiError(404, 'trip_log.not_found'));
    const gone = held({ tripLogId: 'trip-deleted' }, { note: 'said about somebody' });
    shell();
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ state: 'refused', sending: false }));
    // Refused, so nothing sends it again by itself — and nothing drops it either.
    expect(sendHeld).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByTestId('held-reports'));
    const list = within(await screen.findByTestId('held-reports-list'));
    expect(list.getByTestId(`held-reports-refused-${gone.clientKey}`)).toHaveTextContent(/^Refused: /);
    // Every report here is refused: nothing is waiting for "send now".
    expect(list.queryByTestId('held-reports-send')).toBeNull();

    fireEvent.click(list.getByTestId(`held-reports-discard-${gone.clientKey}`));
    expect(await screen.findByText('Discard this report?')).toBeInTheDocument();
    expect(heldReportsOf(ANA)).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(Object.keys(window.localStorage)).toEqual([]);
    expect(screen.queryByTestId('held-reports')).toBeNull();
    expect(screen.queryByText('said about somebody')).toBeNull();
    expect(sendHeld).toHaveBeenCalledTimes(1);
  });

  it('sends a refused report again from the list when asked', async () => {
    sendHeld.mockReset().mockRejectedValueOnce(new ApiError(409, 'tracking.not_writable'));
    const refused = held();
    shell();
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ state: 'refused', sending: false }));
    sendHeld.mockResolvedValue([{}]);

    fireEvent.click(screen.getByTestId('held-reports'));
    fireEvent.click(await screen.findByTestId(`held-reports-again-${refused.clientKey}`));

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sendHeld).toHaveBeenCalledTimes(2);
  });

  it('sends what is waiting on Send now, and says why nothing left', async () => {
    held();
    shell();
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));

    fireEvent.click(screen.getByTestId('held-reports'));
    fireEvent.click(await screen.findByTestId('held-reports-send'));

    expect(await screen.findByText(/Still no answer from the server/)).toBeInTheDocument();
    expect(sendHeld).toHaveBeenCalledTimes(2);
  });

  /**
   * A sign-in that is over is not the server being away: no amount of waiting sends these. The
   * list says so and offers the one thing that mends it, which the kept reports survive.
   */
  it('says the reports wait for a new sign-in, and offers it back to this very page', async () => {
    sendHeld.mockReset().mockRejectedValue(new ApiError(401));
    held();
    shell();
    // Said once without anything being pressed: their author was told they leave by themselves.
    expect(
      await screen.findByText(/Your sign-in has lapsed, so the held reports cannot be sent/),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('held-reports'));
    const lapsed = within(await screen.findByTestId('held-reports-lapsed'));
    expect(lapsed.getByText(/cannot be sent until you have signed in again/)).toBeInTheDocument();
    expect(heldReportsOf(ANA)).toHaveLength(1);

    fireEvent.click(lapsed.getByTestId('held-reports-sign-in'));

    expect(signInAgain).toHaveBeenCalledWith('/map?layer=caves');
    // Still kept: the redirect takes the page's memory, not the browser's storage.
    expect(Object.keys(window.localStorage)).toHaveLength(1);
  });

  it('does not speak of a sign-in while the server is merely away', async () => {
    held();
    shell();
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));

    fireEvent.click(screen.getByTestId('held-reports'));

    await screen.findByTestId('held-reports-list');
    expect(screen.queryByTestId('held-reports-lapsed')).toBeNull();
  });

  it('is not found open again by a report held after the list emptied', async () => {
    const first = held();
    shell();
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ attempts: 2, sending: false }));
    fireEvent.click(screen.getByTestId('held-reports'));
    fireEvent.click(await screen.findByTestId(`held-reports-discard-${first.clientKey}`));
    fireEvent.click(await screen.findByRole('button', { name: 'Discard' }));
    await waitFor(() => expect(screen.queryByTestId('held-reports')).toBeNull());

    held();

    expect(await screen.findByTestId('held-reports')).toBeInTheDocument();
    expect(listIsOpen()).toBe(false);
  });

  it('sends what is held when it is mounted, whatever page is on screen, and then draws nothing', async () => {
    sendHeld.mockReset().mockResolvedValue([{}]);
    held({ tripLogId: 'trip-9' });

    shell();

    await waitFor(() => expect(heldReportsOf(ANA)).toEqual([]));
    expect(sendHeld).toHaveBeenCalledWith(expect.objectContaining({ tripLogId: 'trip-9', clientKey: 'key-1' }));
    expect(screen.queryByTestId('held-reports')).toBeNull();
    expect(screen.getByTestId('where')).toHaveTextContent('/map?layer=caves');
  });

  /**
   * The same storage and the same component: counted for the account that composed it, and not
   * there for another — nor sent by it.
   */
  it('neither counts nor sends what another account composed', async () => {
    held();

    const asAna = shell(ANA);
    expect(await screen.findByTestId('held-reports')).toBeInTheDocument();
    await waitFor(() => expect(sendHeld).toHaveBeenCalledTimes(1));
    asAna.unmount();
    sendHeld.mockClear();

    shell('account-bogdan');
    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.queryByTestId('held-reports')).toBeNull();
    expect(sendHeld).not.toHaveBeenCalled();
    expect(heldReportsOf(ANA)).toHaveLength(1);
  });
});
