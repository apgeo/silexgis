// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import { useLeaveGuard } from '../components/useDiscardGuard.ts';
import { useUnsavedEditsGuard } from '../components/map/useUnsavedEditsGuard.ts';
import { userManager } from './auth.tsx';
import { signOutOfThisBrowser } from './signOut.ts';

function TypedForm() {
  useLeaveGuard(() => true);
  return <p>typed work</p>;
}

function EditedMap() {
  useUnsavedEditsGuard(2);
  return <p>typed work</p>;
}

function show(page: React.ReactElement) {
  const router = createMemoryRouter([{ path: '/', element: <App>{page}</App> }], { initialEntries: ['/'] });
  render(<RouterProvider router={router} />);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('signing out of a page that holds typed work', () => {
  // Runs first: a sign-out marks the page as leaving for good, as it is in a browser.
  it('still asks when the same page is closed for any other reason', async () => {
    show(<TypedForm />);
    await screen.findByText('typed work');
    const leaving = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(leaving);
    expect(leaving.defaultPrevented).toBe(true);
  });

  // The browser's own "leave site?" question is the only one a sign-out can meet, because it leaves
  // by loading a new document rather than through the router. By the time it leaves, the renewal key
  // is cancelled and the sign-in cookie ended: a member who chose to stay would be left on a page that
  // still looks signed in, holding an access token nothing can renew.
  it.each([
    ['the cave form', <TypedForm key="form" />],
    ['the map editor', <EditedMap key="map" />],
  ])('leaves %s without the browser asking whether to stay', async (_name, page) => {
    show(page);
    await screen.findByText('typed work');
    vi.spyOn(userManager, 'stopSilentRenew').mockImplementation(() => undefined);
    vi.spyOn(userManager, 'revokeTokens').mockResolvedValue(undefined);
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 204 }))));
    const cancelled: boolean[] = [];
    vi.stubGlobal('location', {
      replace: () => {
        const leaving = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(leaving);
        cancelled.push(leaving.defaultPrevented);
      },
    });

    await signOutOfThisBrowser('/');

    expect(cancelled).toEqual([false]);
  });
});
