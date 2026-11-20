// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import LoginPage from './LoginPage.tsx';
import ForgotPasswordPage from './ForgotPasswordPage.tsx';
import RegisterPage from './RegisterPage.tsx';

/**
 * The ways in that do not need an administrator: asking for a password-reset mail, and opening an
 * account on an installation that allows it. The server never says whether an address has an
 * account, so neither page may say it either.
 */

interface Answer {
  status: number;
  body?: unknown;
}

const posted: { url: string; body: Record<string, unknown> }[] = [];
let config: Answer | 'unreachable';
let answers: Record<string, Answer | 'unreachable'>;

function respond(answer: Answer | 'unreachable'): Promise<Response> {
  if (answer === 'unreachable') {
    return Promise.reject(new TypeError('Failed to fetch'));
  }
  return Promise.resolve(
    new Response(answer.body === undefined ? null : JSON.stringify(answer.body), {
      status: answer.status,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  posted.length = 0;
  config = { status: 200, body: { openRegistration: false, externalOnly: false, providers: [] } };
  answers = {};
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/config') {
        return respond(config);
      }
      posted.push({ url, body: JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown> });
      return respond(answers[url] ?? { status: 202 });
    }),
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function at(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/register" element={<RegisterPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('the sign-in page', () => {
  it('offers a forgotten-password link under the password, and no account link while registration is closed', async () => {
    at('/login');
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledWith('/api/v1/auth/config'));

    const forgot = await screen.findByRole('link', { name: 'Forgot your password?' });
    expect(forgot).toHaveAttribute('href', '/forgot-password');
    expect(screen.queryByRole('link', { name: 'Create account' })).not.toBeInTheDocument();
  });

  it('offers Create account only when the installation says registration is open', async () => {
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    at('/login');

    expect(await screen.findByRole('link', { name: 'Create account' })).toHaveAttribute('href', '/register');
  });

  it('offers neither link where only providers sign people in', async () => {
    config = {
      status: 200,
      body: { openRegistration: true, externalOnly: true, providers: [{ name: 'google', displayName: 'Google' }] },
    };
    at('/login');

    expect(await screen.findByRole('link', { name: /Sign in with Google/ })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Forgot your password?' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Create account' })).not.toBeInTheDocument();
  });

  it('says that the sign-in options could not be read, and reads them again on Retry', async () => {
    config = 'unreachable';
    at('/login');

    expect(await screen.findByText(/sign-in options could not be read/)).toBeInTheDocument();
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('link', { name: 'Create account' })).toBeInTheDocument();
    expect(screen.queryByText(/sign-in options could not be read/)).not.toBeInTheDocument();
  });

  it.each([
    ['auth.external_no_account', /Ask an administrator to create or invite your account/],
    ['auth.external_email_unverified', /has not verified your address/],
    ['auth.external_link_required', /link the provider from your security settings/],
    ['auth.external_no_email', /did not share an email address/],
    ['auth.external_not_allowed', /may not sign in yet/],
    ['auth.external_provider_unknown', /not set up on this installation/],
    ['auth.locked_out', /temporarily locked/],
    ['auth.external_failed', /did not complete/],
    ['auth.something_new', /did not complete/],
  ])('words a provider sign-in bounced with %s', async (code, sentence) => {
    at(`/login?error=${encodeURIComponent(code)}`);
    expect(await screen.findByText(sentence)).toBeInTheDocument();
  });
});

describe('asking for a password-reset mail', () => {
  it('posts the address and answers the same way whether or not it has an account', async () => {
    at('/forgot-password');
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'someone@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(
      await screen.findByText('If an account exists for that address, a mail is on its way with a link to choose a new password.'),
    ).toBeInTheDocument();
    expect(posted).toEqual([{ url: '/api/v1/auth/password/forgot', body: { email: 'someone@example.test' } }]);
    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
  });

  it('does not say a mail is on its way when the server could not be reached', async () => {
    answers['/api/v1/auth/password/forgot'] = 'unreachable';
    at('/forgot-password');
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'someone@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(await screen.findByText(/server could not be reached/)).toBeInTheDocument();
    expect(screen.queryByText(/a mail is on its way/)).not.toBeInTheDocument();
  });

  it('does not say a mail is on its way when the server failed', async () => {
    answers['/api/v1/auth/password/forgot'] = { status: 500 };
    at('/forgot-password');
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'someone@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(await screen.findByText(/could not complete this right now/)).toBeInTheDocument();
    expect(screen.queryByText(/a mail is on its way/)).not.toBeInTheDocument();
  });
});

describe('opening an account', () => {
  async function register(name = '') {
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'new@example.test' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a-long-enough-password' } });
    fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: 'a-long-enough-password' } });
    if (name) {
      fireEvent.change(screen.getByLabelText('Your name (optional)'), { target: { value: name } });
    }
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
  }

  it('shows no form where registration is closed, and says why', async () => {
    at('/register');
    expect(await screen.findByText(/does not open accounts on request/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('posts the address, password and name, and says a mail is coming when one was sent', async () => {
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    answers['/api/v1/auth/register'] = {
      status: 202,
      body: { email: 'new@example.test', mailSent: true, confirmationRequired: true },
    };
    at('/register');
    await register('Ana');

    expect(await screen.findByText(/a mail with the next step is on its way/)).toBeInTheDocument();
    expect(posted).toEqual([
      {
        url: '/api/v1/auth/register',
        body: { email: 'new@example.test', password: 'a-long-enough-password', displayName: 'Ana' },
      },
    ]);
  });

  // The server answers an address that already has an account exactly as it answers a new one, and
  // leaves that account's password as it was. So the sentence must hold for both, and must not
  // promise that the password just typed now works.
  it('words the no-mail answer so it holds for a taken address too, and points to a password reset', async () => {
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    answers['/api/v1/auth/register'] = {
      status: 202,
      body: { email: 'new@example.test', mailSent: false, confirmationRequired: false },
    };
    at('/register');
    await register();

    expect(await screen.findByText(/If this address was new here, you can now sign in with it/)).toBeInTheDocument();
    expect(screen.queryByText(/You can now sign in with this address and password/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Forgot your password?' })).toHaveAttribute('href', '/forgot-password');
    expect(posted[0]?.body.displayName).toBeNull();
  });

  it('says the address must be confirmed by an administrator when the confirmation mail could not go', async () => {
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    answers['/api/v1/auth/register'] = {
      status: 202,
      body: { email: 'new@example.test', mailSent: false, confirmationRequired: true },
    };
    at('/register');
    await register();

    expect(await screen.findByText(/no mail could be sent to confirm the address/)).toBeInTheDocument();
  });

  it('words a refused password, and a registration closed since the page was opened', async () => {
    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    answers['/api/v1/auth/register'] = { status: 400, body: { code: 'auth.registration_invalid', errors: {} } };
    at('/register');
    await register();
    expect(await screen.findByText(/was not accepted/)).toBeInTheDocument();

    answers['/api/v1/auth/register'] = { status: 403, body: { code: 'auth.registration_disabled' } };
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText(/does not open accounts on request/)).toBeInTheDocument();
  });

  it('says the page could not learn whether registration is open, with a Retry', async () => {
    config = { status: 503 };
    at('/register');
    expect(await screen.findByText(/sign-in options could not be read/)).toBeInTheDocument();

    config = { status: 200, body: { openRegistration: true, externalOnly: false, providers: [] } };
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByLabelText('Password')).toBeInTheDocument();
  });
});
