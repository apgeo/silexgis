// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EventDefaults, EventInfo } from '../../api/hooks.ts';
import EventFormModal from './EventFormModal.tsx';

const { defaultsSpy, createSpy, updateSpy, followingSpy } = vi.hoisted(() => ({
  defaultsSpy: vi.fn(),
  createSpy: vi.fn(),
  updateSpy: vi.fn(),
  followingSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useCreateEvent: () => ({ mutateAsync: createSpy, isPending: false }),
  useUpdateEvent: () => ({ mutateAsync: updateSpy, isPending: false }),
  useEditEventSeriesFollowing: () => ({ mutateAsync: followingSpy, isPending: false }),
  useEventDefaults: () => defaultsSpy(),
}));

const answered: EventDefaults = { visibility: 'cavingGroup', cavingGroupId: 'club-1' };

function show(event?: EventInfo) {
  return render(
    <App>
      <EventFormModal open event={event} onClose={() => {}} />
    </App>,
  );
}

/** An occurrence of a run, which is the only thing offered the choice of how far an edit reaches. */
function anOccurrence(overrides: Partial<EventInfo> = {}): EventInfo {
  return {
    id: '33333333-4444-5555-6666-777777777777',
    title: 'Committee night',
    kind: 'clubMeeting',
    startDate: '2026-09-17',
    endDate: null,
    startTime: null,
    endTime: null,
    place: null,
    description: null,
    ownerUserId: 'owner-1',
    cavingGroupId: null,
    visibility: 'cavingGroup',
    state: 'planned',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    seriesId: 'series-1',
    seriesRule: 'every Tuesday',
    ...overrides,
  } as EventInfo;
}

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  defaultsSpy.mockReturnValue({ data: undefined });
});

describe('writing an event down', () => {
  /**
   * The answer naming the default audience arrives after the dialog is already on screen, and
   * whoever opened it starts typing straight away. Rebuilding the form around that answer when it
   * lands would clear the title, the dates and the times somebody had just entered, with nothing
   * said about where they went.
   */
  it('keeps what the author has typed when the default audience arrives', async () => {
    const { rerender } = show();

    const title = screen.getByTestId('event-title');
    fireEvent.change(title, { target: { value: 'Committee night' } });
    expect((title as HTMLInputElement).value).toBe('Committee night');

    // The answer lands a moment later, which is the only thing that changes.
    defaultsSpy.mockReturnValue({ data: answered });
    rerender(
      <App>
        <EventFormModal open onClose={() => {}} />
      </App>,
    );

    await waitFor(() => {
      expect((screen.getByTestId('event-title') as HTMLInputElement).value).toBe('Committee night');
    });
  });

  /**
   * And the audience it names is still applied — the fix for the above is not to ignore the
   * answer, which would leave the form showing an audience the write would not produce.
   */
  it('applies the default audience the server named', async () => {
    const { rerender } = show();

    defaultsSpy.mockReturnValue({ data: answered });
    rerender(
      <App>
        <EventFormModal open onClose={() => {}} />
      </App>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('event-visibility').textContent).toContain('Caving group');
    });
    expect(screen.getByTestId('event-caving-group').textContent).toContain('Clubul Speo');
  });
});

describe('an event that comes round again', () => {
  /**
   * A repetition is read only when an event is written for the first time. From the moment the run
   * exists it is ordinary events, each edited as itself — so the controls that ask for one are not
   * offered on an edit, where they would suggest a run could be switched on afterwards.
   */
  it('offers a repetition only while an event is being written', () => {
    show();
    expect(screen.getByTestId('event-repeats')).toBeTruthy();

    cleanup();
    show(anOccurrence());
    expect(screen.queryByTestId('event-repeats')).toBeNull();
  });

  /**
   * A run told neither how many occurrences it has nor the last day one falls on has no end, and a
   * generator with no ceiling is how one form submission fills a table. The pair is refused here
   * before anything is sent; the server holds the same rule and is the one that enforces it.
   */
  it('refuses to send a repetition that never stops', async () => {
    show();
    fireEvent.click(screen.getByTestId('event-repeats'));
    await waitFor(() => expect(screen.getByTestId('event-repeat-rule')).toBeTruthy());

    fireEvent.change(screen.getByTestId('event-title'), { target: { value: 'Club night' } });
    fireEvent.change(screen.getByTestId('event-repeat-rule'), {
      target: { value: 'every Tuesday' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(screen.getByText(/is not written at all/)).toBeTruthy());
    expect(createSpy).not.toHaveBeenCalled();
  });

  /**
   * Editing one occurrence and editing the rest of the run are different acts on different routes.
   * The narrow one is the default, because somebody correcting one evening's place must not
   * silently rewrite the next two years by confirming a form.
   */
  it('changes only the occurrence unless the wider choice is made', async () => {
    updateSpy.mockResolvedValue(anOccurrence());
    show(anOccurrence());
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateSpy).toHaveBeenCalled());
    expect(followingSpy).not.toHaveBeenCalled();
  });

  /** And when the wider one is picked, one act reaches the rest of the run. */
  it('reaches this occurrence and every later one when that is what was picked', async () => {
    followingSpy.mockResolvedValue({ seriesId: 'series-1', changed: 12, anchor: anOccurrence() });
    show(anOccurrence());
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByTestId('event-scope-following'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(followingSpy).toHaveBeenCalled());
    expect(updateSpy).not.toHaveBeenCalled();
    // How many occurrences were really reached, reported rather than the number asked for.
    await waitFor(() => expect(screen.getByText(/12 occurrences were changed/)).toBeTruthy());
  });
});
