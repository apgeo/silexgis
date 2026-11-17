// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import { ApiError } from '../../../api/client.ts';

/**
 * The permission-groups list: the guards on protected groups, the address that opens one group's
 * editor directly, and what is said when the list cannot be read. The editor is tested on its own.
 */

const t = i18n.getFixedT('en');
const refetchGroups = vi.fn();
const deleteGroup = vi.fn();
const createGroup = vi.fn();
let groupsQuery: { data: unknown[] | undefined; isPending: boolean; isError: boolean };

const FULL = { id: 'g-full', name: 'Full Administrators', slug: 'full-administrators', description: null, isProtected: true, isSeeded: true, memberCount: 1, entryCount: 0 };
const SURVEYORS = { id: 'g-surv', name: 'Surveyors', slug: 'surveyors', description: null, isProtected: false, isSeeded: false, memberCount: 3, entryCount: 2 };

vi.mock('../../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/hooks.ts')>();
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { permissionGroups: 'read, create, update, delete' } } }),
    usePermissionGroups: () => ({ ...groupsQuery, refetch: refetchGroups }),
    useAccessCatalog: () => ({ data: { domains: [], featureSets: [] }, isError: false, refetch: vi.fn() }),
    useCreatePermissionGroup: () => ({ mutateAsync: createGroup, isPending: false }),
    useDeletePermissionGroup: () => ({ mutateAsync: deleteGroup, isPending: false }),
  };
});

vi.mock('./PermissionGroupEditor.tsx', () => ({
  default: ({ group }: { group: { name: string } }) => <div data-testid="group-editor">{group.name}</div>,
}));

const { default: PermissionGroupsPage } = await import('./PermissionGroupsPage.tsx');

function show(at = '/admin/permission-groups') {
  render(
    <App>
      <MemoryRouter initialEntries={[at]}>
        <PermissionGroupsPage />
      </MemoryRouter>
    </App>,
  );
}

const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;

beforeEach(() => {
  groupsQuery = { data: [FULL, SURVEYORS], isPending: false, isError: false };
  refetchGroups.mockReset();
  deleteGroup.mockReset();
});

afterEach(cleanup);

describe('PermissionGroupsPage', () => {
  it('offers no delete on a protected group, and offers it on an ordinary one', () => {
    show();

    expect(within(rowOf('Full Administrators')).getByRole('button', { name: t('iconButtons.delete') })).toBeDisabled();
    expect(within(rowOf('Surveyors')).getByRole('button', { name: t('iconButtons.delete') })).toBeEnabled();
  });

  it('says why deleting a group was refused, in the words every rules screen uses', async () => {
    // The allowed delete first: the same button, answered, says "deleted".
    deleteGroup.mockResolvedValueOnce(undefined);
    show();
    fireEvent.click(within(rowOf('Surveyors')).getByRole('button', { name: t('iconButtons.delete') }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    expect(await screen.findByText(t('common.deleted'))).toBeTruthy();
    cleanup();

    // A group whose denies withhold rights the deleting administrator does not hold.
    deleteGroup.mockRejectedValueOnce(new ApiError(403, 'access_entry.removal_exceeds_own_rights'));
    show();
    fireEvent.click(within(rowOf('Surveyors')).getByRole('button', { name: t('iconButtons.delete') }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    expect(await screen.findByText(
      'You cannot remove a deny that withholds rights you do not hold yourself.',
    )).toBeTruthy();
  });

  it('opens the editor of the group the address names', () => {
    show('/admin/permission-groups?group=g-surv');

    expect(screen.getByTestId('group-editor')).toHaveTextContent('Surveyors');
  });

  it('opens nothing for an address naming a group that is not in the list', () => {
    show('/admin/permission-groups?group=g-gone');

    expect(screen.queryByTestId('group-editor')).not.toBeInTheDocument();
  });

  it('says a failed read of the groups, rather than showing none, and reads again on retry', () => {
    groupsQuery = { data: undefined, isPending: false, isError: true };
    show();

    expect(screen.getByTestId('permission-groups-load-failed')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchGroups).toHaveBeenCalledOnce();
  });

  it('OK with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
    createGroup.mockClear();
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      show();
      fireEvent.click(screen.getByRole('button', { name: /New permission group/ }));
      const dialog = await screen.findByRole('dialog');
      const name = within(dialog).getByLabelText('Name');
      fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      // Long enough for a rejection with no handler to be reported.
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(createGroup).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});
