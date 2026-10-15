// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { AdminUser } from '../../api/hooks.ts';

const createMutate = vi.fn();
const reinviteMutate = vi.fn();
const lockMutate = vi.fn();
const deleteMutate = vi.fn();
let rights = 'read, write, delete, create';

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
    caverId: null,
    createdAt: '2026-09-01T10:00:00Z',
    ...overrides,
  };
}

const accounts: AdminUser[] = [
  account({ id: 'me', label: 'Operator', email: 'op@example.test', isFullAdministrator: true }),
  account({ id: 'ana', label: 'Ana Pop' }),
  account({ id: 'ion', label: 'Ion Blocat', email: 'ion@example.test', isLockedOut: true, hasPassword: false }),
];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { users: rights } } }),
    useMe: () => ({ data: { id: 'me' } }),
    useAdminUsers: () => ({
      data: { items: accounts, page: 1, pageSize: 25, totalItems: accounts.length },
      isFetching: false,
      error: null,
    }),
    useCreateAdminUser: () => ({ mutateAsync: createMutate, isPending: false }),
    useReinviteAdminUser: () => ({ mutateAsync: reinviteMutate, isPending: false }),
    useSetAdminUserLocked: () => ({ mutateAsync: lockMutate, isPending: false }),
    useDeleteAdminUser: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: UsersPage } = await import('./UsersPage.tsx');
const { userAdminProblem } = await import('./userAdminProblems.ts');

beforeEach(async () => {
  await i18n.changeLanguage('en');
  rights = 'read, write, delete, create';
  createMutate.mockReset();
  reinviteMutate.mockReset();
  lockMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <UsersPage />
    </App>,
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
      'access.create_forbidden',
    ];
    const generic = t('common.saveFailed');
    for (const code of codes) {
      const words = userAdminProblem(new ApiError(400, code), t);
      expect(words, code).not.toBe(generic);
      expect(words, code).not.toMatch(/^admin\./);
    }
  });
});
