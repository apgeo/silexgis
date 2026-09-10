// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
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
});
