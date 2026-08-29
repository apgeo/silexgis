// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { EventInfo } from '../../api/hooks.ts';
import EventDetailPage from './EventDetailPage.tsx';

const EVENT = '33333333-4444-5555-6666-777777777777';

const { eventSpy, canSpy, deleteSpy, deleteFollowingSpy } = vi.hoisted(() => ({
  eventSpy: vi.fn(),
  canSpy: vi.fn(),
  deleteSpy: vi.fn(),
  deleteFollowingSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useEvent: () => eventSpy(),
  useEffectiveAccess: () => ({ data: undefined }),
  useCan: () => canSpy(),
  useDeleteEvent: () => ({ mutateAsync: deleteSpy, isPending: false }),
  useDeleteEventSeriesFollowing: () => ({ mutateAsync: deleteFollowingSpy, isPending: false }),
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

// Mounted by name rather than exercised: each has tests of its own and asks the server for
// something this page knows nothing about.
vi.mock('./EventFormModal.tsx', () => ({ default: () => null }));
vi.mock('./EventStateControl.tsx', () => ({ default: () => <div>where it has got to</div> }));
vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({ default: () => null }));
vi.mock('./EventResponsesTab.tsx', () => ({ default: () => <div>who is coming</div> }));
vi.mock('../../components/history/HistoryPanel.tsx', () => ({ default: () => <div>its trail</div> }));

function anEvent(overrides: Partial<EventInfo> = {}): EventInfo {
  return {
    id: EVENT,
    title: 'Committee night',
    kind: 'clubMeeting',
    startDate: '2026-09-17',
    endDate: null,
    startTime: '19:00:00',
    endTime: '22:30:00',
    place: 'The hut',
    description: null,
    ownerUserId: 'owner-1',
    cavingGroupId: null,
    visibility: 'cavingGroup',
    state: 'planned',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    seriesId: null,
    seriesRule: null,
    ...overrides,
  } as EventInfo;
}

function ListStandIn() {
  const location = useLocation();
  return <div data-testid="list-stand-in">{location.search}</div>;
}

function renderPage() {
  return render(
    // Wrapped in antd's App because the page asks it for the dialog that offers the two ways of
    // calling off an occurrence of a repeating event; the static fallback outside one cannot reach
    // the theme and warns instead of rendering.
    <App>
      <MemoryRouter initialEntries={[`/events/${EVENT}`]}>
        <Routes>
          <Route path="/events/:id" element={<EventDetailPage />} />
          {/* Stands in for the event list, and prints the address it was reached at: what the
              banner's way through to the run is worth is the narrowing it carries, and a link
              rendered without one would satisfy any assertion about the link itself. */}
          <Route path="/events" element={<ListStandIn />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  canSpy.mockReturnValue(false);
  eventSpy.mockReturnValue({ data: anEvent(), isPending: false, isError: false });
});

describe('the event page', () => {
  it('names the event, its kind and the wall-clock times as written', () => {
    renderPage();

    expect(screen.getByTestId('event-title').textContent).toBe('Committee night');
    // 19:00 is 19:00 to every reader: the times are printed from their own parts and neither is
    // handed to a date constructor that would read it against a zone.
    expect(screen.getByText('19:00 – 22:30')).toBeTruthy();
  });

  /**
   * Nobody comes to a deadline, so nobody is asked about one. The server holds the same rule and
   * refuses the whole group for such a kind under its own code; this only decides whether a reader
   * is offered the tab, and offering one that every write behind it would refuse is worse than
   * offering none.
   */
  it('offers who is coming for a kind people come to, and not for one nobody does', () => {
    renderPage();
    expect(screen.getByRole('tab', { name: 'Who is coming' })).toBeTruthy();
    expect(screen.getByText('who is coming')).toBeTruthy();

    cleanup();
    eventSpy.mockReturnValue({
      data: anEvent({ kind: 'deadline' }),
      isPending: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByRole('tab', { name: 'Who is coming' })).toBeNull();
    // The event still has a trail of its own, and it is what the strip falls back to — a tab set
    // whose first pane is missing would otherwise render with nothing under it.
    expect(screen.getByText('its trail')).toBeTruthy();
  });

  /**
   * The read is not retried, so a refusal settles at once and leaves no data behind. Without a
   * branch for it the page holds a spinner that never resolves — which is what somebody following
   * a link to an event whose grant has since been withdrawn would be left looking at, with no
   * message and no way back.
   */
  it('says the event is not there rather than spinning forever when the read is refused', () => {
    eventSpy.mockReturnValue({ data: undefined, isPending: false, isError: true });
    renderPage();

    expect(screen.getByText('No such event')).toBeTruthy();
    // An event nobody may read and one that does not exist answer identically, and the page says
    // so rather than implying the record exists.
    expect(screen.getByText(/not yours to read/)).toBeTruthy();
  });
});

describe('an occurrence of something that comes round again', () => {
  /**
   * The run is not a record anybody can open — it is the other events carrying the same grouping
   * key. So the only account a reader ever gets of why the same evening appears a dozen times is
   * the sentence its author wrote, and it is shown exactly as written: no day on this row or any
   * other is worked out from it.
   */
  it('says the event is one of a run, in the words its author used', () => {
    eventSpy.mockReturnValue({
      data: anEvent({ seriesId: 'series-1', seriesRule: 'every Tuesday in term time' }),
      isPending: false,
      isError: false,
    });
    renderPage();

    expect(screen.getByTestId('event-series-banner')).toBeTruthy();
    expect(screen.getByText(/every Tuesday in term time/)).toBeTruthy();
  });

  /**
   * An event standing on its own has one occurrence and nothing to choose between. This is the
   * positive half of the pair: without it a banner that never rendered at all would satisfy the
   * test above's opposite just as well.
   */
  it('shows no banner on an event that happens once', () => {
    renderPage();

    expect(screen.queryByTestId('event-series-banner')).toBeNull();
  });

  /**
   * The banner says the evening is one of a run and the run is not a record anybody can open, so
   * without a way through to the other occurrences it is a statement with no follow-up. The
   * occurrences are ordinary events, so the way through is the list narrowed to the grouping key
   * — narrowed on the server, where the same visibility walk applies as to any other listing.
   */
  it('offers a way through to the other occurrences of the run', async () => {
    eventSpy.mockReturnValue({
      data: anEvent({ seriesId: 'series-1', seriesRule: 'every Tuesday' }),
      isPending: false,
      isError: false,
    });
    renderPage();

    fireEvent.click(screen.getByTestId('event-series-occurrences'));

    await waitFor(() => expect(screen.getByTestId('list-stand-in').textContent).toBe(
      '?seriesId=series-1',
    ));
  });

  /**
   * Rights over a run are often held over part of it, so an all-or-nothing refusal is the likeliest
   * failure of this act and the least guessable. The words for it are read from the stable code the
   * server sends: a general phrase would tell somebody nothing was saved on an act that saves
   * nothing, and would hide that the refusal was about who may touch which evening.
   */
  it('says why calling off the rest of a run was refused, in the words for that refusal', async () => {
    canSpy.mockReturnValue(true);
    deleteFollowingSpy.mockRejectedValue(
      new ApiError(403, 'event.series_partly_forbidden', 'nope'),
    );
    eventSpy.mockReturnValue({
      data: anEvent({ seriesId: 'series-1', seriesRule: 'every Tuesday' }),
      isPending: false,
      isError: false,
    });
    renderPage();

    fireEvent.click(screen.getByTestId('event-delete'));
    await waitFor(() => expect(screen.getByTestId('event-delete-scope')).toBeTruthy());
    fireEvent.click(screen.getByTestId('event-delete-scope-following'));
    fireEvent.click(screen.getByTestId('event-delete-confirm'));

    await waitFor(() =>
      expect(screen.getByText(/not yours to change, so none of it was changed/)).toBeTruthy(),
    );
  });

  /**
   * Calling off one occurrence and calling off the rest of the run are different acts on different
   * routes, and the narrow one is what a plain confirmation must mean. So the choice is offered
   * before anything is sent, and what goes is what was picked — here the default, which removes
   * this evening and leaves the rest of the run standing.
   */
  it('offers the two ways of calling one off, and takes the narrow one unless the other is picked', async () => {
    canSpy.mockReturnValue(true);
    deleteSpy.mockResolvedValue(undefined);
    eventSpy.mockReturnValue({
      data: anEvent({ seriesId: 'series-1', seriesRule: 'every Tuesday' }),
      isPending: false,
      isError: false,
    });
    renderPage();

    fireEvent.click(screen.getByTestId('event-delete'));
    await waitFor(() => expect(screen.getByTestId('event-delete-scope')).toBeTruthy());
    // Both outcomes are on offer, and what is kept is said before anything is chosen.
    expect(screen.getByTestId('event-delete-scope-occurrence')).toBeTruthy();
    expect(screen.getByTestId('event-delete-scope-following')).toBeTruthy();
    expect(screen.getByText(/already begun are kept/)).toBeTruthy();

    fireEvent.click(screen.getByTestId('event-delete-confirm'));
    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith(EVENT));
    expect(deleteFollowingSpy).not.toHaveBeenCalled();
  });

  /**
   * And when the wider one is picked, the wider one is what goes. The two are different routes
   * rather than a flag on one, so a choice that reached the wrong one would call off two years of
   * evenings under a confirmation that said it was calling off an evening.
   */
  it('calls off the rest of the run when that is what was picked', async () => {
    canSpy.mockReturnValue(true);
    deleteFollowingSpy.mockResolvedValue({ seriesId: 'series-1', deleted: 9, kept: 3 });
    eventSpy.mockReturnValue({
      data: anEvent({ seriesId: 'series-1', seriesRule: 'every Tuesday' }),
      isPending: false,
      isError: false,
    });
    renderPage();

    fireEvent.click(screen.getByTestId('event-delete'));
    await waitFor(() => expect(screen.getByTestId('event-delete-scope')).toBeTruthy());
    fireEvent.click(screen.getByTestId('event-delete-scope-following'));
    fireEvent.click(screen.getByTestId('event-delete-confirm'));

    await waitFor(() => expect(deleteFollowingSpy).toHaveBeenCalledWith(EVENT));
    expect(deleteSpy).not.toHaveBeenCalled();
    // What stayed is reported beside what went: it is the half that surprises people.
    await waitFor(() => expect(screen.getByText(/3 had already happened/)).toBeTruthy());
  });
});
