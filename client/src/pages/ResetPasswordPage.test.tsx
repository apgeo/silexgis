// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import ResetPasswordPage from './ResetPasswordPage.tsx';

/**
 * The page a reset link lands on: the access keys go with the old password only when the reader
 * says so, and an invitation (a new account, so no keys) is not asked.
 */

const fetchMock = vi.fn();

beforeEach(async () => {
  await i18n.changeLanguage('en');
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function show(query: string) {
  return render(
    <MemoryRouter initialEntries={[`/reset-password?${query}`]}>
      <Routes>
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/login" element={<div data-testid="login-page" />} />
      </Routes>
    </MemoryRouter>,
  );
}

function fillAndSend() {
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a-long-new-password' } });
  fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: 'a-long-new-password' } });
  fireEvent.click(screen.getByRole('button', { name: /reset|set|save|password/i }));
}

function sentBody(): Record<string, unknown> {
  const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

describe('ResetPasswordPage', () => {
  it('says a link with no token is broken before asking for a password', () => {
    show('email=someone%40example.test');

    expect(screen.getByTestId('reset-link-broken')).toBeTruthy();
    expect(screen.queryByLabelText('New password')).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('keeps the access keys unless the switch is turned on', async () => {
    show('email=invented%40example.test&token=invented-token');
    expect(screen.getByRole('switch', { name: 'Also revoke my access keys' })).not.toBeChecked();
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toEqual({
      email: 'invented@example.test',
      token: 'invented-token',
      newPassword: 'a-long-new-password',
      revokeAccessKeys: false,
    });
  });

  it('revokes the access keys too when the reader asks', async () => {
    show('email=invented%40example.test&token=invented-token');
    fireEvent.click(screen.getByRole('switch', { name: 'Also revoke my access keys' }));
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toMatchObject({ revokeAccessKeys: true });
  });

  it('does not ask an invited account, which has no keys', async () => {
    show('email=invented%40example.test&token=invented-token&invite=1');
    expect(screen.queryByRole('switch', { name: 'Also revoke my access keys' })).toBeNull();
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toMatchObject({ revokeAccessKeys: false });
  });
});

describe('ResetPasswordPage when the reset does not go through', () => {
  const problem = (code: string) =>
    new Response(JSON.stringify({ status: 400, code }), {
      status: 400,
      headers: { 'Content-Type': 'application/problem+json' },
    });

  it('says the password itself was refused when the server refuses it on policy', async () => {
    fetchMock.mockResolvedValue(problem('validation.failed'));
    show('email=invented%40example.test&token=invented-token');
    fillAndSend();

    expect(await screen.findByText(i18n.t('auth.resetPasswordRejected'))).toBeInTheDocument();
    expect(screen.queryByText(i18n.t('auth.resetLinkInvalid'))).toBeNull();
    // The form stays, so a stronger password can be typed.
    expect(screen.getByLabelText('New password')).toBeInTheDocument();
  });

  it('says the link is no good for any other refusal', async () => {
    fetchMock.mockResolvedValue(problem('auth.reset_token_invalid'));
    show('email=invented%40example.test&token=expired-token');
    fillAndSend();

    expect(await screen.findByText(i18n.t('auth.resetLinkInvalid'))).toBeInTheDocument();
    expect(screen.queryByText(i18n.t('auth.resetPasswordRejected'))).toBeNull();
  });

  it('sends nothing while the two passwords differ', async () => {
    show('email=invented%40example.test&token=invented-token');
    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a-long-new-password' } });
    fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: 'a-long-new-passwore' } });
    fireEvent.click(screen.getByRole('button', { name: /reset|set|save|password/i }));

    expect(await screen.findByText(i18n.t('auth.passwordsDiffer'))).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('ResetPasswordPage when the reset goes through', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('says so, and then takes the reader to the sign-in page', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    show('email=invented%40example.test&token=invented-token');
    fillAndSend();

    expect(await screen.findByText(i18n.t('auth.resetPasswordDone'))).toBeInTheDocument();
    expect(screen.queryByTestId('login-page')).toBeNull();
    await act(async () => {
      vi.advanceTimersByTime(2000);
    });
    expect(screen.getByTestId('login-page')).toBeInTheDocument();
  });
});
