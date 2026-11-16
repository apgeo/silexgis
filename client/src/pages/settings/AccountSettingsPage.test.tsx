// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/** Changing the sign-in name: what the person is told for each way the rename can end. */

const t = i18n.getFixedT('en');
const rename = vi.fn();
const refreshSession = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useMe: () => ({ data: { userName: 'ana.invented' }, isPending: false }),
  useChangeUsername: () => ({ mutateAsync: rename, isPending: false }),
  useRequestDataExport: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDataExport: () => ({ data: undefined }),
}));

vi.mock('../../auth/auth.tsx', () => ({ useAuth: () => ({ refreshSession }) }));
vi.mock('./CalendarFeedCard.tsx', () => ({ default: () => null }));

const { default: AccountSettingsPage } = await import('./AccountSettingsPage.tsx');

async function renameTo(name: string) {
  render(
    <App>
      <AccountSettingsPage />
    </App>,
  );
  fireEvent.change(screen.getByLabelText(t('settings.profile.username')), { target: { value: name } });
  fireEvent.click(screen.getByRole('button', { name: t('settings.account.changeUsername') }));
  await act(async () => {
    fireEvent.click(await screen.findByRole('button', { name: /OK|Yes/ }));
  });
}

beforeEach(() => {
  rename.mockReset();
  refreshSession.mockReset();
  refreshSession.mockResolvedValue(undefined);
});

afterEach(cleanup);

describe('changing the user name', () => {
  it('confirms a rename and renews the session behind it', async () => {
    rename.mockResolvedValue(undefined);
    await renameTo('ana.new');

    expect(await screen.findByText(t('settings.account.usernameChanged'))).toBeInTheDocument();
    expect(refreshSession).toHaveBeenCalledOnce();
  });

  it('says the name is taken when it is', async () => {
    rename.mockRejectedValue(new ApiError(409, 'me.username_taken'));
    await renameTo('ana.new');

    expect(await screen.findByText(t('settings.account.usernameTaken'))).toBeInTheDocument();
    expect(refreshSession).not.toHaveBeenCalled();
  });

  it('does not call a rename that never reached the server an unusable name', async () => {
    rename.mockRejectedValue(new TypeError('Failed to fetch'));
    await renameTo('ana.new');

    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
    expect(screen.queryByText(t('settings.account.usernameInvalid'))).not.toBeInTheDocument();
  });
});
