// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import ResetPasswordPage from './ResetPasswordPage.tsx';

/**
 * The page a reset link lands on: the access keys go with the old password only when the reader
 * says so, and an invitation (a new account, so no keys) is not asked.
 */

const fetchMock = vi.fn();

beforeEach(async () => {
  await i18n.changeLanguage('en');
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function show(query: string) {
  return render(
    <MemoryRouter initialEntries={[`/reset-password?${query}`]}>
      <Routes>
        <Route path="/reset-password" element={<ResetPasswordPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

function fillAndSend() {
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a-long-new-password' } });
  fireEvent.change(screen.getByLabelText('Repeat password'), { target: { value: 'a-long-new-password' } });
  fireEvent.click(screen.getByRole('button', { name: /reset|set|save|password/i }));
}

function sentBody(): Record<string, unknown> {
  const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

describe('ResetPasswordPage', () => {
  it('keeps the access keys unless the switch is turned on', async () => {
    show('email=invented%40example.test&token=invented-token');
    expect(screen.getByRole('switch', { name: 'Also revoke my access keys' })).not.toBeChecked();
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toMatchObject({ revokeAccessKeys: false, newPassword: 'a-long-new-password' });
  });

  it('revokes the access keys too when the reader asks', async () => {
    show('email=invented%40example.test&token=invented-token');
    fireEvent.click(screen.getByRole('switch', { name: 'Also revoke my access keys' }));
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toMatchObject({ revokeAccessKeys: true });
  });

  it('does not ask an invited account, which has no keys', async () => {
    show('email=invented%40example.test&token=invented-token&invite=1');
    expect(screen.queryByRole('switch', { name: 'Also revoke my access keys' })).toBeNull();
    fillAndSend();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(sentBody()).toMatchObject({ revokeAccessKeys: false });
  });
});
