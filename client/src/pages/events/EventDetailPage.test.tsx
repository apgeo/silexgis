// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EventInfo } from '../../api/hooks.ts';
import EventDetailPage from './EventDetailPage.tsx';

const EVENT = '33333333-4444-5555-6666-777777777777';

const { eventSpy } = vi.hoisted(() => ({ eventSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useEvent: () => eventSpy(),
  useEffectiveAccess: () => ({ data: undefined }),
  useCan: () => false,
  useDeleteEvent: () => ({ mutateAsync: vi.fn(), isPending: false }),
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

// Mounted by name rather than exercised: each has tests of its own and asks the server for
// something this page knows nothing about.
vi.mock('./EventFormModal.tsx', () => ({ default: () => null }));
vi.mock('./EventStateControl.tsx', () => ({ default: () => <div>where it has got to</div> }));
vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({ default: () => null }));

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
    ...overrides,
  } as EventInfo;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/events/${EVENT}`]}>
      <Routes>
        <Route path="/events/:id" element={<EventDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
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
