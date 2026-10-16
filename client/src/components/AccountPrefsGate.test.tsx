// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

vi.mock('../api/client.ts', () => ({
  api: {
    GET: () => new Promise(() => {}),
    PATCH: () => new Promise(() => {}),
  },
  ApiError: class extends Error {},
  retryAfterOf: () => undefined,
}));

const { default: AccountPrefsGate } = await import('./AccountPrefsGate.tsx');

afterEach(() => {
  cleanup();
  useUiPrefsStore.getState().releaseAccount();
});

/** A screen that reads a preference as it is drawn, and remembers every value it was drawn with. */
const seen: string[] = [];
function Workspace() {
  const format = useUiPrefsStore((s) => s.coordinateFormat);
  seen.push(format);
  return <div>workspace</div>;
}

describe('AccountPrefsGate', () => {
  it('never draws the shell with the arrangement of the member who used this browser before', () => {
    useUiPrefsStore.getState().adoptAccount('account-b');
    useUiPrefsStore.getState().setCoordinateFormat('utm');
    seen.length = 0;

    render(
      <QueryClientProvider client={new QueryClient()}>
        <AccountPrefsGate accountId="account-a">
          <Workspace />
        </AccountPrefsGate>
      </QueryClientProvider>,
    );

    expect(screen.getByText('workspace')).toBeTruthy();
    expect(seen).not.toContain('utm');
    expect(useUiPrefsStore.getState().accountId).toBe('account-a');
  });

  it('stays drawn, with the defaults, when a sign-out releases the cache', () => {
    useUiPrefsStore.getState().adoptAccount('account-a');
    render(
      <QueryClientProvider client={new QueryClient()}>
        <AccountPrefsGate accountId="account-a">
          <Workspace />
        </AccountPrefsGate>
      </QueryClientProvider>,
    );

    act(() => useUiPrefsStore.getState().releaseAccount());

    expect(screen.getByText('workspace')).toBeTruthy();
  });
});
