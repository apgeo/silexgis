// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { MfaStatus } from '../../api/hooks.ts';

/**
 * The second-factor rows: switching a method on, the one-time recovery codes the first one
 * yields, and what the person is told when a request never comes back. Codes and addresses below
 * are invented.
 */

const t = i18n.getFixedT('en');
const post = vi.fn();
const del = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      ...actual.api,
      POST: (...args: unknown[]) => post(...args),
      DELETE: (...args: unknown[]) => del(...args),
    },
  };
});

const { default: TwoFactorMethods } = await import('./TwoFactorMethods.tsx');

type Method = 'authenticator' | 'email' | 'sms';

function status(enabled: Method[] = []): MfaStatus {
  return {
    enabled: enabled.length > 0,
    recoveryCodesLeft: 10,
    preferredMethod: null,
    methods: (['authenticator', 'email', 'sms'] as Method[]).map((method) => ({
      method,
      enabled: enabled.includes(method),
      allowed: true,
      ready: true,
      destination: null,
    })),
  } as unknown as MfaStatus;
}

function show(enabled: Method[] = []) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <TwoFactorMethods status={status(enabled)} />
      </App>
    </QueryClientProvider>,
  );
}

const turnOn = (index: number) => screen.getAllByRole('button', { name: t('security.turnOn') })[index];

async function press(button: HTMLElement) {
  await act(async () => {
    fireEvent.click(button);
  });
}

beforeEach(() => {
  post.mockReset();
  del.mockReset();
  post.mockImplementation((path: string) => {
    if (path === '/api/v1/me/mfa/enroll') {
      return Promise.resolve({ data: { sharedKey: 'ABCD EFGH', authenticatorUri: 'otpauth://totp/x?secret=ABCDEFGH' } });
    }
    if (path === '/api/v1/me/mfa/methods/{method}/challenge') {
      return Promise.resolve({ data: { destination: 'a***@example.org' } });
    }
    if (path === '/api/v1/me/mfa/methods/{method}') {
      return Promise.resolve({ data: { codes: ['aaaa-1111', 'bbbb-2222'] } });
    }
    return Promise.reject(new Error(`unexpected ${path}`));
  });
});

afterEach(cleanup);

describe('the recovery codes', () => {
  async function enableAuthenticator() {
    await press(turnOn(0));
    fireEvent.change(screen.getByLabelText(t('security.codePlaceholder')), { target: { value: '123456' } });
    await press(screen.getByRole('button', { name: t('security.confirm') }));
  }

  it('stay on screen when another method is started, until the person says they are kept', async () => {
    show();
    await enableAuthenticator();
    expect(screen.getByText(/aaaa-1111/)).toBeInTheDocument();

    // They are shown once and only once: starting the email method must not take them away.
    await press(turnOn(1));
    expect(post).toHaveBeenCalledWith('/api/v1/me/mfa/methods/{method}/challenge', expect.anything());
    expect(screen.getByText(/aaaa-1111/)).toBeInTheDocument();

    await press(screen.getByRole('button', { name: t('security.recoveryKept') }));
    expect(screen.queryByText(/aaaa-1111/)).not.toBeInTheDocument();
  });
});

describe('a request that never comes back', () => {
  it('says the step failed when starting a method cannot reach the server, and frees the button', async () => {
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    show();

    await press(turnOn(1));

    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
    expect(turnOn(1)).not.toHaveClass('ant-btn-loading');
  });

  it('says the step failed when turning a method off cannot reach the server', async () => {
    del.mockRejectedValue(new TypeError('Failed to fetch'));
    show(['authenticator']);

    await press(screen.getByRole('button', { name: t('security.turnOff') }));
    await press(screen.getByRole('button', { name: /^(OK|Yes)$/i }));

    expect(del).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
  });
});
