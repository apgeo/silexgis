// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { AdminUser, AdminUserDetail } from '../../api/hooks.ts';

const createMutate = vi.fn();
const reinviteMutate = vi.fn();
const lockMutate = vi.fn();
const uploadReviewMutate = vi.fn();
const deleteMutate = vi.fn();
const resetMutate = vi.fn();
let rights = 'read, write, delete, create';
let callerIsFullAdmin = true;
let groupRights = 'read';

function account(overrides: Partial<AdminUser>): AdminUser {
  return {
    id: 'u-1',
    label: 'Ana Pop',
    email: 'ana@example.test',
    emailConfirmed: true,
    isLockedOut: false,
    lockoutEnd: null,
    isFullAdministrator: false,
    hasPassword: true,
    twoFactorEnabled: false,
    caverId: null,
    createdAt: '2026-09-01T10:00:00Z',
    uploadsNeedReview: false,
    ...overrides,
  };
}

const accounts: AdminUser[] = [
  account({
    id: 'me',
    label: 'Operator',
    email: 'op@example.test',
    isFullAdministrator: true,
    twoFactorEnabled: true,
  }),
  account({ id: 'ana', label: 'Ana Pop' }),
  account({ id: 'rev', label: 'Rareș Held', email: 'rares@example.test', uploadsNeedReview: true }),
  account({ id: 'ion', label: 'Ion Blocat', email: 'ion@example.test', isLockedOut: true, hasPassword: false }),
  account({ id: 'dana', label: 'Dana Cheie', email: 'dana@example.test', twoFactorEnabled: true }),
  account({
    id: 'sef',
    label: 'Alt Administrator',
    email: 'sef@example.test',
    isFullAdministrator: true,
    twoFactorEnabled: true,
  }),
];

const danaDetail: AdminUserDetail = {
  ...account({ id: 'dana', label: 'Dana Cheie', email: 'dana@example.test', twoFactorEnabled: true }),
  permissionGroups: [
    { id: 'g-editors', name: 'Editori', direct: true, through: [] },
    { id: 'g-club', name: 'Cartografi', direct: false, through: [{ id: 'c-1', name: 'Clubul A' }] },
  ],
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({
      data: { domains: { users: rights, permissionGroups: groupRights }, isFullAdmin: callerIsFullAdmin },
    }),
    useAdminUser: () => ({ data: danaDetail, isPending: false, isError: false }),
    useResetAdminUserTwoFactor: () => ({ mutateAsync: resetMutate, isPending: false }),
    useMe: () => ({ data: { id: 'me' } }),
    useAdminUsers: () => ({
      data: { items: accounts, page: 1, pageSize: 25, totalItems: accounts.length },
      isFetching: false,
      error: null,
    }),
    useCreateAdminUser: () => ({ mutateAsync: createMutate, isPending: false }),
    useReinviteAdminUser: () => ({ mutateAsync: reinviteMutate, isPending: false }),
    useSetAdminUserLocked: () => ({ mutateAsync: lockMutate, isPending: false }),
    useSetAdminUserUploadReview: () => ({ mutateAsync: uploadReviewMutate, isPending: false }),
    useDeleteAdminUser: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: UsersPage } = await import('./UsersPage.tsx');
const { userAdminProblem } = await import('./userAdminProblems.ts');

beforeEach(async () => {
  await i18n.changeLanguage('en');
  rights = 'read, write, delete, create';
  callerIsFullAdmin = true;
  groupRights = 'read';
  resetMutate.mockReset().mockResolvedValue({});
  createMutate.mockReset();
  reinviteMutate.mockReset();
  lockMutate.mockReset().mockResolvedValue({});
  uploadReviewMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <MemoryRouter>
      <App>
        <UsersPage />
      </App>
    </MemoryRouter>,
  );
}

function row(name: string) {
  return screen.getByText(name).closest('tr') as HTMLElement;
}

describe('UsersPage', () => {
  it('says of each account whether it can sign in, and offers nothing that would act on the caller', () => {
    show();

    expect(within(row('Ion Blocat')).getByText('Locked')).toBeInTheDocument();
    expect(within(row('Ion Blocat')).getByText('No password yet')).toBeInTheDocument();
    expect(within(row('Ana Pop')).getByText('Can sign in')).toBeInTheDocument();
    expect(within(row('Operator')).getByText('Full administrator')).toBeInTheDocument();

    // The server refuses both on the caller's own account, so neither is offered there.
    const own = row('Operator');
    expect(within(own).getByText('you')).toBeInTheDocument();
    expect(within(own).queryByRole('button', { name: /Lock/ })).toBeNull();
    expect(within(own).queryByRole('button', { name: /Delete/ })).toBeNull();

    expect(within(row('Ion Blocat')).getByRole('button', { name: /Unlock/ })).toBeInTheDocument();
    expect(within(row('Ion Blocat')).getByRole('button', { name: /Send the invitation again/ })).toBeInTheDocument();
    expect(within(row('Ana Pop')).queryByRole('button', { name: /Send the invitation again/ })).toBeNull();
  });

  it('marks an account for upload review and clears the mark', async () => {
    show();

    // The mark is said on the account that has it and on no other.
    expect(within(row('Rareș Held')).getByText('Uploads need review')).toBeTruthy();
    expect(within(row('Ana Pop')).queryByText('Uploads need review')).toBeNull();

    fireEvent.click(within(row('Ana Pop')).getByRole('button', { name: /Hold uploads for review/ }));
    await waitFor(() => expect(uploadReviewMutate).toHaveBeenCalledWith({ id: 'ana', uploadsNeedReview: true }));

    fireEvent.click(within(row('Rareș Held')).getByRole('button', { name: /Stop holding uploads/ }));
    await waitFor(() => expect(uploadReviewMutate).toHaveBeenCalledWith({ id: 'rev', uploadsNeedReview: false }));
  });

  it('offers the upload-review mark only with the right to write accounts, and not above the caller', () => {
    rights = 'read';
    show();
    expect(screen.queryByRole('button', { name: /Hold uploads for review/ })).toBeNull();

    rights = 'read, write';
    callerIsFullAdmin = false;
    cleanup();
    show();
    // A full administrator's account is out of reach of a caller who is not one.
    expect(within(row('Alt Administrator')).queryByRole('button', { name: /Hold uploads for review/ })).toBeNull();
    expect(within(row('Ana Pop')).getByRole('button', { name: /Hold uploads for review/ })).toBeTruthy();
  });

  it('offers only the controls the caller holds the right for', () => {
    rights = 'read';
    show();

    expect(screen.queryByRole('button', { name: /Invite someone/ })).toBeNull();
    expect(within(row('Ana Pop')).queryByRole('button', { name: /Lock/ })).toBeNull();
    expect(within(row('Ana Pop')).queryByRole('button', { name: /Delete/ })).toBeNull();
  });

  it('locks an account and lets a locked one back in', async () => {
    show();

    fireEvent.click(within(row('Ana Pop')).getByRole('button', { name: /Lock/ }));
    await waitFor(() => expect(lockMutate).toHaveBeenCalledWith({ id: 'ana', locked: true }));

    fireEvent.click(within(row('Ion Blocat')).getByRole('button', { name: /Unlock/ }));
    await waitFor(() => expect(lockMutate).toHaveBeenCalledWith({ id: 'ion', locked: false }));
  });

  it('names the account in the delete confirmation, and says why the last administrator stays', async () => {
    deleteMutate.mockRejectedValue(new ApiError(409, 'permission_group.last_full_admin'));
    show();

    fireEvent.click(within(row('Ana Pop')).getByRole('button', { name: /Delete/ }));
    expect(await screen.findByText('Delete the account of Ana Pop?')).toBeInTheDocument();

    const confirm = screen.getAllByRole('button', { name: 'Delete' }).at(-1)!;
    fireEvent.click(confirm);

    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith('ana'));
    expect(await screen.findByText(/This is the last full administrator who can sign in/)).toBeInTheDocument();
  });

  it('offers a second-factor reset only where there is one to reset and the caller may', () => {
    show();

    const reset = { name: /Reset second factor/ };
    expect(within(row('Dana Cheie')).getByText('Second factor on')).toBeInTheDocument();
    expect(within(row('Dana Cheie')).getByRole('button', reset)).toBeInTheDocument();
    expect(within(row('Alt Administrator')).getByRole('button', reset)).toBeInTheDocument();
    expect(within(row('Ana Pop')).queryByRole('button', reset)).toBeNull();
    expect(within(row('Operator')).queryByRole('button', reset)).toBeNull();
  });

  it('keeps a full administrator\'s second factor to full administrators, and needs the write right', () => {
    callerIsFullAdmin = false;
    const { unmount } = show();
    expect(within(row('Dana Cheie')).getByRole('button', { name: /Reset second factor/ })).toBeInTheDocument();
    expect(within(row('Alt Administrator')).queryByRole('button', { name: /Reset second factor/ })).toBeNull();
    // Lock and delete stop at the same seat.
    expect(within(row('Alt Administrator')).queryByRole('button', { name: /Lock/ })).toBeNull();
    expect(within(row('Alt Administrator')).queryByRole('button', { name: /Delete/ })).toBeNull();
    expect(within(row('Ana Pop')).getByRole('button', { name: /Lock/ })).toBeInTheDocument();
    unmount();

    rights = 'read';
    show();
    expect(within(row('Dana Cheie')).queryByRole('button', { name: /Reset second factor/ })).toBeNull();
  });

  it('says in the confirmation what a reset does, and resets on confirming', async () => {
    show();

    fireEvent.click(within(row('Dana Cheie')).getByRole('button', { name: /Reset second factor/ }));
    expect(await screen.findByText('Reset the second factor of Dana Cheie?')).toBeInTheDocument();
    expect(screen.getByText(/signed out on every device and are told by email/)).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Reset second factor' }).at(-1)!);
    await waitFor(() => expect(resetMutate).toHaveBeenCalledWith('dana'));
    expect(await screen.findByText('The second factor of Dana Cheie was reset.')).toBeInTheDocument();
  });

  it('puts a refused reset into words', async () => {
    resetMutate.mockRejectedValue(new ApiError(403, 'access_entry.exceeds_own_rights'));
    show();

    fireEvent.click(within(row('Alt Administrator')).getByRole('button', { name: /Reset second factor/ }));
    fireEvent.click((await screen.findAllByRole('button', { name: 'Reset second factor' })).at(-1)!);

    expect(
      await screen.findByText(/Only a full administrator can lock, unlock, delete or reset the second factor/),
    ).toBeInTheDocument();
  });

  it('lists the permission groups of an opened account, each linking to where membership is edited', async () => {
    show();

    fireEvent.click(within(row('Dana Cheie')).getByRole('button', { name: /expand/i }));
    const groups = await screen.findByTestId('account-groups-dana');

    expect(within(groups).getByRole('link', { name: 'Editori' })).toHaveAttribute(
      'href',
      '/admin/permission-groups?group=g-editors',
    );
    expect(within(groups).getByText('member')).toBeInTheDocument();
    expect(within(groups).getByRole('link', { name: 'Cartografi' })).toBeInTheDocument();
    expect(within(groups).getByText('through Clubul A')).toBeInTheDocument();
  });

  it('names the groups without links for a caller who cannot open the permission-groups page', async () => {
    groupRights = '';
    show();

    fireEvent.click(within(row('Dana Cheie')).getByRole('button', { name: /expand/i }));
    const groups = await screen.findByTestId('account-groups-dana');
    expect(within(groups).getByText('Editori')).toBeInTheDocument();
    expect(within(groups).queryByRole('link')).toBeNull();
  });

  it('hands the set-password link over when the invitation could not be mailed', async () => {
    createMutate.mockResolvedValue({
      user: account({ id: 'new', label: 'Maria Noua', email: 'maria@example.test', hasPassword: false }),
      invitationSent: false,
      setPasswordUrl: 'https://club.example.test/reset-password?email=maria&token=abc&invite=1',
      expiresHours: 24,
    });
    show();

    fireEvent.click(screen.getByRole('button', { name: /Invite someone/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: '  Maria Noua ' } });
    fireEvent.change(within(dialog).getByLabelText('Email address'), { target: { value: 'maria@example.test' } });
    fireEvent.click(within(dialog).getByRole('button', { name: /Open the account/ }));

    await waitFor(() =>
      expect(createMutate).toHaveBeenCalledWith({ email: 'maria@example.test', displayName: 'Maria Noua' }),
    );
    expect(await screen.findByText('Hand this link over')).toBeInTheDocument();
    expect(screen.getByTestId('hand-over-link')).toHaveTextContent(
      'https://club.example.test/reset-password?email=maria&token=abc&invite=1',
    );
    expect(screen.getByText(/it is shown only this once/)).toBeInTheDocument();
  });

  it('says a mailed invitation went, and shows no link', async () => {
    createMutate.mockResolvedValue({
      user: account({ id: 'new', label: 'Maria Noua', email: 'maria@example.test', hasPassword: false }),
      invitationSent: true,
      setPasswordUrl: null,
      expiresHours: 24,
    });
    show();

    fireEvent.click(screen.getByRole('button', { name: /Invite someone/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Maria Noua' } });
    fireEvent.change(within(dialog).getByLabelText('Email address'), { target: { value: 'maria@example.test' } });
    fireEvent.click(within(dialog).getByRole('button', { name: /Open the account/ }));

    expect(await screen.findByText('An invitation was sent to maria@example.test.')).toBeInTheDocument();
    expect(screen.queryByText('Hand this link over')).toBeNull();
  });

  it('puts a taken address on the address field', async () => {
    createMutate.mockRejectedValue(new ApiError(409, 'user.email_taken'));
    show();

    fireEvent.click(screen.getByRole('button', { name: /Invite someone/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Ana' } });
    fireEvent.change(within(dialog).getByLabelText('Email address'), { target: { value: 'ana@example.test' } });
    fireEvent.click(within(dialog).getByRole('button', { name: /Open the account/ }));

    expect(await within(dialog).findByText('An account with this address already exists.')).toBeInTheDocument();
  });

  it('has words for every refusal it names, in both languages', async () => {
    const t = i18n.getFixedT('ro');
    const codes = [
      'permission_group.last_full_admin', 'user.cannot_lock_self', 'user.cannot_delete_self',
      'user.email_taken', 'user.already_enterable', 'user.not_found', 'validation.failed',
      'access.create_forbidden', 'user.cannot_reset_own_two_factor', 'user.two_factor_not_enabled',
      'access_entry.exceeds_own_rights',
    ];
    const generic = t('common.saveFailed');
    for (const code of codes) {
      const words = userAdminProblem(new ApiError(400, code), t);
      expect(words, code).not.toBe(generic);
      expect(words, code).not.toMatch(/^admin\./);
    }
  });
});
