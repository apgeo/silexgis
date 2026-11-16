// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';

const signinRedirectCallback = vi.fn();
vi.mock('../auth/auth.tsx', () => ({
  userManager: { signinRedirectCallback: () => signinRedirectCallback() },
}));

const signInAgain = vi.fn(async (_returnTo: string) => {});
vi.mock('../auth/renewSignIn.ts', () => ({
  signInAgain: (returnTo: string) => signInAgain(returnTo),
}));

const { default: CallbackPage } = await import('./CallbackPage.tsx');

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/auth/callback?code=c&state=s']}>
        <Routes>
          <Route path="/auth/callback" element={<CallbackPage />} />
          <Route path="/" element={<div data-testid="home" />} />
          <Route path="/caves/:id" element={<div data-testid="cave" />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
});

afterEach(() => {
  cleanup();
  signinRedirectCallback.mockReset();
  signInAgain.mockClear();
});

/**
 * <b>The return from the sign-in server finishes the sign-in, or stops and says it could not.</b>
 * Every page that needs an account sends a visitor with no sign-in to the sign-in server, and that
 * server answers at once while its own session lasts. So a return that fails and then goes to any
 * such page is sent straight back here, fails the same way, and goes round for as long as the tab
 * is open — a spinner, no message, and one more sign-in request each time against a limit that is
 * shared by everyone behind the same address. A failed return stays here until the person acts.
 */
describe('the return from signing in', () => {
  it('goes back to the page the sign-in began from', async () => {
    signinRedirectCallback.mockResolvedValue({ state: '/caves/7' });
    show();

    expect(await screen.findByTestId('cave')).toBeTruthy();
  });

  it('a failed return stays here, says so, and does not begin signing in again by itself', async () => {
    signinRedirectCallback.mockRejectedValue(new Error('iat is in the future'));
    show();

    const failed = await screen.findByTestId('sign-in-return-failed');
    expect(failed.textContent).toContain(i18n.t('auth.callbackFailed'));
    // Give a navigation the time it would take; neither the home page nor a new sign-in follows.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByTestId('home')).toBeNull();
    expect(signInAgain).not.toHaveBeenCalled();
  });

  it('signing in again is the person\'s choice, once per press', async () => {
    signinRedirectCallback.mockRejectedValue(new Error('No matching state found in storage'));
    show();

    fireEvent.click(await screen.findByRole('button', { name: i18n.t('common.retry') }));

    await waitFor(() => expect(signInAgain).toHaveBeenCalledTimes(1));
    expect(signInAgain).toHaveBeenCalledWith('/');
    // The code a failed return carried is spent; it is never exchanged a second time.
    expect(signinRedirectCallback).toHaveBeenCalledTimes(1);
  });
});
