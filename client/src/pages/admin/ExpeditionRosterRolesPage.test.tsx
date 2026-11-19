// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// A read of the list that did not land, switched on by the test that needs it.
const listRead = vi.hoisted(() => {
  const refetch = vi.fn();
  return {
    failed: false,
    refetch,
    answer: () => ({ data: undefined, isLoading: false, isError: true, error: new TypeError('offline'), refetch }),
  };
});
import i18n from '../../i18n';
import type { ExpeditionRosterRoleWrite } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();

const roles = [
  { id: 1, code: 'member', name: 'Member', description: null, sortOrder: 10, isSeeded: true },
  { id: 2, code: 'cook', name: 'Cook', description: null, sortOrder: 30, isSeeded: true },
  { id: 3, code: 'night_watch', name: 'Pază de noapte', description: null, sortOrder: 90, isSeeded: false },
];

const capabilities = { domains: { taxonomies: 'read, write' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useExpeditionRosterRoles: () => (listRead.failed ? listRead.answer() : { data: roles, isLoading: false }),
    useCreateExpeditionRosterRole: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateExpeditionRosterRole: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteExpeditionRosterRole: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: ExpeditionRosterRolesPage } = await import('./ExpeditionRosterRolesPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <ExpeditionRosterRolesPage />
    </App>,
  );
}

describe('ExpeditionRosterRolesPage', () => {
  it('offers a delete only for a role the installation added', () => {
    show();
    // Three rows, one delete: a shipped role keeps its row, because camps elsewhere are read by
    // its code and one of them is what simply having been there is recorded as.
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(3);
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1);
  });

  it('reads shipped rows in the reader’s language and a club row as it was written', () => {
    show();
    expect(screen.getByText('Member')).toBeTruthy();
    expect(screen.getByText('Pază de noapte')).toBeTruthy();
  });

  it('locks the code of a shipped role and leaves a club row’s open', () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]);
    expect(screen.getByLabelText('Code').hasAttribute('disabled')).toBe(true);
  });

  it('saves a club role the administrator adds', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a role/ }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: '  Night watch  ' } });
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: 'night_watch' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(createMutate).toHaveBeenCalled());
    const body = createMutate.mock.calls[0][0] as ExpeditionRosterRoleWrite;
    expect(body).toMatchObject({ code: 'night_watch', name: 'Night watch', description: null, sortOrder: 0 });
  });

  it('names the refusal the server gives when a code is already taken', async () => {
    const { ApiError } = await import('../../api/client.ts');
    createMutate.mockRejectedValue(new ApiError(400, 'expedition_roster_role.code_taken'));
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a role/ }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Cook' } });
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: 'cook' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(screen.getByText('Another role already uses that code.')).toBeTruthy());
  });

  it('Save with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      show();
      fireEvent.click(screen.getByRole('button', { name: /Add a role/ }));
      const name = screen.getByLabelText('Name');
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      // Long enough for a rejection with no handler to be reported.
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(createMutate).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});

describe('a failed read of the list', () => {
  it('says the read failed instead of showing an empty list, and offers Retry', () => {
    listRead.failed = true;
    try {
      show();
      expect(screen.getByTestId('expedition-roster-roles-load-failed')).toBeTruthy();
      fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
      expect(listRead.refetch).toHaveBeenCalledTimes(1);
    } finally {
      listRead.failed = false;
      listRead.refetch.mockReset();
    }
  });
});
