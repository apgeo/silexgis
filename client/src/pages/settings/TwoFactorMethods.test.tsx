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
const put = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      ...actual.api,
      POST: (...args: unknown[]) => post(...args),
      DELETE: (...args: unknown[]) => del(...args),
      PUT: (...args: unknown[]) => put(...args),
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
      // As the server sends it: ready only for a method that is switched on, whatever else holds.
      ready: enabled.includes(method),
      destination: null,
      destinationConfirmed: true,
      channelConfigured: true,
    })),
  } as unknown as MfaStatus;
}

function show(enabled: Method[] = [], value: MfaStatus = status(enabled)) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <TwoFactorMethods status={value} />
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
  put.mockReset();
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

/** Makes the confirm step answer a refusal the way the server does: in `error`, not thrown. */
function refuseConfirm(code: string) {
  const answered = post.getMockImplementation()!;
  post.mockImplementation((path: string, ...rest: unknown[]) =>
    path === '/api/v1/me/mfa/methods/{method}'
      ? Promise.resolve({ error: { status: 400, code }, response: new Response(null, { status: 400 }) })
      : answered(path, ...rest),
  );
}

describe('a refused step', () => {
  async function typeAndConfirm() {
    fireEvent.change(screen.getByLabelText(t('security.codePlaceholder')), { target: { value: '123456' } });
    await press(screen.getByRole('button', { name: t('security.confirm') }));
  }

  it('says the account is locked when the server says so, and does not invite another code', async () => {
    refuseConfirm('auth.locked_out');
    show();
    await press(turnOn(1));
    await typeAndConfirm();

    // Told only "the code is invalid", somebody retypes a correct code indefinitely — and on the
    // texted method asks for another paid message each time.
    expect(await screen.findByText(t('security.lockedOut'))).toBeInTheDocument();
    expect(screen.queryByText(t('security.codeInvalid'))).toBeNull();
    expect(screen.queryByText(t('security.enabled'))).toBeNull();
  });

  it('says the code was wrong for any other refusal, and keeps the form and the typed code', async () => {
    refuseConfirm('auth.mfa_code_invalid');
    show();
    await press(turnOn(0));
    await typeAndConfirm();

    expect(await screen.findByText(t('security.codeInvalid'))).toBeInTheDocument();
    expect(screen.queryByText(t('security.lockedOut'))).toBeNull();
    expect(screen.getByLabelText(t('security.codePlaceholder'))).toHaveValue('123456');
    // The shared key stays too: it is the only way to finish adding the authenticator.
    expect(screen.getByText('ABCD EFGH')).toBeInTheDocument();
  });

  it('says the code could not be sent when the challenge is refused, and opens no code form', async () => {
    post.mockImplementation((path: string) =>
      path === '/api/v1/me/mfa/methods/{method}/challenge'
        ? Promise.resolve({ error: { status: 400, code: 'auth.mfa_method_not_ready' } })
        : Promise.reject(new Error(`unexpected ${path}`)),
    );
    show();
    await press(turnOn(1));

    expect(await screen.findByText(t('security.challengeFailed'))).toBeInTheDocument();
    expect(screen.queryByLabelText(t('security.codePlaceholder'))).toBeNull();
    expect(turnOn(1)).not.toHaveClass('ant-btn-loading');
  });

  it('says the default could not be changed when the server refuses it', async () => {
    put.mockResolvedValue({ error: { status: 400, code: 'auth.mfa_method_not_enabled' } });
    show(['authenticator', 'email']);

    await press(screen.getAllByRole('button', { name: t('security.makeDefault') })[0]);

    expect(put).toHaveBeenCalledWith('/api/v1/me/mfa/preferred', { body: { method: 'authenticator' } });
    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
  });
});

describe('a later method', () => {
  it('keeps the recovery codes already shown when its answer carries none', async () => {
    show();
    await press(turnOn(0));
    fireEvent.change(screen.getByLabelText(t('security.codePlaceholder')), { target: { value: '123456' } });
    await press(screen.getByRole('button', { name: t('security.confirm') }));
    expect(screen.getByText(/aaaa-1111/)).toBeInTheDocument();

    // Codes come back only the first time; an empty list must not replace the set being kept.
    post.mockImplementation((path: string) =>
      path === '/api/v1/me/mfa/methods/{method}'
        ? Promise.resolve({ data: { codes: [] } })
        : Promise.resolve({ data: { destination: 'a***@example.org' } }),
    );
    await press(turnOn(1));
    fireEvent.change(screen.getByLabelText(t('security.codePlaceholder')), { target: { value: '654321' } });
    await press(screen.getByRole('button', { name: t('security.confirm') }));

    expect(post).toHaveBeenLastCalledWith(
      '/api/v1/me/mfa/methods/{method}',
      expect.objectContaining({ body: { code: '654321' } }),
    );
    expect(screen.getByText(/aaaa-1111/)).toBeInTheDocument();
  });
});

describe('an email method waiting for a confirmed address', () => {
  function emailMethod(change: { destinationConfirmed?: boolean; channelConfigured?: boolean }): MfaStatus {
    const value = status();
    return {
      ...value,
      methods: value.methods.map((m) => (m.method === 'email' ? { ...m, ...change } : m)),
    } as MfaStatus;
  }

  it('offers to send the confirmation email again beside the reason it cannot be turned on', async () => {
    post.mockResolvedValue({ data: undefined, error: undefined, response: { status: 202 } });
    show([], emailMethod({ destinationConfirmed: false }));

    expect(screen.getByText(t('security.methodEmailNotReady'))).toBeTruthy();
    expect(turnOn(1)).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: t('settings.emails.verify') }));

    await act(async () => {});
    expect(post).toHaveBeenCalledWith('/api/v1/me/email/verify');
    expect(await screen.findByText(t('settings.emails.verifySent'))).toBeTruthy();
  });

  it('offers nothing of the kind once the address is confirmed, and lets the method be turned on', () => {
    // Not switched on, so not ready — the state every confirmed member starts in.
    show();
    expect(screen.queryByRole('button', { name: t('settings.emails.verify') })).toBeNull();
    expect(screen.queryByText(t('security.methodEmailNotReady'))).toBeNull();
    expect(turnOn(1)).toBeEnabled();
  });

  it('says no mail server is set up, rather than "check your inbox", when nothing could carry the link', () => {
    show([], emailMethod({ destinationConfirmed: false, channelConfigured: false }));
    expect(screen.getByText(t('security.methodEmailNoChannel'))).toBeTruthy();
    expect(screen.queryByRole('button', { name: t('settings.emails.verify') })).toBeNull();
    expect(turnOn(1)).toBeDisabled();
  });

  it('says no mail server is set up for a confirmed address too, and offers no confirmation', () => {
    show([], emailMethod({ channelConfigured: false }));
    expect(screen.getByText(t('security.methodEmailNoChannel'))).toBeTruthy();
    expect(screen.queryByRole('button', { name: t('settings.emails.verify') })).toBeNull();
    expect(turnOn(1)).toBeDisabled();
  });
});
