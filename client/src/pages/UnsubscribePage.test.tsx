// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import UnsubscribePage from './UnsubscribePage.tsx';

const fetchSpy = vi.fn();

function show(path = '/unsubscribe?token=signed-token') {
  return render(
    <App>
      <MemoryRouter initialEntries={[path]}>
        <UnsubscribePage />
      </MemoryRouter>
    </App>,
  );
}

function answer(status: number, body: unknown = {}) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
}

beforeEach(() => {
  fetchSpy.mockReset();
  vi.stubGlobal('fetch', fetchSpy);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('UnsubscribePage', () => {
  it('says what stopped when the link is honoured', async () => {
    fetchSpy.mockReturnValue(answer(200, { kind: 'category', category: null }));
    show();
    expect(
      await screen.findByText(i18n.t('unsubscribe.done', { category: i18n.t('unsubscribe.theseMessages') })),
    ).toBeTruthy();
    const [url, init] = fetchSpy.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/v1/notifications/unsubscribe');
    expect(JSON.parse(init.body as string)).toEqual({ token: 'signed-token' });
  });

  it('says the link is no longer valid when the server refuses it', async () => {
    fetchSpy.mockReturnValue(answer(400, { code: 'unsubscribe.token_invalid' }));
    show();
    expect(await screen.findByText(i18n.t('unsubscribe.failed'))).toBeTruthy();
    expect(screen.queryByRole('button', { name: i18n.t('common.retry') })).toBeNull();
  });

  it('says the link is no longer valid when it carries no token, without asking the server', async () => {
    show('/unsubscribe');
    expect(await screen.findByText(i18n.t('unsubscribe.failed'))).toBeTruthy();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  // A lost connection says nothing about the link. Calling it invalid sends somebody who wanted
  // the mail to stop away believing it cannot be stopped this way.
  it('says the server could not be reached, and asks again on Retry, when the request does not land', async () => {
    fetchSpy.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    fetchSpy.mockReturnValueOnce(answer(200, { kind: 'dailyDigest', category: null }));
    show();
    expect(await screen.findByTestId('unsubscribe-unreachable')).toBeTruthy();
    expect(screen.queryByText(i18n.t('unsubscribe.failed'))).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(await screen.findByText(i18n.t('unsubscribe.doneDigest'))).toBeTruthy();
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('treats a server error or a request asked too often as not reaching it, not as an invalid link', async () => {
    fetchSpy.mockReturnValueOnce(answer(503));
    show();
    expect(await screen.findByTestId('unsubscribe-unreachable')).toBeTruthy();
    cleanup();

    fetchSpy.mockReturnValueOnce(answer(429));
    show();
    expect(await screen.findByTestId('unsubscribe-unreachable')).toBeTruthy();
    expect(screen.queryByText(i18n.t('unsubscribe.failed'))).toBeNull();
  });
});
