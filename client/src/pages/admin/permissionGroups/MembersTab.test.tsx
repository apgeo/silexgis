// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import type { PermissionGroup } from '../../../api/hooks.ts';

/** A group's trustees: what is said when the list cannot be read, and when a change is refused. */

const t = i18n.getFixedT('en');
const add = vi.fn();
const remove = vi.fn();
const refetch = vi.fn();
let membersQuery: { data: unknown[] | undefined; isPending: boolean; isError: boolean };

vi.mock('../../../api/hooks.ts', () => ({
  usePermissionGroupMembers: () => ({ ...membersQuery, refetch }),
  usePermissionGroupEntries: () => ({ data: [] }),
  useAddPermissionGroupMember: () => ({ mutateAsync: add, isPending: false }),
  useRemovePermissionGroupMember: () => ({ mutateAsync: remove, isPending: false }),
}));

// The picker is tested on its own; here it only has to name somebody.
vi.mock('../../../components/permissions/SubjectPicker.tsx', () => ({
  default: ({ onSubjectChange }: { onSubjectChange: (id: string, name: string) => void }) => (
    <button type="button" onClick={() => onSubjectChange('user-ana', 'Ana Invented')}>
      pick Ana
    </button>
  ),
}));

const { default: MembersTab } = await import('./MembersTab.tsx');

const group = { id: 'group-1', name: 'Surveyors', slug: 'surveyors', isProtected: false } as unknown as PermissionGroup;
const BOGDAN = { memberKind: 'user', memberId: 'user-bogdan', memberName: 'Bogdan Invented' };

function show() {
  render(
    <App>
      <MembersTab group={group} />
    </App>,
  );
}

beforeEach(() => {
  membersQuery = { data: [BOGDAN], isPending: false, isError: false };
  add.mockReset().mockResolvedValue(undefined);
  remove.mockReset().mockResolvedValue(undefined);
  refetch.mockReset();
});

afterEach(cleanup);

describe('MembersTab', () => {
  it('says a failed read of the members, offers a retry, and never calls the group empty', () => {
    membersQuery = { data: undefined, isPending: false, isError: true };
    show();

    expect(screen.getByText(t('permissionGroups.membersLoadFailed'))).toBeInTheDocument();
    expect(screen.queryByText(t('permissionGroups.noMembers'))).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it('says why adding a member was refused', async () => {
    add.mockRejectedValue(new ApiError(403, 'access_entry.exceeds_own_rights'));
    show();

    fireEvent.click(screen.getByRole('button', { name: 'pick Ana' }));
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: t('permissionGroups.addMember') }));
    });

    expect(add).toHaveBeenCalledWith({ memberKind: 'user', memberId: 'user-ana' });
    expect(await screen.findByText(t('permissionGroups.exceedsOwnRights'))).toBeInTheDocument();
  });

  it('says why removing a member was refused', async () => {
    remove.mockRejectedValue(new ApiError(409, 'permission_group.last_full_admin'));
    show();

    fireEvent.click(screen.getByRole('button', { name: t('iconButtons.delete') }));
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: /OK|Yes/ }));
    });

    expect(remove).toHaveBeenCalledWith({ memberKind: 'user', memberId: 'user-bogdan' });
    expect(await screen.findByText(t('permissionGroups.lastFullAdmin'))).toBeInTheDocument();
  });
});
