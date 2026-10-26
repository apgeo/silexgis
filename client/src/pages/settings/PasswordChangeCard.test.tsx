// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

const changeMutate = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useChangePassword: () => ({ mutateAsync: changeMutate, isPending: false }),
}));

const { default: PasswordChangeCard } = await import('./PasswordChangeCard.tsx');

const onChanged = vi.fn();

beforeEach(async () => {
  await i18n.changeLanguage('en');
  changeMutate.mockReset();
  onChanged.mockReset();
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <PasswordChangeCard onChanged={onChanged} />
    </App>,
  );
}

function fill(current: string, next: string, again: string) {
  fireEvent.change(screen.getByLabelText('Current password'), { target: { value: current } });
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: next } });
  fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: again } });
  fireEvent.click(screen.getByRole('button', { name: /Change password/ }));
}

describe('PasswordChangeCard', () => {
  it('says before the change that it signs the reader out', () => {
    show();
    expect(screen.getByText(/signs you out everywhere, including here/)).toBeInTheDocument();
  });

  it('refuses a short password and a confirmation that differs, before asking the server', async () => {
    show();

    fill('the-old-password', 'short', 'short');
    expect(await screen.findByText('Use at least 10 characters.')).toBeInTheDocument();

    fill('the-old-password', 'a-long-new-password', 'a-different-one');
    expect(await screen.findByText('The two passwords do not match.')).toBeInTheDocument();
    expect(changeMutate).not.toHaveBeenCalled();
  });

  it('changes the password and then ends the session here', async () => {
    changeMutate.mockResolvedValue(undefined);
    show();

    fill('the-old-password', 'a-long-new-password', 'a-long-new-password');

    await waitFor(() =>
      expect(changeMutate).toHaveBeenCalledWith({
        currentPassword: 'the-old-password',
        newPassword: 'a-long-new-password',
        revokeAccessKeys: false,
      }),
    );
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });

  it('revokes the access keys too only when the reader switches it on', async () => {
    changeMutate.mockResolvedValue(undefined);
    show();

    const keys = screen.getByRole('switch', { name: 'Also revoke my access keys' });
    expect(keys).not.toBeChecked();
    fireEvent.click(keys);
    fill('the-old-password', 'a-long-new-password', 'a-long-new-password');

    await waitFor(() =>
      expect(changeMutate).toHaveBeenCalledWith({
        currentPassword: 'the-old-password',
        newPassword: 'a-long-new-password',
        revokeAccessKeys: true,
      }),
    );
  });

  it('puts a wrong current password on its own field and stays signed in', async () => {
    changeMutate.mockRejectedValue(new ApiError(400, 'me.password_incorrect'));
    show();

    fill('not-the-password', 'a-long-new-password', 'a-long-new-password');

    expect(await screen.findByText('This is not your current password.')).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('puts a new password the server refuses on the new-password field', async () => {
    changeMutate.mockRejectedValue(new ApiError(400, 'me.password_invalid'));
    show();

    fill('the-old-password', 'a-long-new-password', 'a-long-new-password');

    expect(await screen.findByText(/This password is not accepted/)).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });
});
