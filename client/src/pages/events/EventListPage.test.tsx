// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import EventListPage from './EventListPage.tsx';
import { expectEveryFieldNamed } from '../../testFieldNames.ts';

const { eventsSpy, canSpy } = vi.hoisted(() => ({
  eventsSpy: vi.fn(),
  canSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useEvents: (params: unknown) => eventsSpy(params),
  useCan: () => canSpy(),
}));

vi.mock('./EventFormModal.tsx', () => ({ default: () => null }));

function renderList(address: string) {
  return render(
    <App>
      <MemoryRouter initialEntries={[address]}>
        <Routes>
          <Route path="/events" element={<EventListPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  canSpy.mockReturnValue(false);
  eventsSpy.mockReturnValue({
    data: { items: [], page: 1, pageSize: 20, totalItems: 0 },
    isFetching: false,
  });
});

/**
 * A run of a repeating event is not a record of its own — it is the ordinary events sharing one
 * grouping key. So "show me the rest of this run" is this list, narrowed by that key, and the
 * narrowing arrives in the address because a link to a run has to survive being copied and
 * reloaded.
 */
describe('the occurrences of one repeating event', () => {
  it('asks the server for one run when the address names one, and says so on the page', async () => {
    renderList('/events?seriesId=series-1');

    await waitFor(() =>
      expect(eventsSpy).toHaveBeenCalledWith(expect.objectContaining({ seriesId: 'series-1' })),
    );
    expect(screen.getByTestId('event-series-filter')).toBeTruthy();
  });

  /**
   * The positive half of the pair: an ordinary visit narrows by nothing and says nothing, so what
   * the test above proves is the address being read and not a filter that is always on.
   */
  it('narrows by no run and shows no such label when the address names none', async () => {
    renderList('/events');

    await waitFor(() => expect(eventsSpy).toHaveBeenCalled());
    expect(eventsSpy.mock.calls.at(-1)?.[0]).toMatchObject({ seriesId: undefined });
    expect(screen.queryByTestId('event-series-filter')).toBeNull();
  });

  /**
   * And the narrowing can be let go of. A list showing a fraction of the events with no way back
   * to all of them reads as a list that has lost most of its rows.
   */
  it('drops the narrowing when the label is closed', async () => {
    renderList('/events?seriesId=series-1');

    fireEvent.click(screen.getByTestId('event-series-filter').querySelector('.anticon-close')!);

    await waitFor(() =>
      expect(eventsSpy.mock.calls.at(-1)?.[0]).toMatchObject({ seriesId: undefined }),
    );
    expect(screen.queryByTestId('event-series-filter')).toBeNull();
  });
});

describe('a list of events that could not be read', () => {
  it('says the events could not be read and offers to ask again, rather than drawing an empty table', () => {
    const refetch = vi.fn();
    eventsSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true, refetch });
    renderList('/events');

    expect(screen.getByTestId('event-list-unavailable').textContent).toContain(
      i18n.t('events.listUnavailable'),
    );
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(refetch).toHaveBeenCalled();
  });
});

describe('the filter row for a screen reader', () => {
  it('names every field, the kind and state filters among them', () => {
    const { container } = renderList('/events');

    expectEveryFieldNamed(container);
  });
});
