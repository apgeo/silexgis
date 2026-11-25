// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

/**
 * Removing the phone number. When texted codes are the account's only second factor the number
 * takes two-factor sign-in with it, and the confirmation must say so plainly before anything is
 * removed; otherwise the ordinary confirmation is enough. The number below is invented.
 */

type Method = 'authenticator' | 'email' | 'sms';
let enabled: Method[] = [];
const remove = vi.fn();
const post = vi.fn();
interface PhoneStatus {
  phoneNumber: string | null;
  confirmed: boolean;
  pendingPhoneNumber: string | null;
  smsConfigured: boolean;
}
const confirmedPhone: PhoneStatus = { phoneNumber: '+40700000001', confirmed: true, pendingPhoneNumber: null, smsConfigured: true };
let phone: PhoneStatus = confirmedPhone;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePhoneStatus: () => ({ data: phone }),
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
  return {
    ...actual,
    api: {
      ...actual.api,
      DELETE: (...args: unknown[]) => remove(...args),
      POST: (...args: unknown[]) => post(...args),
    },
  };
});

const { default: PhoneNumberCard } = await import('./PhoneNumberCard.tsx');

function show(client = new QueryClient()) {
  return render(
    <QueryClientProvider client={client}>
      <App>
        <PhoneNumberCard />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  remove.mockReset();
  remove.mockResolvedValue({ data: undefined, error: undefined });
  post.mockReset();
  phone = confirmedPhone;
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

describe('a number waiting for its code', () => {
  // An invented number, mistyped by one digit, waiting for a code nobody will receive.
  const pending: PhoneStatus = { phoneNumber: null, confirmed: false, pendingPhoneNumber: '+40700000009', smsConfigured: true };

  it('asks for the code to be texted again to the waiting number', async () => {
    phone = pending;
    post.mockResolvedValue({ data: { destination: '+40•••••0009' }, error: undefined });
    show();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneResend') }));

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/me/phone/resend'));
    expect(await screen.findByText(i18n.t('security.codeSent', { destination: '+40•••••0009' }))).toBeInTheDocument();
  });

  it('says to wait when the code was asked for too recently', async () => {
    phone = pending;
    post.mockResolvedValue({ data: undefined, error: { code: 'me.phone_resend_too_soon' } });
    show();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneResend') }));

    expect(await screen.findByText(i18n.t('security.phoneResendTooSoon'))).toBeInTheDocument();
  });

  it('lets a mistyped number be replaced by sending to another one', async () => {
    phone = pending;
    post.mockResolvedValue({ data: { destination: '+40•••••0001' }, error: undefined });
    show();

    expect(screen.queryByRole('textbox', { name: i18n.t('security.phoneNumber') })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneUseOther') }));
    fireEvent.change(screen.getByRole('textbox', { name: i18n.t('security.phoneNumber') }), {
      target: { value: '+40 700 000 001' },
    });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneChange') }));

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/v1/me/phone/change', { body: { phoneNumber: '+40700000001' } }));
  });

  it('drops only the waiting number, leaving a confirmed one alone', async () => {
    phone = { ...pending, phoneNumber: '+40700000001', confirmed: true };
    show();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneCancelPending') }));

    await waitFor(() => expect(remove).toHaveBeenCalledWith('/api/v1/me/phone/pending'));
    expect(remove).not.toHaveBeenCalledWith('/api/v1/me/phone');
    expect(await screen.findByText(i18n.t('security.phonePendingCancelled'))).toBeInTheDocument();
  });

  it('offers dropping the waiting number even without a text gateway', () => {
    phone = { ...pending, smsConfigured: false };
    show();

    expect(screen.getByRole('button', { name: i18n.t('security.phoneCancelPending') })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: i18n.t('security.phoneResend') })).toBeNull();
  });

  it('says a send failed when the request never reached the server', async () => {
    phone = pending;
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    show();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneResend') }));

    expect(await screen.findByText(i18n.t('security.phoneSendFailed'))).toBeInTheDocument();
  });
});

describe('confirming the texted code', () => {
  const pending: PhoneStatus = { phoneNumber: null, confirmed: false, pendingPhoneNumber: '+40700000009', smsConfigured: true };

  async function typeAndConfirm(code = ' 123456 ') {
    fireEvent.change(screen.getByLabelText(i18n.t('security.codePlaceholder')), { target: { value: code } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.confirm') }));
    await waitFor(() => expect(post).toHaveBeenCalled());
  }

  it('sends the typed code, says the number is confirmed and clears the code', async () => {
    phone = pending;
    post.mockResolvedValue({ data: undefined, error: undefined });
    show();

    await typeAndConfirm();

    expect(post).toHaveBeenCalledWith('/api/v1/me/phone/confirm', { body: { code: '123456' } });
    expect(await screen.findByText(i18n.t('security.phoneConfirmed'))).toBeInTheDocument();
    expect(screen.getByLabelText(i18n.t('security.codePlaceholder'))).toHaveValue('');
  });

  it.each([
    ['auth.locked_out', 'security.lockedOut'],
    ['me.phone_taken', 'security.phoneTaken'],
    ['me.phone_code_invalid', 'security.codeInvalid'],
  ])('words the %s refusal on its own and keeps the typed code', async (code, wording) => {
    phone = pending;
    post.mockResolvedValue({ data: undefined, error: { code } });
    show();

    await typeAndConfirm('123456');

    expect(await screen.findByText(i18n.t(wording))).toBeInTheDocument();
    for (const other of ['security.lockedOut', 'security.phoneTaken', 'security.codeInvalid'].filter((key) => key !== wording)) {
      expect(screen.queryByText(i18n.t(other))).toBeNull();
    }
    expect(screen.queryByText(i18n.t('security.phoneConfirmed'))).toBeNull();
    expect(screen.getByLabelText(i18n.t('security.codePlaceholder'))).toHaveValue('123456');
  });

  it('says nothing is waiting any more and reads the card again when the pending number is gone', async () => {
    phone = pending;
    post.mockResolvedValue({ data: undefined, error: { code: 'me.phone_change_missing' } });
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    show(client);

    fireEvent.click(screen.getByRole('button', { name: i18n.t('security.phoneResend') }));

    expect(await screen.findByText(i18n.t('security.phoneNothingPending'))).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalled();
  });
});
