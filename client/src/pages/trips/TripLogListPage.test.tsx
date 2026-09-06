// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripListFacets, TripLogInfo, TripLogListParams } from '../../api/hooks.ts';

const { listSpy, facetSpy, groupSpy, downloadSpy } = vi.hoisted(() => ({
  listSpy: vi.fn(),
  facetSpy: vi.fn(),
  groupSpy: vi.fn(),
  downloadSpy: vi.fn((_url: string) => Promise.resolve()),
}));

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: (params: TripLogListParams) => listSpy(params),
  useTripLogFacets: (params: unknown) => facetSpy(params),
  useTripLogGrouping: (params: unknown, enabled: boolean) => groupSpy(params, enabled),
  useTripTypes: () => ({ data: [{ id: 3, code: 'survey', name: 'Survey' }] }),
  useCan: () => false,
}));

// Only the fetch is stood in for. The URL builder is the real one, because what this asserts is
// which parameters reach the export route — a stand-in for it would be asserting the stand-in.
vi.mock('../../api/download.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/download.ts')>()),
  downloadFile: (url: string) => downloadSpy(url),
}));

const { default: TripLogListPage } = await import('./TripLogListPage.tsx');

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    tripDate: '2026-09-05',
    tripDateEnd: null,
    tripTypeId: 3,
    locationText: null,
    state: 'done',
    visibility: 'private',
    participants: [],
    ...overrides,
  } as unknown as TripLogInfo;
}

function facets(overrides: Partial<TripListFacets> = {}): TripListFacets {
  return {
    matching: 4,
    overall: 12,
    types: [{ value: '3', label: null, count: 4 }],
    states: [{ value: 'done', label: null, count: 4 }],
    visibilities: [{ value: 'private', label: null, count: 4 }],
    incident: [{ value: 'true', label: null, count: 1 }],
    participants: [{ value: 'p1', label: 'Ana Pop', count: 3 }],
    areas: [{ value: 'a1', label: 'Padis', count: 2 }],
    ...overrides,
  };
}

/** What the page last asked the listing for. */
const lastList = () => listSpy.mock.calls.at(-1)![0] as TripLogListParams;
/** What the page last asked for a shape of, and whether it asked at all. */
const lastGrouping = () => groupSpy.mock.calls.at(-1)! as [Record<string, unknown>, boolean];
/** What the page last asked the counts about. */
const lastFacets = () => facetSpy.mock.calls.at(-1)![0] as Record<string, unknown>;

/**
 * The words one control offers. antd builds a select's list only once it is opened, so a test
 * that never opens one is testing the closed control and not the options behind it.
 */
function open(testId: string): string {
  const control = screen.getByTestId(testId);
  fireEvent.mouseDown(control.querySelector('.ant-select-selector') ?? control);
  return document.body.textContent ?? '';
}

/**
 * The address, rendered, so a test can read where a navigation went. The router keeps it and the
 * page does not, and a filter that lives in the address is only testable by reading it back.
 */
function Address() {
  const location = useLocation();
  return <span data-testid="trip-list-address">{`${location.pathname}${location.search}`}</span>;
}

function show(address = '/trip-logs') {
  return render(
    <MemoryRouter initialEntries={[address]}>
      <TripLogListPage />
      <Address />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  facetSpy.mockReset();
  groupSpy.mockReset();
  downloadSpy.mockClear();
  groupSpy.mockReturnValue({ data: undefined });
  listSpy.mockReturnValue({
    data: { items: [trip()], page: 1, pageSize: 20, totalItems: 4 },
    isFetching: false,
    isError: false,
  });
  facetSpy.mockReturnValue({ data: facets() });
});

describe('the trip listing', () => {
  it('takes its whole filter from the address, so a narrowed listing is a link', () => {
    show('/trip-logs?q=coiba&types=3&states=done&hadIncident=true&participantIds=p1,p2&sort=title');

    expect(lastList()).toMatchObject({
      search: 'coiba',
      types: '3',
      states: 'done',
      hadIncident: true,
      participantIds: 'p1,p2',
      sort: 'title',
      page: 1,
    });
  });

  it('asks the counts about the same narrowings as the page, minus paging and order', () => {
    // This is what keeps the number beside an option and the rows that option produces two
    // readings of one request rather than two requests that happen to look alike.
    show('/trip-logs?q=coiba&types=3&page=2&sort=title');

    const asked = lastFacets();
    expect(asked).toMatchObject({ search: 'coiba', types: '3' });
    expect('page' in asked).toBe(false);
    expect('sort' in asked).toBe(false);
  });

  it('says how many trips the filter leaves out of how many the reader may read', () => {
    show('/trip-logs?types=3');

    expect(screen.getByTestId('trip-list-count').textContent).toContain('4');
    expect(screen.getByTestId('trip-list-count').textContent).toContain('12');
  });

  it('offers every option with what it would leave, translated where the client has the words', () => {
    show();

    // The trip type's name comes from the vocabulary this client already holds; the person's
    // comes with the count, because a roster is not a vocabulary the client has.
    expect(open('trip-facet-types')).toContain('Survey / mapping (4)');
    expect(open('trip-facet-states')).toContain('Done (4)');
    expect(open('trip-facet-visibilities')).toContain('Private (4)');
    expect(open('trip-facet-participant')).toContain('Ana Pop (3)');
    expect(open('trip-facet-area')).toContain('Padis (2)');
  });

  it('keeps a chosen value the counts no longer reach, shown as leaving none', () => {
    // Otherwise the other facets excluding somebody's own choice would leave the control showing
    // a raw identifier, with no way to let go of it.
    facetSpy.mockReturnValue({ data: facets({ participants: [] }) });
    show('/trip-logs?participantIds=p1');

    expect(open('trip-facet-participant')).toContain('p1 (0)');
  });

  it('offers a reset only while something is narrowing, and clears the address', () => {
    const { unmount } = show();
    expect(screen.queryByTestId('trip-list-count-reset')).toBeNull();
    unmount();

    show('/trip-logs?types=3&participantIds=p1&pageSize=50');
    fireEvent.click(screen.getByTestId('trip-list-count-reset'));

    const asked = lastList();
    expect(asked.types).toBeUndefined();
    expect(asked.participantIds).toBeUndefined();
    // Where the reader was standing is not a narrowing and is not thrown away with them.
    expect(asked.pageSize).toBe(50);
  });

  it('hands the order to the server rather than rearranging the page it was given', () => {
    show('/trip-logs?sort=title');

    expect(lastList().sort).toBe('title');
    // Only the server can order a listing: sorting the rows already handed back would reorder
    // one page, which is a different answer as soon as there is more than one.
    expect(listSpy).toHaveBeenCalled();
  });

  it('tells a reader whose filter found nothing apart from one whose list is empty', () => {
    listSpy.mockReturnValue({
      data: { items: [], page: 1, pageSize: 20, totalItems: 0 },
      isFetching: false,
      isError: false,
    });
    const { unmount } = show('/trip-logs?types=3');
    expect(screen.getByTestId('trip-list-empty').textContent).toContain('match this filter');
    unmount();

    show();
    expect(screen.getByTestId('trip-list-empty').textContent).toContain('No trips yet');
  });

  it('says a request failed rather than that there is nothing to see', () => {
    listSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    show();

    expect(screen.getByTestId('trip-list-empty').textContent).toContain('could not be loaded');
  });
});

describe('the shape above the table', () => {
  it('asks for no slices until somebody asks to be shown some', () => {
    show('/trip-logs');
    expect(lastGrouping()[1]).toBe(false);
  });

  it('takes the grouping from the address and cuts it from the same narrowed set', () => {
    show('/trip-logs?q=coiba&states=done&groupBy=participant&thenBy=year');

    const [params, enabled] = lastGrouping();
    expect(enabled).toBe(true);
    expect(params).toMatchObject({
      search: 'coiba',
      states: 'done',
      groupBy: 'participant',
      thenBy: 'year',
    });
    // Which page somebody is standing on decides no slice, so it is not asked about.
    expect(params.page).toBeUndefined();
    expect(params.pageSize).toBeUndefined();
  });

  it('says the totals exceed the trips when a trip counts into every value it holds', () => {
    groupSpy.mockReturnValue({
      data: {
        groupBy: 'participant',
        thenBy: 'none',
        matching: 4,
        overlapping: true,
        truncated: false,
        groups: [
          {
            value: 'p1',
            label: 'Ana Pop',
            count: 3,
            firstDay: '2026-01-02',
            lastDay: '2026-03-04',
            topTypes: [{ value: '3', label: null, count: 3 }],
            topPeople: [{ value: 'p1', label: 'Ana Pop', count: 3 }],
            groups: [],
          },
        ],
      },
    });
    show('/trip-logs?groupBy=participant');

    expect(screen.getByTestId('trip-grouping-overlap')).toBeTruthy();
    // The count travels with every name, which is the figure a list of bare names throws away.
    expect(screen.getByTestId('trip-grouping-panel').textContent).toContain('Ana Pop (3)');
  });
});

describe('taking the listing away', () => {
  it('exports the filter and not the page', () => {
    show('/trip-logs?q=coiba&states=done&page=3');
    fireEvent.click(screen.getByTestId('trip-list-export'));

    const url = downloadSpy.mock.calls.at(-1)![0];
    expect(url).toContain('search=coiba');
    expect(url).toContain('states=done');
    // The page and its size are where the reader is standing, and a file has no page four.
    expect(url).not.toContain('page=');
    expect(url).not.toContain('pageSize=');
  });

  it('hands the narrowing to the map and nothing about where the reader was standing', () => {
    show('/trip-logs?q=coiba&states=done&page=3&sort=title&groupBy=year');
    fireEvent.click(screen.getByTestId('trip-list-show-on-map'));

    const address = screen.getByTestId('trip-list-address').textContent ?? '';
    expect(address).toContain('/map');
    expect(address).toContain('states=done');
    expect(address).toContain('trips=1');
    expect(address).not.toContain('sort=');
    expect(address).not.toContain('groupBy=');
  });
});
