// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { HeldReport } from './trackingOutbox.ts';

const sendHeld = vi.fn();
vi.mock('../../api/hooks.ts', () => ({
  useSendHeldTrackingReport: () => sendHeld,
}));

// Who the tab is signed in as. Stood in for rather than signed in: the sign-in lives in memory
// behind a redirect to the server, and what is under test is whose reports a surface shows.
let account: string | null = 'account-ana';
vi.mock('../../auth/accountId.ts', () => ({
  useSignedInAccountId: () => account,
}));

const { default: TrackingHeldReports } = await import('./TrackingHeldReports.tsx');
const { holdReport, heldReportsOf, refreshHeldReports } = await import('./trackingOutbox.ts');
const { resetHeldReportsDrain } = await import('./trackingOutboxDrain.ts');

const ANA = 'account-ana';
const BOGDAN = 'account-bogdan';
const NAMES: Record<string, string> = { 'caver-1': 'Ion Pop', 'caver-2': 'Maria Dinu' };

let made = 0;
function held(over: Partial<HeldReport> = {}, body: Partial<HeldReport['body']> = {}): HeldReport {
  made += 1;
  const entry: HeldReport = {
    clientKey: `key-${made}`,
    accountId: ANA,
    tripLogId: 'trip-1',
    composedAt: `2026-05-01T10:00:0${made}.000Z`,
    state: 'held',
    problemCode: null,
    attempts: 1,
    ...over,
    body: {
      caverIds: ['caver-1', 'caver-2'],
      kind: 'atStation',
      stationName: 'p.g.42',
      depthM: null,
      teamId: null,
      note: 'waiting at the pitch head',
      recordedAt: '2026-05-01T10:15:00.000Z',
      ...body,
    },
  };
  act(() => {
    holdReport(entry);
  });
  return entry;
}

function show(tripLogId = 'trip-1') {
  return render(
    <App>
      <TrackingHeldReports
        tripLogId={tripLogId}
        nameOf={(caverId) => NAMES[caverId] ?? 'Somebody'}
        when={(value) => (value ? `at ${value.slice(11, 16)}` : '—')}
        controlSize="small"
      />
    </App>,
  );
}

const open = () => fireEvent.click(screen.getByTestId('trip-tracking-outbox-open'));

beforeEach(() => {
  made = 0;
  account = ANA;
  window.localStorage.clear();
  refreshHeldReports();
  resetHeldReportsDrain();
  sendHeld.mockReset().mockResolvedValue([{}]);
});

afterEach(() => {
  cleanup();
  window.localStorage.clear();
});

describe('the held reports above the report card', () => {
  it('is not there at all while nothing is held', () => {
    show();

    expect(screen.queryByTestId('trip-tracking-outbox')).toBeNull();
  });

  it('counts this trip’s held reports and says in words where their text is kept', () => {
    held();
    held();
    held({ tripLogId: 'trip-2' });

    show();

    expect(screen.getByTestId('trip-tracking-outbox-count')).toHaveTextContent('Held reports: 2');
    expect(screen.getByTestId('trip-tracking-outbox-kept')).toHaveTextContent(
      /kept in this browser, for this account only, until each one is sent or discarded/,
    );
  });

  it('opens to a list saying the hour, who and what of each', () => {
    const station = held();
    const depth = held({}, { kind: 'atDepth', stationName: null, depthM: 120, note: null, caverIds: ['caver-2'] });
    show();

    open();

    const first = within(screen.getByTestId(`trip-tracking-outbox-${station.clientKey}`));
    expect(first.getByText('at 10:15')).toBeInTheDocument();
    expect(first.getByText('Ion Pop, Maria Dinu')).toBeInTheDocument();
    expect(first.getByText('At a station')).toBeInTheDocument();
    expect(first.getByText('p.g.42')).toBeInTheDocument();
    expect(first.getByText('waiting at the pitch head')).toBeInTheDocument();
    const second = within(screen.getByTestId(`trip-tracking-outbox-${depth.clientKey}`));
    expect(second.getByText('Maria Dinu')).toBeInTheDocument();
    expect(second.getByText('At a depth')).toBeInTheDocument();
    expect(second.getByText('120 m')).toBeInTheDocument();
  });

  /**
   * The same storage, the same trip, the same component: signed in as the account that composed
   * them the reports are listed, and signed in as another they are not there — nor is the notice.
   */
  it('shows an account only what it composed itself', () => {
    held();
    held({ accountId: BOGDAN }, { note: 'somebody else’s words' });

    const asAna = show();
    open();
    expect(screen.getByTestId('trip-tracking-outbox-count')).toHaveTextContent('Held reports: 1');
    expect(screen.getByText('waiting at the pitch head')).toBeInTheDocument();
    expect(screen.queryByText('somebody else’s words')).toBeNull();
    asAna.unmount();

    account = BOGDAN;
    show();
    open();
    expect(screen.getByTestId('trip-tracking-outbox-count')).toHaveTextContent('Held reports: 1');
    expect(screen.getByText('somebody else’s words')).toBeInTheDocument();
    expect(screen.queryByText('waiting at the pitch head')).toBeNull();
  });

  it('shows nothing to a tab signed in as nobody it can name', () => {
    held();
    account = null;

    show();

    expect(screen.queryByTestId('trip-tracking-outbox')).toBeNull();
  });

  it('sends on Send now, and is gone once everything has landed', async () => {
    const one = held();
    show();

    fireEvent.click(screen.getByTestId('trip-tracking-outbox-send'));

    await waitFor(() => expect(screen.queryByTestId('trip-tracking-outbox')).toBeNull());
    expect(sendHeld).toHaveBeenCalledWith({
      tripLogId: 'trip-1',
      ...one.body,
      clientKey: one.clientKey,
    });
    expect(heldReportsOf(ANA)).toEqual([]);
  });

  it('keeps the report and says why nothing left when Send now gets no answer', async () => {
    sendHeld.mockRejectedValue(new TypeError('Failed to fetch'));
    held();
    show();

    fireEvent.click(screen.getByTestId('trip-tracking-outbox-send'));

    expect(await screen.findByText(/Still no answer from the server/)).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-outbox-count')).toHaveTextContent('Held reports: 1');
  });

  it('keeps a refused report with the server’s reason in words, and sends it again only when asked', async () => {
    sendHeld.mockRejectedValueOnce(new ApiError(409, 'tracking.not_writable'));
    const refused = held();
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-outbox-send'));
    await waitFor(() => expect(heldReportsOf(ANA)[0]).toMatchObject({ state: 'refused', sending: false }));

    open();
    expect(screen.getByTestId(`trip-tracking-outbox-refused-${refused.clientKey}`)).toHaveTextContent(
      `Refused: ${i18n.t('trips.tracking.problems.notWritable')}`,
    );
    // Every report here is refused: the button that sends what is waiting has nothing to send.
    expect(screen.queryByTestId('trip-tracking-outbox-send')).toBeNull();
    expect(sendHeld).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByTestId(`trip-tracking-outbox-again-${refused.clientKey}`));

    await waitFor(() => expect(screen.queryByTestId('trip-tracking-outbox')).toBeNull());
    expect(sendHeld).toHaveBeenCalledTimes(2);
  });

  it('words a refusal the server gave no known reason for', () => {
    const refused = held({ state: 'refused', problemCode: 'something.nobody.words' });
    show();
    open();

    expect(screen.getByTestId(`trip-tracking-outbox-refused-${refused.clientKey}`)).toHaveTextContent(
      /^Refused: the server refused this report and named no reason/,
    );
  });

  it('discards a report only after a confirmation, and sends nothing', async () => {
    const kept = held();
    const thrownAway = held({}, { note: 'not this one after all' });
    show();
    open();

    fireEvent.click(screen.getByTestId(`trip-tracking-outbox-discard-${thrownAway.clientKey}`));
    // Asked first: the press alone removes nothing.
    expect(await screen.findByText(/^Discard this report\?/)).toBeInTheDocument();
    // What discarding does, and not a claim that the report never reached the server: a send
    // nobody answered may have been written all the same.
    expect(
      screen.getByText(/It may already have reached the server .* remove the report there if it is listed/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/never sent/)).toBeNull();
    expect(heldReportsOf(ANA)).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

    await waitFor(() => expect(heldReportsOf(ANA).map((entry) => entry.clientKey)).toEqual([kept.clientKey]));
    expect(
      await screen.findByText(/its copy in this browser is deleted and will not be sent again\. If it is on the trip's log/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Nothing was sent/)).toBeNull();
    expect(screen.queryByText('not this one after all')).toBeNull();
    expect(sendHeld).not.toHaveBeenCalled();
    expect(Object.keys(window.localStorage)).toEqual([`silexgis.trackingOutbox.${kept.clientKey}`]);
  });
});
