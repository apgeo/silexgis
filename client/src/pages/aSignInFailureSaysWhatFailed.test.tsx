// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import LoginPage from './LoginPage.tsx';
import ResetPasswordPage from './ResetPasswordPage.tsx';
import ConfirmEmailPage from './ConfirmEmailPage.tsx';

/**
 * The anonymous sign-in pages talk to the server with plain fetch. A request that got no answer,
 * that the per-address rate window refused, or that the server failed says nothing about the
 * password, the code or the link — and each used to be reported as exactly that (or, for a fetch
 * that rejected, as nothing at all: the button stopped spinning and the rejection went unhandled).
 */

type Answer = Response | 'no-answer';

function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...headers },
  });
}

/** Answers each path from the table; the auth config is always an ordinary installation. */
function serve(answers: Record<string, Answer | Answer[]>) {
  const calls: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: string) => {
      const path = String(input);
      calls.push(path);
      if (path === '/api/v1/auth/config') {
        return Promise.resolve(
          json(200, { openRegistration: false, externalOnly: false, providers: [], testLogins: null }),
        );
      }
      const listed = answers[path];
      const answer = Array.isArray(listed) ? listed.shift() : listed;
      if (answer === undefined) {
        return Promise.reject(new Error(`unexpected request ${path}`));
      }
      return answer === 'no-answer' ? Promise.reject(new TypeError('Failed to fetch')) : Promise.resolve(answer);
    }),
  );
  return calls;
}

function signIn() {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'ana@example.test' } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'not-a-real-password' } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

function renderLogin(entry = '/login') {
  render(
    <MemoryRouter initialEntries={[entry]}>
      <LoginPage />
    </MemoryRouter>,
  );
}

describe('a sign-in that failed says what failed', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('a wrong password is still a wrong password', async () => {
    serve({ '/api/v1/auth/login': json(401, { code: 'auth.invalid_credentials' }) });
    renderLogin();
    signIn();
    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
  });

  it('a locked account is said to be locked, not to have a wrong password', async () => {
    serve({ '/api/v1/auth/login': json(401, { code: 'auth.locked_out' }) });
    renderLogin();
    signIn();
    expect(await screen.findByText('Account temporarily locked. Try again later.')).toBeInTheDocument();
    expect(screen.queryByText('Invalid email or password.')).not.toBeInTheDocument();
  });

  it('a full rate window is not a wrong password, and passes on the wait the server names', async () => {
    serve({ '/api/v1/auth/login': json(429, { code: 'rate.limited' }, { 'Retry-After': '42' }) });
    renderLogin();
    signIn();
    expect(
      await screen.findByText('Too many sign-in requests from this network. Try again in 42 s.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Invalid email or password.')).not.toBeInTheDocument();
  });

  it('a failed server is not a wrong password', async () => {
    serve({ '/api/v1/auth/login': json(502, {}) });
    renderLogin();
    signIn();
    expect(
      await screen.findByText('The server could not complete this right now. Try again in a moment.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Invalid email or password.')).not.toBeInTheDocument();
  });

  it('no answer at all is said, instead of a button that just stops spinning', async () => {
    serve({ '/api/v1/auth/login': 'no-answer' });
    renderLogin();
    signIn();
    expect(
      await screen.findByText('The server could not be reached. Check the connection, then try again.'),
    ).toBeInTheDocument();
  });

  it('a confirmation mail that could not be asked for is not reported as on its way', async () => {
    serve({
      '/api/v1/auth/login': json(403, { code: 'auth.email_not_confirmed' }),
      '/api/v1/auth/email/resend': 'no-answer',
    });
    renderLogin();
    signIn();
    fireEvent.click(await screen.findByRole('button', { name: 'Send the confirmation email again' }));
    expect(
      await screen.findByText('The server could not be reached. Check the connection, then try again.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText('If that address has an account, a confirmation email is on its way.'),
    ).not.toBeInTheDocument();
  });

  const emailChallenge = () =>
    json(401, { code: 'auth.mfa_required', methods: ['email'], preferredMethod: 'email', recoveryAccepted: true });

  it('a method the account no longer has is said, not reported as a code that failed to go out', async () => {
    serve({
      '/api/v1/auth/login': emailChallenge(),
      '/api/v1/auth/2fa/send': json(400, { code: 'auth.mfa_method_unavailable' }),
    });
    renderLogin();
    signIn();
    expect(
      await screen.findByText('That method is not available on this account. Choose another.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('The code could not be sent. Try another method.')).not.toBeInTheDocument();
  });

  it('a code asked for after the waiting sign-in ended says the sign-in has to start again', async () => {
    serve({
      '/api/v1/auth/login': emailChallenge(),
      '/api/v1/auth/2fa/send': new Response(null, { status: 401 }),
    });
    renderLogin();
    signIn();
    expect(await screen.findByText('The sign-in waited too long for its code. Start again.')).toBeInTheDocument();
    expect(screen.queryByText('The code could not be sent. Try another method.')).not.toBeInTheDocument();
  });

  it('a code that could not be sent for no stated reason is still said so', async () => {
    serve({
      '/api/v1/auth/login': emailChallenge(),
      '/api/v1/auth/2fa/send': json(400, { code: 'auth.mfa_send_failed' }),
    });
    renderLogin();
    signIn();
    expect(await screen.findByText('The code could not be sent. Try another method.')).toBeInTheDocument();
  });

  it.each([
    ['no answer at all', 'no-answer' as Answer, 'The server could not be reached. Check the connection, then try again.'],
    [
      'a full rate window',
      json(429, { code: 'rate.limited' }) as Answer,
      'Too many sign-in requests from this network. Wait a minute, then try again.',
    ],
    ['a failed server', json(503, {}) as Answer, 'The server could not complete this right now. Try again in a moment.'],
  ])(
    'a provider sign-in waiting at its second factor, met with %s on arrival, is not called expired',
    async (_label, answer, sentence) => {
      serve({ '/api/v1/auth/2fa/verify': answer });
      renderLogin('/login?mfa=required');
      expect(await screen.findByText(sentence)).toBeInTheDocument();
      expect(screen.queryByText('The sign-in waited too long for its code. Start again.')).not.toBeInTheDocument();
    },
  );

  it('a provider sign-in that is no longer waiting is still called expired', async () => {
    serve({ '/api/v1/auth/2fa/verify': json(401, { code: 'auth.mfa_not_pending' }) });
    renderLogin('/login?mfa=required');
    expect(await screen.findByText('The sign-in waited too long for its code. Start again.')).toBeInTheDocument();
  });

  it('an external sign-in bounced for a locked account says the account is locked', async () => {
    serve({});
    renderLogin('/login?error=auth.locked_out');
    expect(await screen.findByText('Account temporarily locked. Try again later.')).toBeInTheDocument();
    expect(screen.queryByText(/External sign-in did not complete/)).not.toBeInTheDocument();
  });

  it('a password reset that got no answer does not call the link invalid', async () => {
    serve({ '/api/v1/auth/password/reset': 'no-answer' });
    render(
      <MemoryRouter initialEntries={['/reset-password?email=a%40example.test&token=t0ken']}>
        <ResetPasswordPage />
      </MemoryRouter>,
    );
    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'long-enough-password' } });
    fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: 'long-enough-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Set password' }));
    expect(
      await screen.findByText('The server could not be reached. Check the connection, then try again.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('This reset link is invalid or has expired.')).not.toBeInTheDocument();
  });

  it('a confirmation link that got no answer is not called invalid, and can be tried again', async () => {
    const calls = serve({
      '/api/v1/auth/email/confirm': ['no-answer', new Response(null, { status: 204 })],
    });
    render(
      <MemoryRouter initialEntries={['/confirm-email?user=u1&token=t0ken']}>
        <ConfirmEmailPage />
      </MemoryRouter>,
    );
    expect(
      await screen.findByText('The server could not be reached. Check the connection, then try again.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/This link is invalid or has expired/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Your address is confirmed. You can sign in now.')).toBeInTheDocument();
    await waitFor(() => expect(calls.filter((c) => c === '/api/v1/auth/email/confirm')).toHaveLength(2));
  });

  it('a confirmation link the server refused is still called invalid', async () => {
    serve({ '/api/v1/auth/email/confirm': json(400, { code: 'auth.confirmation_invalid' }) });
    render(
      <MemoryRouter initialEntries={['/confirm-email?user=u1&token=t0ken']}>
        <ConfirmEmailPage />
      </MemoryRouter>,
    );
    expect(await screen.findByText(/This link is invalid or has expired/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });
});
