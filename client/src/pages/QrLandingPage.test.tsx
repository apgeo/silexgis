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
  // A phone at a cave entrance: nobody is signed in.
  userManager: { getUser: async () => null },
  useAuth: () => ({ signIn: async () => {} }),
}));

const { api } = await import('../api/client.ts');
const { default: QrLandingPage } = await import('./QrLandingPage.tsx');

async function answerLikeTheClient() {
  const response = await network.answer();
  const body: unknown = await response.clone().json().catch(() => undefined);
  return response.ok ? { data: body, response } : { error: body, response };
}

const json = (status: number, body: unknown, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } });
const problem = (status: number, code: string) => json(status, { status, code }, 'application/problem+json');

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/q/ABC123']}>
        <Routes>
          <Route path="/q/:code" element={<QrLandingPage />} />
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
 * <b>A printed label says which installation it belongs to, says "nothing to show" when the server
 * refuses it, and never says that about a read that got no answer.</b> A settled refusal is the
 * same page whatever its reason, so the page tells nobody which codes exist; a lost connection, a
 * server error or a request refused for coming too often says nothing about the code and is offered
 * again instead.
 */
describe('the page a scanned cave label opens', () => {
  it('names the installation a published code belongs to', async () => {
    network.answer = async () => json(200, { instanceName: 'Invented club' });
    show();
    expect(await screen.findByText(i18n.t('qr.landingFoundBody', { instance: 'Invented club' }))).toBeTruthy();
  });

  it('a refused code says there is nothing to show, with nothing to retry', async () => {
    network.answer = async () => problem(404, 'qr.not_found');
    show();
    expect(await screen.findByText(i18n.t('qr.landingUnknownTitle'))).toBeTruthy();
    expect(screen.queryByTestId('qr-landing-unreachable')).toBeNull();
    expect(screen.queryByRole('button', { name: i18n.t('common.retry') })).toBeNull();
  });

  it.each([
    ['no connection', () => Promise.reject(new TypeError('Failed to fetch'))],
    ['a server error', () => Promise.resolve(problem(503, 'server.unavailable'))],
    ['too many scans from one connection', () => Promise.resolve(problem(429, 'rate_limited'))],
  ])('%s is a read that failed, never "nothing to show for this code"', async (_name, answer) => {
    network.answer = answer;
    show();

    expect(await screen.findByTestId('qr-landing-unreachable')).toBeTruthy();
    expect(screen.queryByText(i18n.t('qr.landingUnknownTitle'))).toBeNull();

    network.answer = async () => json(200, { instanceName: 'Invented club' });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(await screen.findByText(i18n.t('qr.landingFoundBody', { instance: 'Invented club' }))).toBeTruthy();
  });
});
