// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripParticipantRoleWrite } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();

const roles = [
  { id: 1, code: 'participant', name: 'Participant', description: null, sortOrder: 10, isSeeded: true },
  { id: 2, code: 'leader', name: 'Leader', description: null, sortOrder: 30, isSeeded: true },
  { id: 3, code: 'cook', name: 'Bucătar de bivuac', description: null, sortOrder: 90, isSeeded: false },
];

const capabilities = { domains: { taxonomies: 'read, write' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useTripParticipantRoles: () => ({ data: roles, isLoading: false }),
    useCreateTripParticipantRole: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateTripParticipantRole: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteTripParticipantRole: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: TripParticipantRolesPage } = await import('./TripParticipantRolesPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <TripParticipantRolesPage />
    </App>,
  );
}

describe('TripParticipantRolesPage', () => {
  it('offers a delete only for a role the installation added', () => {
    show();
    // Three rows, one delete: a shipped job keeps its row, because trips elsewhere are read by
    // its code and two of them are what attendance and the right to edit a proposal rest on.
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(3);
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1);
  });

  it('reads shipped rows in the reader’s language and a club row as it was written', () => {
    show();
    expect(screen.getByText('Participant')).toBeTruthy();
    expect(screen.getByText('Bucătar de bivuac')).toBeTruthy();
  });

  it('locks the code of a shipped role and leaves a club row’s open', () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]);
    expect(screen.getByLabelText('Code').hasAttribute('disabled')).toBe(true);
  });

  it('saves a club role the administrator adds', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a role/ }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: '  Camp cook  ' } });
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: 'camp_cook' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(createMutate).toHaveBeenCalled());
    const body = createMutate.mock.calls[0][0] as TripParticipantRoleWrite;
    expect(body).toMatchObject({ code: 'camp_cook', name: 'Camp cook', description: null, sortOrder: 0 });
  });
});
