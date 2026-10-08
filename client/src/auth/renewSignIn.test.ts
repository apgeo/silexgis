// SPDX-License-Identifier: AGPL-3.0-or-later
import { ErrorResponse } from 'oidc-client-ts';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const getUser = vi.fn();
const signinSilent = vi.fn();
vi.mock('./auth.tsx', () => ({
  userManager: {
    getUser: () => getUser(),
    signinSilent: () => signinSilent(),
  },
}));

const { renewSignIn } = await import('./renewSignIn.ts');

beforeEach(() => {
  getUser.mockReset().mockResolvedValue({ refresh_token: 'kept', expired: true });
  signinSilent.mockReset().mockResolvedValue({ expired: false });
});

/**
 * What a request refused as coming from nobody does next turns on which of three things a renewal
 * came to, and two of them look alike from outside: both leave the tab without a usable token.
 */
describe('renewing the sign-in a tab holds', () => {
  it('says renewed when the server hands back a token that has not run out', async () => {
    expect(await renewSignIn()).toBe('renewed');
  });

  it('says the sign-in is over, asking the server nothing, when there is nothing to renew it with', async () => {
    getUser.mockResolvedValue({ expired: true });
    expect(await renewSignIn()).toBe('lapsed');

    getUser.mockResolvedValue(null);
    expect(await renewSignIn()).toBe('lapsed');

    expect(signinSilent).not.toHaveBeenCalled();
  });

  it('says the sign-in is over when the server refuses the renewal', async () => {
    signinSilent.mockRejectedValue(new ErrorResponse({ error: 'invalid_grant' }));

    expect(await renewSignIn()).toBe('lapsed');
  });

  it('says the sign-in is over when the renewal hands back nobody', async () => {
    signinSilent.mockResolvedValue(null);

    expect(await renewSignIn()).toBe('lapsed');
  });

  /** No connection yet: the sign-in may be perfectly good, and must not be called over. */
  it('says nobody answered when the renewal itself got no answer', async () => {
    signinSilent.mockRejectedValue(new TypeError('Failed to fetch'));

    expect(await renewSignIn()).toBe('noAnswer');
  });

  /** A renewal token is spent by being used: two renewals at once would each spend the other's. */
  it('makes one renewal of two asked for in the same moment, and a new one afterwards', async () => {
    let finish: (user: { expired: boolean }) => void = () => undefined;
    signinSilent.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );

    const first = renewSignIn();
    const second = renewSignIn();
    await vi.waitFor(() => expect(signinSilent).toHaveBeenCalledTimes(1));
    finish({ expired: false });

    expect(await Promise.all([first, second])).toEqual(['renewed', 'renewed']);
    expect(signinSilent).toHaveBeenCalledTimes(1);

    signinSilent.mockResolvedValue({ expired: false });
    await renewSignIn();
    expect(signinSilent).toHaveBeenCalledTimes(2);
  });
});
