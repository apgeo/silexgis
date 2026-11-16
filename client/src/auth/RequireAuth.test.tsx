// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import RequireAuth from './RequireAuth.tsx';
import * as auth from './auth.tsx';

function renderGuarded() {
  return render(
    <MemoryRouter initialEntries={['/caves']}>
      <Routes>
        <Route element={<RequireAuth />}>
          <Route path="/caves" element={<div>the caves page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

function mockAuth(signIn: () => Promise<void>, state: Pick<ReturnType<typeof auth.useAuth>, 'user' | 'loading'> = { user: null, loading: false }) {
  vi.spyOn(auth, 'useAuth').mockReturnValue({
    ...state,
    signIn,
    refreshSession: () => Promise.resolve(),
  });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('RequireAuth', () => {
  it('starts the sign-in redirect carrying the requested URL', async () => {
    const signIn = vi.fn().mockResolvedValue(undefined);
    mockAuth(signIn);

    renderGuarded();

    await waitFor(() => expect(signIn).toHaveBeenCalledWith('/caves'));
  });

  // A dead identity server rejects the redirect. Without a landing state the guard would
  // sit on its spinner for good, since nothing retries the redirect on its own.
  it('reports an unreachable server instead of spinning for ever', async () => {
    mockAuth(() => Promise.reject(new Error('Invalid response Content-Type: text/html')));

    renderGuarded();

    expect(await screen.findByText('Cannot reach the server')).toBeInTheDocument();
  });

  it('retries the redirect when asked', async () => {
    const signIn = vi
      .fn()
      .mockRejectedValueOnce(new Error('down'))
      .mockResolvedValue(undefined);
    mockAuth(signIn);

    renderGuarded();
    await screen.findByText('Cannot reach the server');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(signIn).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Cannot reach the server')).not.toBeInTheDocument();
  });

  it('shows the page to a signed-in account and does not start a sign-in', async () => {
    const signIn = vi.fn().mockResolvedValue(undefined);
    mockAuth(signIn, { user: { profile: { sub: 'u1' }, expired: false } as never, loading: false });

    renderGuarded();

    expect(await screen.findByText('the caves page')).toBeInTheDocument();
    expect(signIn).not.toHaveBeenCalled();
  });

  // While the held session is still being read, nobody is known to be signed out: starting a
  // redirect then would throw away a session that was about to be found.
  it('waits, without a sign-in and without the page, while the session is being read', async () => {
    const signIn = vi.fn().mockResolvedValue(undefined);
    mockAuth(signIn, { user: null, loading: true });

    renderGuarded();

    await Promise.resolve();
    expect(screen.queryByText('the caves page')).not.toBeInTheDocument();
    expect(signIn).not.toHaveBeenCalled();
  });
});
