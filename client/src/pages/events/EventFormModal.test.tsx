// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EventDefaults } from '../../api/hooks.ts';
import EventFormModal from './EventFormModal.tsx';

const { defaultsSpy } = vi.hoisted(() => ({ defaultsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useCreateEvent: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateEvent: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useEventDefaults: () => defaultsSpy(),
}));

const answered: EventDefaults = { visibility: 'cavingGroup', cavingGroupId: 'club-1' };

function show() {
  return render(
    <App>
      <EventFormModal open onClose={() => {}} />
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
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
