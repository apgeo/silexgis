// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';

// The page is read through the real generated client and the refusal it builds from a Problem
// Details answer; only the request itself is answered here.
const network = { answer: (): Promise<Response> => Promise.reject(new Error('no answer set')) };

vi.mock('../auth/auth.tsx', () => ({
  // A shared link is opened cold, by somebody with no session in that tab.
  userManager: { getUser: async () => null },
  useAuth: () => ({ signIn: async () => {} }),
}));

const { api } = await import('../api/client.ts');
const { default: SharedViewPage } = await import('./SharedViewPage.tsx');

async function answerLikeTheClient() {
  const response = await network.answer();
  const body: unknown = await response.clone().json().catch(() => undefined);
  return response.ok ? { data: body, response } : { error: body, response };
}

const problem = (status: number, code: string) =>
  new Response(JSON.stringify({ status, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/shared/views/tok-1']}>
        <Routes>
          <Route path="/shared/views/:token" element={<SharedViewPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  vi.spyOn(api, 'GET').mockImplementation((() => answerLikeTheClient()) as never);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  network.answer = () => Promise.reject(new Error('no answer set'));
});

/**
 * <b>A shared map link that the server refuses says so; one whose read got no answer never does.</b>
 * A wrong, revoked or expired link is one settled answer and one page. A lost connection, a server
 * error or a request refused for coming too often says nothing about the link, and is offered again.
 */
describe('the page a shared map-view link opens', () => {
  it('a refused link says it does not exist or was revoked, with nothing to retry', async () => {
    network.answer = async () => problem(404, 'map_view.share_not_found');
    show();

    const alert = await screen.findByTestId('shared-view-not-found');
    expect(alert.textContent).toContain(i18n.t('views.sharedNotFound'));
    expect(screen.queryByRole('button', { name: i18n.t('common.retry') })).toBeNull();
  });

  it.each([
    ['no connection', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['a server error', () => Promise.resolve(problem(500, 'server.error'))],
    ['too many requests', () => Promise.resolve(problem(429, 'rate_limited'))],
  ])('%s is a failed load with a retry, never "this view does not exist"', async (_name, answer) => {
    network.answer = answer;
    show();

    const alert = await screen.findByTestId('shared-view-load-failed');
    expect(alert.textContent).not.toContain(i18n.t('views.sharedNotFound'));

    // The retry asks the server again: its settled answer this time is what the page then shows.
    network.answer = async () => problem(404, 'map_view.share_not_found');
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(await screen.findByTestId('shared-view-not-found')).toBeTruthy();
  });
});
