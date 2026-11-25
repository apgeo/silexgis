// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import i18n from '../i18n';

const signInAgain = vi.fn<(returnTo: string) => Promise<void>>();
vi.mock('./renewSignIn.ts', () => ({
  signInAgain: (returnTo: string) => signInAgain(returnTo),
}));

import SignInLapsedNotice from './SignInLapsedNotice.tsx';
import { signInLapsed, useSignInLapsed } from './signInLapsed.ts';

function renderNotice() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/trips/4?tab=report#now']}>
        <textarea defaultValue="typed and not yet saved" aria-label="report" />
        <SignInLapsedNotice />
      </MemoryRouter>
    </App>,
  );
}

/**
 * A sign-in that has ended is said, and signing in again — which leaves the page — waits for the
 * person to press the button. Until then the page, and any form on it, stays where it is.
 */
describe('the notice that the sign-in has ended', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
    signInAgain.mockReset().mockResolvedValue(undefined);
    useSignInLapsed.setState({ lapsed: false });
  });

  afterEach(() => {
    cleanup();
  });

  it('shows nothing while the sign-in holds', () => {
    renderNotice();

    expect(screen.queryByText('Your sign-in has ended')).not.toBeInTheDocument();
  });

  it('says so, and leaves the page only when the person asks to sign in again', async () => {
    renderNotice();

    signInLapsed();

    expect(await screen.findByText('Your sign-in has ended')).toBeInTheDocument();
    expect(signInAgain).not.toHaveBeenCalled();
    expect(screen.getByLabelText('report')).toHaveValue('typed and not yet saved');

    fireEvent.click(screen.getByTestId('sign-in-lapsed-sign-in'));

    expect(signInAgain).toHaveBeenCalledTimes(1);
    expect(signInAgain).toHaveBeenCalledWith('/trips/4?tab=report#now');
  });

  it('lets the person stay, and comes back with the next refusal', async () => {
    renderNotice();
    signInLapsed();
    fireEvent.click(await screen.findByTestId('sign-in-lapsed-stay'));

    await waitFor(() => expect(useSignInLapsed.getState().lapsed).toBe(false));
    expect(signInAgain).not.toHaveBeenCalled();

    signInLapsed();
    expect(useSignInLapsed.getState().lapsed).toBe(true);
  });

  it('says the sign-in server cannot be reached, and stays, when the redirect cannot start', async () => {
    signInAgain.mockRejectedValue(new Error('down'));
    renderNotice();
    signInLapsed();

    fireEvent.click(await screen.findByTestId('sign-in-lapsed-sign-in'));

    expect(await screen.findByText('Cannot reach the server')).toBeInTheDocument();
    expect(screen.getByText('Your sign-in has ended')).toBeInTheDocument();
  });
});
