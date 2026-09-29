// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';

/**
 * The survey address the viewer is handed, as every render sees it — not only the last one.
 *
 * <b>The renders in between are what a reader can be shown.</b> A page that draws "there is no
 * survey drawing for this trip" whenever this answers null would say so, inside somebody's
 * article, for every render in which an address was in hand and this had not caught up with it.
 * So what is recorded here is each answer in turn, and the claim is about all of them.
 */
function rendered(initial: { url: string | null | undefined; token: string | undefined }) {
  const seen: (string | null)[] = [];
  const hook = renderHook(
    ({ url, token }) => {
      const pinned = usePinnedModelUrl(url, token);
      seen.push(pinned);
      return pinned;
    },
    { initialProps: initial },
  );
  return { ...hook, seen };
}

const FIRST = '/api/v1/files/abc/content?token=first';
const RESIGNED = '/api/v1/files/abc/content?token=second';
const OTHER = '/api/v1/files/def/content?token=first';

describe('the address the viewer is handed', () => {
  it('is the address from the very first render that has one', () => {
    const { seen } = rendered({ url: FIRST, token: 'follow-token' });

    expect(seen.length).toBeGreaterThan(0);
    expect(seen.every((pinned) => pinned === FIRST)).toBe(true);
  });

  it('is never null in a render that arrived with an address', () => {
    const { rerender, seen } = rendered({ url: undefined, token: 'follow-token' });
    seen.length = 0;

    rerender({ url: FIRST, token: 'follow-token' });

    expect(seen).not.toContain(null);
  });

  it('holds still across a re-signed address for the same file', () => {
    const { rerender, result } = rendered({ url: FIRST, token: 'follow-token' });

    rerender({ url: RESIGNED, token: 'follow-token' });

    expect(result.current).toBe(FIRST);
  });

  it('moves to a different file the moment it is named', () => {
    const { rerender, seen } = rendered({ url: FIRST, token: 'follow-token' });
    seen.length = 0;

    rerender({ url: OTHER, token: 'follow-token' });

    expect(seen.every((pinned) => pinned === OTHER)).toBe(true);
  });

  it('keeps what it had through a read that carried no survey', () => {
    const { rerender, result } = rendered({ url: FIRST, token: 'follow-token' });

    rerender({ url: undefined, token: 'follow-token' });

    expect(result.current).toBe(FIRST);
  });

  it('starts again on another trip, and never hands one trip the other’s drawing', () => {
    const { rerender, seen } = rendered({ url: FIRST, token: 'follow-token' });
    seen.length = 0;

    // The past trip's survey has not been read yet: nothing is drawn rather than the live one.
    rerender({ url: undefined, token: 'follow-token:past' });
    expect(seen.length).toBeGreaterThan(0);
    expect(seen.every((pinned) => pinned === null)).toBe(true);

    // And the same address arriving for it is pinned for it, even though it is the same string.
    seen.length = 0;
    rerender({ url: RESIGNED, token: 'follow-token:past' });
    expect(seen.every((pinned) => pinned === RESIGNED)).toBe(true);
  });
});
