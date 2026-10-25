// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { AccessKey, AccessKeyList } from '../../api/hooks.ts';

/**
 * The page that makes and withdraws the keys a field device uploads with.
 *
 * What is under test is what the credential forces on the screen: the secret is shown once, from
 * the creating answer, and never from the list; the lifetimes offered are the server's; only an
 * active key can be revoked; and a refusal for a device-held sign-in is worded as that.
 *
 * Every key and secret below is invented.
 */

const createMutate = vi.fn();
const revokeMutate = vi.fn();
let list: AccessKeyList;
let queryState: { isPending: boolean; isError: boolean; error: unknown };

const keyOf = (over: Partial<AccessKey>): AccessKey => ({
  id: 'k1',
  name: 'Pixel de test',
  purpose: 'surveyUpload',
  prefix: 'sgx_AbCdEfGh',
  createdAt: '2026-10-01T10:00:00Z',
  expiresAt: '2027-10-01T10:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  state: 'active',
  ...over,
});

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useAccessKeys: () => ({ data: list, refetch: vi.fn(), ...queryState }),
    useCreateAccessKey: () => ({ mutateAsync: createMutate, isPending: false }),
    useRevokeAccessKey: () => ({ mutateAsync: revokeMutate, isPending: false, variables: undefined }),
  };
});

const { default: AccessKeysSettingsPage } = await import('./AccessKeysSettingsPage.tsx');

function show() {
  return render(
    <App>
      <AccessKeysSettingsPage />
    </App>,
  );
}

beforeEach(() => {
  createMutate.mockReset();
  revokeMutate.mockReset();
  list = { maxDays: 365, offeredDays: [30, 90, 365], defaultDays: 365, keys: [] };
  queryState = { isPending: false, isError: false, error: null };
});

afterEach(cleanup);

describe('the access keys page', () => {
  it('says so when there are no keys', () => {
    show();
    expect(screen.getByText('No access keys yet.')).toBeTruthy();
  });

  it('lists a key by its prefix and state, never by anything secret', () => {
    list.keys = [
      keyOf({}),
      keyOf({ id: 'k2', name: 'Vechi', state: 'revoked', revokedAt: '2026-10-02T10:00:00Z' }),
    ];

    show();

    const rows = screen.getAllByTestId('access-key-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByText('sgx_AbCdEfGh…')).toBeTruthy();
    expect(within(rows[0]!).getByText('Never')).toBeTruthy();
    expect(within(rows[0]!).getByText('Active')).toBeTruthy();
    // A revoked key keeps its row and loses its control.
    expect(within(rows[1]!).getByText('Revoked')).toBeTruthy();
    expect(within(rows[1]!).queryByRole('button', { name: 'Revoke' })).toBeNull();
    expect(within(rows[0]!).getByRole('button', { name: 'Revoke' })).toBeTruthy();
  });

  it('offers the lifetimes the server offers and asks for the default one', async () => {
    list.offeredDays = [30, 90];
    list.defaultDays = 90;
    createMutate.mockResolvedValue({ key: keyOf({}), secret: 'sgx_not-a-real-key' });

    show();
    fireEvent.change(screen.getByPlaceholderText('TopoDroid on my phone'), { target: { value: '  Telefon  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create key' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalledWith({ name: 'Telefon', expiresInDays: 90 }));
  });

  it('cannot create a key without a name', () => {
    show();
    expect(screen.getByRole('button', { name: 'Create key' })).toBeDisabled();
  });

  it('shows the secret once, from the creating answer, with a warning', async () => {
    createMutate.mockResolvedValue({ key: keyOf({}), secret: 'sgx_not-a-real-key' });

    show();
    fireEvent.change(screen.getByPlaceholderText('TopoDroid on my phone'), { target: { value: 'Pixel de test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByTestId('access-key-secret')).toHaveTextContent('sgx_not-a-real-key');
    expect(screen.getByText(/shown only once/)).toBeTruthy();
    expect(screen.getByText(/Once you close this message nobody/)).toBeTruthy();
  });

  it('words a refusal as the sign-in requirement', async () => {
    createMutate.mockRejectedValue(new ApiError(403, 'auth.access_key_not_accepted'));

    show();
    fireEvent.change(screen.getByPlaceholderText('TopoDroid on my phone'), { target: { value: 'X' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByText(/signed in with your password/)).toBeTruthy();
  });

  it('asks before revoking, and revokes the key it was asked about', async () => {
    list.keys = [keyOf({})];
    revokeMutate.mockResolvedValue(undefined);

    show();
    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
    expect(revokeMutate).not.toHaveBeenCalled();
    const confirm = await screen.findByText(/Revoke "Pixel de test"\?/);
    fireEvent.click(within(confirm.closest('.ant-popover') as HTMLElement).getByRole('button', { name: 'Revoke' }));

    await waitFor(() => expect(revokeMutate).toHaveBeenCalledWith('k1'));
  });
});
