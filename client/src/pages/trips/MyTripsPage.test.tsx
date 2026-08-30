// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { MyTripLogListParams, TripLogInfo } from '../../api/hooks.ts';

const { listSpy } = vi.hoisted(() => ({ listSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useMyTripLogs: (params: MyTripLogListParams) => listSpy(params),
  useTripTypes: () => ({ data: undefined }),
}));

const { default: MyTripsPage } = await import('./MyTripsPage.tsx');

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    tripDate: '2026-09-05',
    tripDateEnd: null,
    tripTypeId: null,
    locationText: null,
    state: 'planned',
    visibility: 'private',
    participants: [],
    ...overrides,
  } as unknown as TripLogInfo;
}

/** What the page last asked the server for. */
function lastParams(): MyTripLogListParams {
  return listSpy.mock.calls.at(-1)![0] as MyTripLogListParams;
}

function show() {
  return render(
    <MemoryRouter>
      <MyTripsPage />
    </MemoryRouter>,
  );
}

function answer(items: TripLogInfo[], totalItems = items.length) {
  listSpy.mockReturnValue({
    data: { items, page: 1, pageSize: 20, totalItems },
    isFetching: false,
    isError: false,
  });
}

/** A request that never answered: no data, and nothing known about the reader's diary. */
function fails() {
  listSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  answer([trip()]);
});

describe('my trips', () => {
  it('lists a trip with its days and where the plan has got to', () => {
    show();

    expect(screen.getByText('Coiba Mare recce')).toBeTruthy();
    expect(screen.getByTestId('trip-state').textContent).toContain('Planned');
  });

  /**
   * The figure the listing carries is drawn, because a listing is where somebody scans what is
   * coming and asks how ready each one is. A trip whose purpose names no list, or whose list this
   * reader may not see, carries no figure and gets no badge — a zero there would say a list
   * exists, which is the one thing the server declines to say.
   */
  it('shows how much of the checklist is settled, and nothing where there is no figure', () => {
    answer([
      trip({
        id: 'aaaaaaaa-0000-0000-0000-000000000001',
        title: 'Half ready',
        checklistReadiness: {
          checklistId: 'bbbbbbbb-0000-0000-0000-000000000001',
          ticked: 1,
          total: 3,
        },
      }),
      trip({
        id: 'aaaaaaaa-0000-0000-0000-000000000002',
        title: 'No list to speak of',
        checklistReadiness: null,
      }),
    ]);
    show();

    const badges = screen.getAllByTestId('trip-readiness');
    expect(badges).toHaveLength(1);
    expect(badges[0].textContent).toContain('1 of 3 settled');
  });

  /**
   * The order is the server's. It is ascending — soonest first, which is the opposite of every
   * other trip listing — and the page must render the rows in the order they arrived rather than
   * sorting them again: re-sorting would only reorder the page in hand, which is a different and
   * wrong answer the moment there is more than one page of them.
   */
  it('renders the rows in the order the server sent them, soonest first', () => {
    answer([
      trip({ id: 'aaaaaaaa-0000-0000-0000-000000000001', title: 'This weekend', tripDate: '2026-09-05' }),
      trip({ id: 'aaaaaaaa-0000-0000-0000-000000000002', title: 'Next month', tripDate: '2026-10-11' }),
      trip({ id: 'aaaaaaaa-0000-0000-0000-000000000003', title: 'Christmas', tripDate: '2026-12-27' }),
    ]);
    show();

    const titles = screen
      .getAllByRole('row')
      .map((row) => row.textContent ?? '')
      .filter((text) => text.includes('202') || /weekend|month|Christmas/.test(text));

    expect(titles[0]).toContain('This weekend');
    expect(titles[1]).toContain('Next month');
    expect(titles[2]).toContain('Christmas');
  });

  /**
   * The page sends no identifier of any kind, and there is no control on it that could name a
   * person: whose trips these are is worked out on the server from whoever is asking. A parameter
   * for it would let somebody assemble where a named person has been out of trips they may never
   * open, so this assertion is about a refusal rather than about a default.
   */
  it('asks for a page and names nobody, until somebody narrows it', () => {
    show();

    expect(lastParams()).toEqual({ page: 1, pageSize: 20 });
  });

  /**
   * Narrowing goes to the server. Filtering the page of rows already in hand would silently
   * answer a different question — "which of these twenty" rather than "which of mine" — and the
   * count under the table would go on describing the unnarrowed list.
   */
  it('sends the state to the server rather than filtering the rows in hand', () => {
    show();

    fireEvent.mouseDown(within(screen.getByTestId('my-trips-state-filter')).getByRole('combobox'));
    fireEvent.click(screen.getByText('Confirmed'));

    expect(lastParams().state).toBe('confirmed');
  });

  it('goes back to the first page whenever the list is narrowed', () => {
    answer([trip()], 60);
    show();

    fireEvent.click(screen.getByTitle('2'));
    expect(lastParams().page).toBe(2);

    fireEvent.mouseDown(within(screen.getByTestId('my-trips-state-filter')).getByRole('combobox'));
    fireEvent.click(screen.getByText('Done'));

    expect(lastParams().page).toBe(1);
  });

  /**
   * Being on no trips is the ordinary state of a new account rather than a fault or a search that
   * found nothing, so the empty table says what would put a trip here.
   */
  it('tells somebody on no trips what would put one here', () => {
    answer([]);
    show();

    expect(screen.getByTestId('my-trips-empty').textContent).toContain('invites you');
  });

  /**
   * A filter that excluded everything is not an empty diary. Saying "you are not on any trip yet"
   * to somebody on twelve trips who asked for the cancelled ones would be flatly untrue, and it
   * hides the fact that the filter is what is doing it.
   */
  it('says the filters matched nothing rather than claiming the reader is on no trips', () => {
    answer([]);
    show();

    fireEvent.mouseDown(within(screen.getByTestId('my-trips-state-filter')).getByRole('combobox'));
    fireEvent.click(screen.getByText('Cancelled'));

    const empty = screen.getByTestId('my-trips-empty').textContent ?? '';
    expect(empty).toContain('these filters');
    expect(empty).not.toContain('invites you');
  });

  /**
   * Unknown, not empty. A request that never answered has not earned a claim about the reader's
   * diary, and reporting one as the other is the wrong direction to fail in for a listing whose
   * sibling feature is the overdue callout.
   */
  it('reports trips it could not read instead of an empty diary', () => {
    fails();
    show();

    const empty = screen.getByTestId('my-trips-empty').textContent ?? '';
    expect(empty).toContain('could not be loaded');
    expect(empty).not.toContain('invites you');
  });
});
