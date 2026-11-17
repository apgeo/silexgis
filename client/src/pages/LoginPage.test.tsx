// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import LoginPage from './LoginPage.tsx';

function stubAuthConfig(config: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve({ ok: true, json: () => Promise.resolve(config) } as unknown as Response),
    ),
  );
}

describe('LoginPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders email, password and submit', () => {
    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('says the ways to sign in could not be read, and reads them again on Retry', async () => {
    const answers = [
      Promise.resolve({ ok: false, status: 503, json: () => Promise.resolve(null) } as unknown as Response),
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ openRegistration: false, externalOnly: false, providers: [], testLogins: null }),
      } as unknown as Response),
    ];
    vi.stubGlobal('fetch', vi.fn(() => answers.shift()!));

    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    expect(await screen.findByTestId('login-config-failed')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.queryByTestId('login-config-failed')).not.toBeInTheDocument());
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(2);
  });

  it('shows no demo accounts on an ordinary installation', async () => {
    stubAuthConfig({ openRegistration: false, externalOnly: false, providers: [], testLogins: null });

    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
    expect(screen.queryByText('Test installation')).not.toBeInTheDocument();
  });

  it('announces demo accounts, fills the form on click and explains each role', async () => {
    stubAuthConfig({
      openRegistration: false,
      externalOnly: false,
      providers: [],
      testLogins: [
        { email: 'admin@test.local', password: 'test-login-pass-1', role: 'administrator' },
        { email: 'editor@test.local', password: 'test-login-pass-1', role: 'editor' },
        { email: 'viewer@test.local', password: 'test-login-pass-1', role: 'viewer' },
      ],
    });

    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Test installation')).toBeInTheDocument();
    // The credentials themselves are printed — showing them is the feature.
    expect(screen.getByText('admin@test.local · test-login-pass-1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editor' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Viewer' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Administrator' }));
    expect(screen.getByLabelText('Email')).toHaveValue('admin@test.local');
    expect(screen.getByLabelText('Password')).toHaveValue('test-login-pass-1');

    // The info button opens the description of what that account may do.
    fireEvent.click(screen.getAllByRole('button', { name: 'What this account may do' })[0]);
    expect(
      await screen.findByText(/Full control of the installation/),
    ).toBeInTheDocument();
  });

  describe('after an external provider held the sign-in at its second factor', () => {
    const returnUrl = '/connect/authorize?client_id=silexgis-spa&state=s';
    const entry = `/login?mfa=required&returnUrl=${encodeURIComponent(returnUrl)}`;

    function stubPendingSignIn(verify: (body: Record<string, unknown>) => { status: number; body: unknown }) {
      const calls: Record<string, unknown>[] = [];
      vi.stubGlobal(
        'fetch',
        vi.fn((url: string, init?: RequestInit) => {
          if (url === '/api/v1/auth/2fa/verify') {
            const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>;
            calls.push(body);
            const answer = verify(body);
            return Promise.resolve({
              ok: answer.status < 300,
              status: answer.status,
              json: () => Promise.resolve(answer.body),
            } as unknown as Response);
          }
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve({ openRegistration: false, externalOnly: false, providers: [] }),
          } as unknown as Response);
        }),
      );
      return calls;
    }

    it('asks for the code alone and resumes the authorize flow once it is accepted', async () => {
      const assign = vi.fn();
      vi.stubGlobal('location', { ...window.location, assign });
      const calls = stubPendingSignIn((body) =>
        body.code
          ? { status: 200, body: { userId: 'u', email: 'a@t.local', displayName: null } }
          : {
              status: 401,
              body: {
                code: 'auth.mfa_required',
                methods: ['authenticator'],
                preferredMethod: 'authenticator',
                recoveryAccepted: true,
              },
            },
      );

      render(
        <MemoryRouter initialEntries={[entry]}>
          <LoginPage />
        </MemoryRouter>,
      );

      const code = await screen.findByLabelText('Two-factor code');
      // The provider already vouched for the account: no password is asked for, and no provider
      // buttons are offered in the middle of a sign-in.
      expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
      expect(screen.getByText('Enter your second-factor code.')).toBeInTheDocument();

      fireEvent.change(code, { target: { value: '123456' } });
      fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

      await waitFor(() => expect(assign).toHaveBeenCalledWith(returnUrl));
      expect(calls.at(-1)).toEqual({ code: '123456', method: 'authenticator' });
    });

    // Only the authorization server's own resume address is followed after a sign-in: anything
    // else in returnUrl — another site, a protocol-relative address that the browser reads as
    // another host, or an ordinary page — lands on the home page instead.
    it.each([
      ['another site', 'https://elsewhere.example/connect/authorize'],
      ['a protocol-relative address', '//host.example/connect/authorize'],
      ['a backslash address the browser reads as another host', '/\\host.example/connect/authorize'],
      ['an ordinary page', '/caves'],
    ])('does not follow %s in returnUrl once the code is accepted', async (_label, foreign) => {
      const assign = vi.fn();
      vi.stubGlobal('location', { ...window.location, assign });
      stubPendingSignIn((body) =>
        body.code
          ? { status: 200, body: { userId: 'u', email: 'a@t.local', displayName: null } }
          : {
              status: 401,
              body: {
                code: 'auth.mfa_required',
                methods: ['authenticator'],
                preferredMethod: 'authenticator',
                recoveryAccepted: true,
              },
            },
      );

      render(
        <MemoryRouter initialEntries={[`/login?mfa=required&returnUrl=${encodeURIComponent(foreign)}`]}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<p>home page</p>} />
          </Routes>
        </MemoryRouter>,
      );

      fireEvent.change(await screen.findByLabelText('Two-factor code'), { target: { value: '123456' } });
      fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

      expect(await screen.findByText('home page')).toBeInTheDocument();
      expect(assign).not.toHaveBeenCalled();
    });

    it('sends a recovery code with its prefix and says so when the code is wrong', async () => {
      const calls = stubPendingSignIn((body) =>
        body.code
          ? { status: 401, body: { code: 'auth.mfa_invalid' } }
          : {
              status: 401,
              body: {
                code: 'auth.mfa_required',
                methods: ['authenticator'],
                preferredMethod: 'authenticator',
                recoveryAccepted: true,
              },
            },
      );

      render(
        <MemoryRouter initialEntries={[entry]}>
          <LoginPage />
        </MemoryRouter>,
      );

      fireEvent.click(await screen.findByRole('button', { name: 'Use a recovery code instead' }));
      fireEvent.change(screen.getByLabelText('Recovery code'), { target: { value: 'abcd-efgh' } });
      fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

      expect(await screen.findByText('The two-factor code is not valid.')).toBeInTheDocument();
      expect(calls.at(-1)).toEqual({ code: 'recovery:abcd-efgh' });
    });

    it('offers to start again when no sign-in is waiting any more', async () => {
      stubPendingSignIn(() => ({ status: 401, body: { code: 'auth.mfa_not_pending' } }));

      render(
        <MemoryRouter initialEntries={[entry]}>
          <LoginPage />
        </MemoryRouter>,
      );

      expect(
        await screen.findByText('The sign-in waited too long for its code. Start again.'),
      ).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Start signing in again' })).toHaveAttribute('href', '/login');
      expect(screen.queryByLabelText('Two-factor code')).not.toBeInTheDocument();
    });
  });
});
