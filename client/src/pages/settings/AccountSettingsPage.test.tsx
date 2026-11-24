// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { DownloadError } from '../../api/download.ts';

/** Changing the sign-in name: what the person is told for each way the rename can end. */

const t = i18n.getFixedT('en');
const rename = vi.fn();
const refreshSession = vi.fn();
const refetchMe = vi.fn();
const requestExport = vi.fn();
const downloadFile = vi.fn();
// The account's latest export; a ready one where a test downloads it.
let latestExport: Record<string, unknown> | undefined;
// The account's own read; replaced by a failed one where a test needs it.
let meRead: Record<string, unknown> = { data: { userName: 'ana.invented' }, isPending: false };

vi.mock('../../api/hooks.ts', () => ({
  useMe: () => meRead,
  useChangeUsername: () => ({ mutateAsync: rename, isPending: false }),
  // The page asks with mutate and its callbacks; the fake settles them the way TanStack Query does.
  useRequestDataExport: () => ({
    mutate: (_: unknown, callbacks: { onSuccess?: () => void; onError?: (error: unknown) => void }) => {
      (requestExport() as Promise<unknown>).then(
        () => callbacks.onSuccess?.(),
        (error: unknown) => callbacks.onError?.(error),
      );
    },
    isPending: false,
  }),
  useDataExport: () => ({ data: latestExport }),
}));

vi.mock('../../api/download.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/download.ts')>()),
  downloadFile: (...args: unknown[]) => downloadFile(...args),
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
  const option = await screen.findByRole('button', { name: /OK|Yes/ });
  await act(async () => {
    fireEvent.click(option);
  });
}

beforeEach(() => {
  rename.mockReset();
  refreshSession.mockReset();
  refreshSession.mockResolvedValue(undefined);
  refetchMe.mockReset();
  requestExport.mockReset();
  downloadFile.mockReset();
  latestExport = undefined;
  meRead = { data: { userName: 'ana.invented' }, isPending: false };
});

describe('a failed read of the account', () => {
  it('says so and offers Retry instead of spinning for ever', () => {
    meRead = { data: undefined, isPending: false, isError: true, error: new TypeError('offline'), refetch: refetchMe };
    render(
      <App>
        <AccountSettingsPage />
      </App>,
    );
    expect(screen.getByTestId('account-load-failed')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchMe).toHaveBeenCalledTimes(1);
  });
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

  it('says the name cannot be used when the server refuses it as such', async () => {
    rename.mockRejectedValue(new ApiError(400, 'me.username_invalid'));
    await renameTo('ana.new');

    expect(await screen.findByText(t('settings.account.usernameInvalid'))).toBeInTheDocument();
    expect(refreshSession).not.toHaveBeenCalled();
    expect(screen.queryByText(t('settings.account.usernameChanged'))).not.toBeInTheDocument();
  });

  it('does not call a rename that never reached the server an unusable name', async () => {
    rename.mockRejectedValue(new TypeError('Failed to fetch'));
    await renameTo('ana.new');

    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
    expect(screen.queryByText(t('settings.account.usernameInvalid'))).not.toBeInTheDocument();
  });
});

describe('the data export', () => {
  function show() {
    render(
      <App>
        <AccountSettingsPage />
      </App>,
    );
  }

  it('says an export is already on its way when the server refuses a second one', async () => {
    requestExport.mockRejectedValue(new ApiError(409, 'me.export_in_progress', 'An export is already being prepared.'));
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.account.export.request') }));

    expect(await screen.findByText(t('settings.account.export.inProgress'))).toBeInTheDocument();
    expect(screen.queryByText(t('settings.account.export.requested'))).not.toBeInTheDocument();
    expect(screen.queryByText(t('common.saveFailed'))).not.toBeInTheDocument();
  });

  it('names a request that failed for any other reason as an export that was not asked for', async () => {
    requestExport.mockRejectedValue(new ApiError(500, undefined, 'Internal Server Error'));
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.account.export.request') }));

    expect(await screen.findByText(t('settings.account.export.requestFailed'))).toBeInTheDocument();
    expect(screen.queryByText(t('settings.account.export.requested'))).not.toBeInTheDocument();
  });

  it('says a ready export has expired when its download is refused as expired', async () => {
    latestExport = { id: 'e-1', status: 'ready' };
    downloadFile.mockRejectedValue(new DownloadError(404, { code: 'me.export_expired', status: 404 }));
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.account.export.download') }));

    expect(await screen.findByText(t('settings.account.export.expired'))).toBeInTheDocument();
    expect(downloadFile).toHaveBeenCalledWith('/api/v1/me/data-export/e-1/download');
  });
});
