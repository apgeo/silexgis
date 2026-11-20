// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { claimHomeView, forgetHomeViewClaimForTests } from './homeViewClaim.ts';

afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
  forgetHomeViewClaimForTests();
});

describe('claiming the once-per-session home view', () => {
  it('is granted once in a session and refused after that', () => {
    expect(claimHomeView()).toBe(true);
    expect(claimHomeView()).toBe(false);
    // The claim outlives the page that made it, as the session it stands for does.
    forgetHomeViewClaimForTests();
    expect(claimHomeView()).toBe(false);
  });

  it('works without throwing where the browser refuses session storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });

    expect(claimHomeView()).toBe(true);
    // Still once: without storage the claim is held for as long as the page is loaded.
    expect(claimHomeView()).toBe(false);
  });
});
