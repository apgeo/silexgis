// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/**
 * Removing the phone number. When texted codes are the account's only second factor the number
 * takes two-factor sign-in with it, and the confirmation must say so plainly before anything is
 * removed; otherwise the ordinary confirmation is enough. The number below is invented.
 */

type Method = 'authenticator' | 'email' | 'sms';
let enabled: Method[] = [];
const remove = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePhoneStatus: () => ({
      data: { phoneNumber: '+40700000001', confirmed: true, pendingPhoneNumber: null, smsConfigured: true },
    }),
    useMfaStatus: () => ({
      data: {
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
      },
    }),
  };
});

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, api: { ...actual.api, DELETE: (...args: unknown[]) => remove(...args) } };
});

const { default: PhoneNumberCard } = await import('./PhoneNumberCard.tsx');

function show() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <PhoneNumberCard />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  remove.mockReset();
  remove.mockResolvedValue({ data: undefined, error: undefined });
});

afterEach(cleanup);

describe('removing the phone number', () => {
  it('warns that two-factor sign-in goes off when the number is the last second factor', async () => {
    enabled = ['sms'];
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(await screen.findByText('Remove your only second factor?')).toBeInTheDocument();
    expect(screen.getByText(/Removing the number switches two-factor sign-in off/)).toBeInTheDocument();
    expect(remove).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Remove and switch off' }));
    await waitFor(() => expect(remove).toHaveBeenCalledWith('/api/v1/me/phone'));
  });

  it('asks the ordinary question while another second factor remains', async () => {
    enabled = ['sms', 'authenticator'];
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(
      await screen.findByText('Remove your number? Texted codes will be switched off with it.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Remove your only second factor?')).toBeNull();
  });

  it('asks the ordinary question when the number carries no second factor', async () => {
    enabled = [];
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(
      await screen.findByText('Remove your number? Texted codes will be switched off with it.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/switches two-factor sign-in off/)).toBeNull();
  });
});
