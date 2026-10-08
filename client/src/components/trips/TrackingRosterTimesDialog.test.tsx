// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TrackingRosterTimes, TrackingRosterTimesPerson } from '../../api/hooks.ts';

const ANA = '11111111-1111-1111-1111-111111111111';
const BOGDAN = '22222222-2222-2222-2222-222222222222';
const CARMEN = '33333333-3333-3333-3333-333333333333';
const DAN = '44444444-4444-4444-4444-444444444444';
const ELENA = '55555555-5555-5555-5555-555555555555';

const NAMES: Record<string, string> = {
  [ANA]: 'Ana Popescu',
  [BOGDAN]: 'Bogdan Ilie',
  [CARMEN]: 'Carmen Radu',
  [DAN]: 'Dan Marin',
  [ELENA]: 'Elena Stan',
};

const timesQuery = vi.fn();
const takeTimes = vi.fn();
const refetch = vi.fn();
const onClose = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useTrackingRosterTimes: (...asked: unknown[]) => timesQuery(...asked),
  useTakeTrackingRosterTimes: () => ({ mutateAsync: takeTimes, isPending: false }),
}));

// The reviewer's own zone, fixed: what the dialog asks the server in must not depend on the
// machine the test happens to run on.
vi.mock('./trackingCsvZones.ts', async (original) => ({
  ...(await original<typeof import('./trackingCsvZones.ts')>()),
  ownSheetZone: () => 'Europe/Bucharest',
  sheetZoneNames: () => ['Europe/Bucharest', 'Europe/London', 'UTC'],
}));

const { default: TrackingRosterTimesDialog } = await import('./TrackingRosterTimesDialog.tsx');

function person(caverId: string, overrides: Partial<TrackingRosterTimesPerson> = {}): TrackingRosterTimesPerson {
  return {
    caverId,
    onRoster: true,
    enteredAt: '2026-09-12T06:00:00Z',
    exitedAt: '2026-09-12T14:00:00Z',
    stays: 1,
    entry: '09:00:00',
    exit: '17:00:00',
    currentEntry: null,
    currentExit: null,
    changes: true,
    overwrites: false,
    problem: null,
    ...overrides,
  };
}

/** A party with one of each kind of row the dialog has to tell apart. */
function party(): TrackingRosterTimes {
  return {
    timeZone: 'Europe/Bucharest',
    people: [
      person(ANA),
      person(BOGDAN, { currentEntry: '08:15:00', currentExit: '16:30:00', overwrites: true }),
      person(CARMEN, { currentEntry: '09:00:00', currentExit: '17:00:00', changes: false }),
      person(DAN, { exitedAt: null, exit: null, changes: false, problem: 'noExit' }),
      person(ELENA, { onRoster: false, changes: false, problem: 'notOnRoster' }),
    ],
  };
}

function holding(data: TrackingRosterTimes | undefined, error: unknown = null) {
  timesQuery.mockReturnValue({ data, error, isPending: data === undefined && !error, isFetching: false, refetch });
}

function show() {
  return render(
    <App>
      <TrackingRosterTimesDialog tripLogId="trip-1" nameOf={(id) => NAMES[id] ?? 'somebody'} onClose={onClose} />
    </App>,
  );
}

const tick = (caverId: string) => screen.getByTestId(`roster-times-take-${caverId}`) as HTMLInputElement;

beforeEach(() => {
  timesQuery.mockReset();
  takeTimes.mockReset().mockResolvedValue({ people: 1, rows: 1, times: party() });
  refetch.mockReset();
  onClose.mockReset();
  holding(party());
});

afterEach(async () => {
  cleanup();
  await i18n.changeLanguage('en');
});

describe('taking roster times from the tracking log', () => {
  it('asks in the reviewer\'s own zone and states the zone the server applied', () => {
    show();

    expect(timesQuery).toHaveBeenCalledWith('trip-1', 'Europe/Bucharest');
    expect(screen.getByTestId('roster-times-zone-applied')).toHaveTextContent(
      'Read on the clocks of Europe/Bucharest.',
    );
  });

  /**
   * The rule the dialog exists for: nothing typed is replaced unless somebody ticks it. The
   * positive half is in the same table — the person with nothing typed starts ticked — so the
   * unticked row is not unticked merely because nothing is.
   */
  it('starts with a row ticked only where nothing somebody typed would be replaced', () => {
    show();

    expect(tick(ANA).checked).toBe(true);
    expect(tick(ANA).disabled).toBe(false);

    expect(tick(BOGDAN).checked).toBe(false);
    expect(tick(BOGDAN).disabled).toBe(false);
    expect(screen.getByTestId(`roster-times-overwrites-${BOGDAN}`)).toHaveTextContent(
      'Replaces a time somebody typed',
    );
    expect(screen.getByTestId(`roster-times-current-${BOGDAN}`)).toHaveTextContent('08:15 – 16:30');
    expect(screen.getByTestId(`roster-times-log-${BOGDAN}`)).toHaveTextContent('09:00 – 17:00');

    expect(screen.getByTestId('roster-times-write')).toHaveTextContent('Write to the roster: 1');
  });

  it('cannot tick a row that is already the same, has a problem, or has no roster row — and says which', () => {
    show();

    expect(tick(CARMEN).disabled).toBe(true);
    expect(tick(CARMEN).checked).toBe(false);
    expect(screen.getByTestId(`roster-times-same-${CARMEN}`)).toHaveTextContent('Already the same');

    expect(tick(DAN).disabled).toBe(true);
    expect(screen.getByTestId(`roster-times-problem-${DAN}`)).toHaveTextContent('No report says they came out.');

    // Somebody with reports and no row on the roster is in the table, named, and said to be off it.
    expect(screen.getByText('Elena Stan')).toBeInTheDocument();
    expect(tick(ELENA).disabled).toBe(true);
    expect(screen.getByTestId(`roster-times-problem-${ELENA}`)).toHaveTextContent('Not on the roster');
  });

  it('writes exactly the people ticked, each with the pair that was on screen', async () => {
    show();

    fireEvent.click(tick(BOGDAN));
    expect(screen.getByTestId('roster-times-write')).toHaveTextContent('Write to the roster: 2');
    fireEvent.click(tick(ANA));
    expect(screen.getByTestId('roster-times-write')).toHaveTextContent('Write to the roster: 1');
    expect(takeTimes).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('roster-times-write'));

    await waitFor(() =>
      expect(takeTimes).toHaveBeenCalledWith({
        tripLogId: 'trip-1',
        timeZone: 'Europe/Bucharest',
        // With what the roster was shown to hold, and that it is an overwrite: the server
        // checks both against the roster as it is when the write arrives.
        people: [
          {
            caverId: BOGDAN,
            entry: '09:00:00',
            exit: '17:00:00',
            currentEntry: '08:15:00',
            currentExit: '16:30:00',
            overwrites: true,
          },
        ],
      }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('offers nothing to write when nobody is ticked', () => {
    holding({ timeZone: 'Europe/Bucharest', people: [person(CARMEN, { changes: false })] });
    show();

    expect(screen.getByTestId('roster-times-write')).toBeDisabled();
  });

  it('says why a write was refused, reads the times again and stays open', async () => {
    takeTimes.mockRejectedValue(new ApiError(409, 'tracking.roster_times_changed'));
    show();

    fireEvent.click(screen.getByTestId('roster-times-write'));

    expect(await screen.findByText(/Nothing was written\. The log or the roster changed/)).toBeInTheDocument();
    expect(refetch).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('says so when the times cannot be read, in the words of the refusal', () => {
    holding(undefined, new ApiError(400, 'tracking.roster_times_zone_unknown'));
    show();

    expect(screen.getByTestId('roster-times-unavailable')).toHaveTextContent(
      'This server does not know that time zone',
    );
    expect(screen.queryByTestId('roster-times-table')).toBeNull();
    expect(screen.getByTestId('roster-times-write')).toBeDisabled();
  });

  it('has every sentence in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    show();

    expect(screen.getByText('Orele de intrare și de ieșire din jurnalul de urmărire')).toBeInTheDocument();
    expect(screen.getByTestId(`roster-times-overwrites-${BOGDAN}`)).toHaveTextContent(
      'Înlocuiește o oră scrisă de cineva',
    );
    expect(screen.getByTestId(`roster-times-problem-${ELENA}`)).toHaveTextContent('Nu este pe listă');
    expect(screen.getByTestId('roster-times-write')).toHaveTextContent('Scrie pe listă: 1');
  });
});
