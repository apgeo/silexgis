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

const { lastSignInRenewal, renewSignIn, renewOnReturn } = await import('./renewSignIn.ts');

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

  /**
   * A caller handed a refusal the transport already tried to renew past reads what that renewal came
   * to, rather than renewing again to find out.
   */
  it('remembers what the latest renewal came to', async () => {
    signinSilent.mockRejectedValue(new TypeError('Failed to fetch'));
    await renewSignIn();
    expect(lastSignInRenewal()).toBe('noAnswer');

    signinSilent.mockResolvedValue({ expired: false });
    await renewSignIn();
    expect(lastSignInRenewal()).toBe('renewed');
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

/**
 * A tab that comes back from sleep or an outage, holding a token that ran out while nothing could
 * renew it, renews before the screens that refetch on return are each refused once.
 */
describe('renewing when the tab comes back', () => {
  async function settle() {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  }

  it('renews a token that ran out when the connection returns, and when the tab is looked at again', async () => {
    getUser.mockResolvedValue({ refresh_token: 'kept', expires_in: -30, expired: true });
    const stop = renewOnReturn();
    try {
      window.dispatchEvent(new Event('online'));
      await settle();
      expect(signinSilent).toHaveBeenCalledTimes(1);

      document.dispatchEvent(new Event('visibilitychange'));
      await settle();
      expect(signinSilent).toHaveBeenCalledTimes(2);
    } finally {
      stop();
    }
  });

  it('leaves a token with time left alone, and stops listening when asked', async () => {
    getUser.mockResolvedValue({ refresh_token: 'kept', expires_in: 600, expired: false });
    const stop = renewOnReturn();
    window.dispatchEvent(new Event('online'));
    await settle();
    expect(signinSilent).not.toHaveBeenCalled();

    stop();
    getUser.mockResolvedValue({ refresh_token: 'kept', expires_in: -30, expired: true });
    window.dispatchEvent(new Event('online'));
    await settle();
    expect(signinSilent).not.toHaveBeenCalled();
  });

  it('asks nothing while the tab is hidden', async () => {
    getUser.mockResolvedValue({ refresh_token: 'kept', expires_in: -30, expired: true });
    const hidden = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    const stop = renewOnReturn();
    try {
      document.dispatchEvent(new Event('visibilitychange'));
      await settle();
      expect(signinSilent).not.toHaveBeenCalled();
    } finally {
      stop();
      hidden.mockRestore();
    }
  });
});
