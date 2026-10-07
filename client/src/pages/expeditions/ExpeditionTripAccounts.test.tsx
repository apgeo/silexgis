// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo, TripType } from '../../api/hooks.ts';

const { tripsSpy } = vi.hoisted(() => ({ tripsSpy: vi.fn() }));

const SURVEY: Partial<TripType> = {
  id: 7,
  fieldDataSchema: JSON.stringify({
    type: 'object',
    properties: { water_seen: { type: 'string', title: 'Water seen' } },
  }),
  logisticsSchema: null,
  safetySchema: JSON.stringify({
    type: 'object',
    properties: { what_happened: { type: 'string', title: 'What happened' } },
  }),
};

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: (...args: unknown[]) => tripsSpy(...args),
  useTripTypes: () => ({ data: [SURVEY] }),
  useCavers: () => ({ data: [] }),
}));

const { default: ExpeditionTripAccounts } = await import('./ExpeditionTripAccounts.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

function trip(overrides: Partial<TripLogInfo>): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Down the shaft',
    tripDate: '2026-07-19',
    tripDateEnd: null,
    tripTypeId: 7,
    description: null,
    results: null,
    fieldData: {},
    logistics: {},
    safety: null,
    ...overrides,
  } as unknown as TripLogInfo;
}

function answer(items: TripLogInfo[]) {
  tripsSpy.mockReturnValue({
    data: { items, page: 1, pageSize: 50, totalItems: items.length },
    isPending: false,
  });
}

afterEach(cleanup);
beforeEach(() => tripsSpy.mockReset());

describe('ExpeditionTripAccounts', () => {
  it('prints what each trip wrote about itself, under the trip, in the order of the days', () => {
    answer([
      trip({
        id: 'later',
        title: 'Sump push',
        tripDate: '2026-07-21',
        description: 'The sump was open.',
        results: 'Sixty metres beyond it.',
        fieldData: { water_seen: 'Lower than in May' },
      }),
      trip({ id: 'earlier', title: 'Rigging day', tripDate: '2026-07-19', description: 'Rigged the entrance.' }),
    ]);

    render(<ExpeditionTripAccounts expeditionId={CAMP} />);

    const part = screen.getByTestId('expedition-report-accounts');
    expect(within(part).getByText('What each trip wrote')).toBeTruthy();
    const headings = within(part)
      .getAllByRole('heading', { level: 5 })
      .map((heading) => heading.textContent);
    expect(headings[0]).toContain('Rigging day');
    expect(headings[1]).toContain('Sump push');

    const later = screen.getByTestId('expedition-report-account-later');
    expect(within(later).getByText('The sump was open.')).toBeTruthy();
    expect(within(later).getByText('Sixty metres beyond it.')).toBeTruthy();
    // Under the name the form gave the question.
    expect(within(later).getByText('Water seen')).toBeTruthy();
    expect(within(later).getByText('Lower than in May')).toBeTruthy();
  });

  it('asks for the camp trips the list of trips asks for, so the two share one answer', () => {
    answer([]);
    render(<ExpeditionTripAccounts expeditionId={CAMP} />);
    expect(tripsSpy).toHaveBeenCalledWith({ expeditionId: CAMP, page: 1, pageSize: 50 });
  });

  it('prints the account of what went wrong only where the trip arrived with it', () => {
    answer([
      trip({
        id: 'mine',
        title: 'Third pitch',
        description: 'Turned round at the third pitch.',
        safety: { what_happened: 'Ran out of light.' },
      }),
      trip({
        id: 'theirs',
        title: 'Traverse day',
        tripDate: '2026-07-20',
        description: 'Crossed the traverse.',
        // Withheld by the server: nothing at all, not an empty object.
        safety: null,
      }),
    ]);

    render(<ExpeditionTripAccounts expeditionId={CAMP} />);

    const mine = screen.getByTestId('expedition-report-account-mine');
    expect(within(mine).getByText('Ran out of light.')).toBeTruthy();
    expect(within(mine).getByText('Safety')).toBeTruthy();

    const theirs = screen.getByTestId('expedition-report-account-theirs');
    expect(within(theirs).getByText('Crossed the traverse.')).toBeTruthy();
    expect(within(theirs).queryByText('Safety')).toBeNull();
    expect(within(theirs).queryByText('What happened')).toBeNull();
  });

  it('gives a trip that wrote nothing no heading of its own', () => {
    answer([
      trip({ id: 'said', title: 'Survey day', description: 'Closed the loop.' }),
      trip({ id: 'silent', title: 'Carry day', tripDate: '2026-07-20' }),
    ]);

    render(<ExpeditionTripAccounts expeditionId={CAMP} />);

    expect(screen.getByTestId('expedition-report-account-said')).toBeTruthy();
    expect(screen.queryByTestId('expedition-report-account-silent')).toBeNull();
    expect(screen.queryByText(/Carry day/)).toBeNull();
  });

  it('shows no part at all when none of the trips wrote anything', () => {
    answer([trip({ id: 'silent', title: 'Carry day' })]);

    const { container } = render(<ExpeditionTripAccounts expeditionId={CAMP} />);

    expect(container.firstChild).toBeNull();
  });
});
