// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();

const checklists = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Before we set off',
    description: 'The things that have to be settled.',
    ownerUserId: '22222222-2222-2222-2222-222222222222',
    cavingGroupId: null,
    visibility: 'private',
    items: [
      { id: '33333333-3333-3333-3333-333333333333', text: 'Permit obtained', sortOrder: 0 },
      { id: '44444444-4444-4444-4444-444444444444', text: 'Key collected', sortOrder: 1 },
    ],
    createdAt: '2026-08-22T10:00:00Z',
    updatedAt: '2026-08-22T10:00:00Z',
  },
];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useChecklists: () => ({ data: checklists, isLoading: false }),
    useCavingGroups: () => ({ data: [] }),
    useCreateChecklist: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateChecklist: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteChecklist: () => ({ mutate: deleteMutate, isPending: false }),
  };
});

const { default: ChecklistsPage } = await import('./ChecklistsPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset();
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <ChecklistsPage />
    </App>,
  );
}

describe('ChecklistsPage', () => {
  it('writes a new list with the lines somebody typed', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /New checklist/ }));

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Winter trips' } });
    fireEvent.change(screen.getByPlaceholderText('Something to settle before setting off'), {
      target: { value: 'Callout arranged' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    const body = createMutate.mock.calls[0][0];
    expect(body.title).toBe('Winter trips');
    expect(body.items).toEqual([{ id: null, text: 'Callout arranged' }]);

    // Its own audience and nothing else decides who reads it: a list starts shut, and there is
    // no token and no "publish" act anywhere on this page.
    expect(body.visibility).toBe('private');
  });

  it('keeps a line its identity when its wording is corrected', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    const lines = screen.getAllByPlaceholderText('Something to settle before setting off');
    fireEvent.change(lines[0], { target: { value: 'Permit obtained from the estate' } });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const { body } = updateMutate.mock.calls[0][0];

    // The id travels with the reworded line. Sending it without one would write a new line and
    // strand every confirmation any trip had made against the old one.
    expect(body.items[0]).toEqual({
      id: '33333333-3333-3333-3333-333333333333',
      text: 'Permit obtained from the estate',
    });
    expect(body.items[1].id).toBe('44444444-4444-4444-4444-444444444444');
  });
});
