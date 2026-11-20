// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EventDefaults, EventInfo } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { noteVersionOfReading } from '../../api/readingVersion.ts';
import EventFormModal from './EventFormModal.tsx';

const { defaultsSpy, createSpy, updateSpy, followingSpy, versionOnFile, runVersionOnFile } =
  vi.hoisted(() => ({
    defaultsSpy: vi.fn(),
    createSpy: vi.fn(),
    updateSpy: vi.fn(),
    followingSpy: vi.fn(),
    // The versions on file for the event and for its run, as the transport would answer them now.
    versionOnFile: vi.fn<(path: string) => string | undefined>(() => undefined),
    runVersionOnFile: vi.fn<(path: string) => string | undefined>(() => undefined),
  }));


/** `reading` as a read answered at `version` hands it over: the version is kept with the values. */
function readAt<T extends object>(reading: T, version: string): T {
  noteVersionOfReading(reading, new Response(null, { headers: { ETag: version } }));
  return reading;
}

vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  lastReadETag: (path: string) => versionOnFile(path),
  lastReadRunVersion: (path: string) => runVersionOnFile(path),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useCreateEvent: () => ({ mutateAsync: createSpy, isPending: false }),
  useUpdateEvent: () => ({ mutateAsync: updateSpy, isPending: false }),
  useEditEventSeriesFollowing: () => ({ mutateAsync: followingSpy, isPending: false }),
  useEventDefaults: () => defaultsSpy(),
}));

const answered: EventDefaults = { visibility: 'cavingGroup', cavingGroupId: 'club-1' };

const SAVE_FAILED = 'The operation failed. Please try again.';

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
  versionOnFile.mockReturnValue(undefined);
  runVersionOnFile.mockReturnValue(undefined);
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

  /**
   * The repetition is offered only with the wider scope, prefilled with the one the run was
   * written under, and travels only when it was changed: sent unchanged it would re-space the
   * run and undo an occurrence somebody had moved by hand.
   */
  it('offers the repetition for the rest of the run only, and sends it only when changed', async () => {
    followingSpy.mockResolvedValue({ seriesId: 'series-1', changed: 3, anchor: anOccurrence() });
    show(anOccurrence({ seriesFrequency: 'weekly' } as Partial<EventInfo>));
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());
    expect(screen.queryByTestId('event-series-frequency')).toBeNull();

    fireEvent.click(screen.getByTestId('event-scope-following'));
    await waitFor(() => expect(screen.getByTestId('event-series-frequency')).toBeTruthy());

    // Unchanged: the body carries no repetition.
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(followingSpy).toHaveBeenCalled());
    const unchanged = followingSpy.mock.calls[0][0] as { body: { seriesFrequency?: string } };
    expect(unchanged.body.seriesFrequency).toBeUndefined();
  });

  it('sends the new repetition for the rest of the run once one is picked', async () => {
    followingSpy.mockResolvedValue({ seriesId: 'series-1', changed: 3, anchor: anOccurrence() });
    show(anOccurrence({ seriesFrequency: 'weekly' } as Partial<EventInfo>));
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());
    fireEvent.click(screen.getByTestId('event-scope-following'));
    await waitFor(() => expect(screen.getByTestId('event-series-frequency')).toBeTruthy());

    // antd's select: open it by its combobox, then pick the visible option by title.
    fireEvent.mouseDown(screen.getByTestId('event-series-frequency').querySelector('input')!);
    fireEvent.click(await screen.findByTitle('Every other week'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(followingSpy).toHaveBeenCalled());
    const changed = followingSpy.mock.calls[0][0] as { body: { seriesFrequency?: string } };
    expect(changed.body.seriesFrequency).toBe('fortnightly');
  });
});

/**
 * The page behind the dialog goes on reading the event — on a return to the browser tab, after
 * anything that refreshes events — and every read files a newer version. A save that carried that
 * one with the values the form was filled from would present somebody else's change as already
 * seen and write over it; the save has to carry the version the dialog opened with.
 */
describe('a save from a dialog opened before the event was read again', () => {
  const path = `/api/v1/events/${anOccurrence().id}`;

  function reopenAfterARead() {
    // A read made outside the cache has filed an event version newer than either copy holds.
    versionOnFile.mockImplementation((asked) => (asked === path ? '"3"' : undefined));
    runVersionOnFile.mockImplementation((asked) => (asked === path ? '"run-1"' : undefined));
    const view = show(readAt(anOccurrence(), '"1"'));
    runVersionOnFile.mockImplementation((asked) => (asked === path ? '"run-2"' : undefined));
    view.rerender(
      <App>
        <EventFormModal
          open
          event={readAt(anOccurrence({ title: 'Renamed by somebody else' }), '"2"')}
          onClose={() => {}}
        />
      </App>,
    );
  }

  it('sends the version of the occurrence the dialog opened with', async () => {
    updateSpy.mockResolvedValue(anOccurrence());
    reopenAfterARead();
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateSpy).toHaveBeenCalled());
    const sent = updateSpy.mock.calls[0][0] as { ifMatch?: string; body: { title: string } };
    expect(sent.ifMatch).toBe('"1"');
    expect(sent.body.title).toBe('Committee night');
  });

  it('sends the version of the run the dialog opened with when the rest of the run is edited', async () => {
    followingSpy.mockResolvedValue({ seriesId: 'series-1', changed: 2, anchor: anOccurrence() });
    reopenAfterARead();
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByTestId('event-scope-following'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(followingSpy).toHaveBeenCalled());
    expect((followingSpy.mock.calls[0][0] as { runVersion?: string }).runVersion).toBe('"run-1"');
  });

  it('leaves a stale-version refusal to the one conflict warning', async () => {
    updateSpy.mockRejectedValueOnce(new ApiError(412, 'concurrency.version_mismatch'));
    show(anOccurrence());
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(updateSpy).toHaveBeenCalledTimes(1));

    // A second press refused for a reason of its own draws its own sentence. Messages are drawn
    // in the order they are asked for, so once it is on screen anything the stale refusal had
    // asked for would be too: only then does the absence of "save failed" prove anything.
    updateSpy.mockRejectedValueOnce(new ApiError(409, 'event.recurrence_overlapping'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText(/Each occurrence would still be running/)).toBeInTheDocument();
    expect(screen.queryByText(SAVE_FAILED)).toBeNull();
  });

  it.each([
    ['a refusal it has words for', new ApiError(409, 'event.recurrence_overlapping'), /Each occurrence would still be running/],
    ['a failed server', new ApiError(500, undefined), SAVE_FAILED],
  ])('says why a save was refused for %s, and stays open', async (_, error, sentence) => {
    updateSpy.mockRejectedValueOnce(error);
    show(anOccurrence());
    await waitFor(() => expect(screen.getByTestId('event-scope')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(await screen.findByText(sentence)).toBeInTheDocument();
    expect(screen.getByTestId('event-title')).toHaveValue('Committee night');
  });
});

describe('leaving the event dialog with typed work', () => {
  afterEach(cleanup);

  function showClosable() {
    const onClose = vi.fn();
    render(
      <App>
        <EventFormModal open onClose={onClose} />
      </App>,
    );
    return onClose;
  }
  const cancel = () => fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

  it('closes at once when nothing was typed', () => {
    const onClose = showClosable();
    cancel();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('asks before throwing typed work away', async () => {
    const onClose = showClosable();
    fireEvent.change(screen.getByTestId('event-title'), { target: { value: 'Committee night' } });
    cancel();
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.click((await screen.findAllByTestId('discard-confirm')).at(-1)!);
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('is not closed by a click beside it', () => {
    const onClose = showClosable();
    const wrap = document.querySelector('.ant-modal-wrap') as HTMLElement;
    fireEvent.mouseDown(wrap);
    fireEvent.mouseUp(wrap);
    fireEvent.click(wrap);
    expect(onClose).not.toHaveBeenCalled();
  });
});
