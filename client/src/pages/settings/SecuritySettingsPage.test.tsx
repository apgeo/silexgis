// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

/**
 * The account's sign-in methods: what the page shows while they are read, when the read fails,
 * and the password card that only an account holding a password is offered.
 */

const t = i18n.getFixedT('en');
const get = vi.fn();
const post = vi.fn();
const del = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      ...actual.api,
      GET: (...args: unknown[]) => get(...args),
      POST: (...args: unknown[]) => post(...args),
      DELETE: (...args: unknown[]) => del(...args),
    },
  };
});

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    ...actual,
    useMfaStatus: () => ({
      data: { enabled: true, recoveryCodesLeft: 3, preferredMethod: null, methods: [] },
    }),
  };
});

vi.mock('./PasswordChangeCard.tsx', () => ({ default: () => <div data-testid="password-card" /> }));
vi.mock('./PhoneNumberCard.tsx', () => ({ default: () => null }));
vi.mock('./TwoFactorMethods.tsx', () => ({ default: () => null }));

const { default: SecuritySettingsPage } = await import('./SecuritySettingsPage.tsx');

function answer(hasPassword: boolean, logins: { provider: string; displayName: string }[] = []) {
  get.mockImplementation((path: string) =>
    Promise.resolve(
      path === '/api/v1/me/external-logins'
        ? { data: { logins, hasPassword } }
        : { data: { providers: [] } },
    ),
  );
}

async function show() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <MemoryRouter initialEntries={['/settings/security']}>
          <SecuritySettingsPage />
        </MemoryRouter>
      </App>
    </QueryClientProvider>,
  );
  await act(async () => {});
}

beforeEach(() => {
  get.mockReset();
  post.mockReset();
  del.mockReset();
});

afterEach(cleanup);

describe('the security page’s sign-in methods', () => {
  it('shows the password card only once the account is known to hold a password', async () => {
    answer(true);
    await show();
    expect(screen.getByTestId('password-card')).toBeInTheDocument();
  });

  it('offers no password change to a passwordless account', async () => {
    answer(false);
    await show();
    expect(screen.queryByTestId('password-card')).not.toBeInTheDocument();
  });

  it('names a failed read, shows no password card on a guess, and reads again on retry', async () => {
    get.mockRejectedValue(new TypeError('Failed to fetch'));
    await show();

    expect(screen.getByText(t('security.loginsLoadFailed'))).toBeInTheDocument();
    expect(screen.queryByTestId('password-card')).not.toBeInTheDocument();

    answer(true);
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    });
    expect(screen.queryByText(t('security.loginsLoadFailed'))).not.toBeInTheDocument();
    expect(screen.getByTestId('password-card')).toBeInTheDocument();
  });

  it('says so when switching every second factor off never reaches the server', async () => {
    answer(true);
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    await show();

    fireEvent.click(screen.getByRole('button', { name: t('security.disable') }));
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: /OK|Yes/ }));
    });
    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
  });
});

describe('unlinking an external sign-in', () => {
  const google = { provider: 'google', displayName: 'Google' };
  const github = { provider: 'github', displayName: 'GitHub' };

  function unlinkButton() {
    return screen.getAllByRole('button', { name: new RegExp(t('security.unlink')) })[0];
  }

  async function confirmUnlink() {
    fireEvent.click(unlinkButton());
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: /OK|Yes/ }));
    });
  }

  it('never offers a passwordless account to drop its only sign-in, and does offer it otherwise', async () => {
    answer(false, [google]);
    await show();
    expect(unlinkButton()).toBeDisabled();
    cleanup();

    answer(true, [google]);
    await show();
    expect(unlinkButton()).toBeEnabled();
    cleanup();

    answer(false, [google, github]);
    await show();
    expect(unlinkButton()).toBeEnabled();
  });

  it('removes the provider it was asked to, says so and reads the list again', async () => {
    answer(true, [google]);
    del.mockResolvedValue({ error: undefined });
    await show();
    const readsBefore = get.mock.calls.length;

    await confirmUnlink();

    expect(await screen.findByText(t('security.unlinked'))).toBeInTheDocument();
    expect(del).toHaveBeenCalledWith('/api/v1/me/external-logins/{provider}', {
      params: { path: { provider: 'google' } },
    });
    expect(get.mock.calls.length).toBeGreaterThan(readsBefore);
  });

  it.each([
    [
      'the only sign-in method left',
      () => ({ error: { code: 'auth.external_last_login', status: 409 } }),
      'security.unlinkLastLogin',
    ],
    ['a failed server', () => ({ error: { status: 500 } }), 'security.unlinkFailed'],
    ['no connection', () => Promise.reject(new TypeError('Failed to fetch')), 'security.unlinkFailed'],
  ])('names %s and does not claim the account was unlinked', async (_label, refusal, key) => {
    answer(true, [google]);
    del.mockImplementation(refusal);
    await show();

    await confirmUnlink();

    expect(await screen.findByText(t(key))).toBeInTheDocument();
    expect(screen.queryByText(t('security.unlinked'))).not.toBeInTheDocument();
  });
});
