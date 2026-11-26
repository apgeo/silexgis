// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import en from '../i18n/locales/en.json';
import ro from '../i18n/locales/ro.json';
import RegisterPage from './RegisterPage.tsx';
import ResetPasswordPage from './ResetPasswordPage.tsx';

// A password rule learnt only from a refusal costs every new member a failed submit; the rule is
// written under the field before anything is typed.
vi.mock('../auth/useAuthConfig.ts', () => ({
  useAuthConfig: () => ({ config: { openRegistration: true }, failed: false, reload: vi.fn() }),
}));

beforeEach(async () => {
  await i18n.changeLanguage('en');
});
afterEach(cleanup);

describe('the password rule', () => {
  it('is shown under the password field when registering, before anything is sent', () => {
    render(
      <MemoryRouter>
        <RegisterPage />
      </MemoryRouter>,
    );
    expect(screen.getByText(en.auth.passwordRule)).toBeTruthy();
    expect(ro.auth.passwordRule).toBeTruthy();
  });

  it('is shown under the new password when resetting one', () => {
    render(
      <MemoryRouter initialEntries={['/reset-password?email=invented%40example.test&token=invented-token']}>
        <ResetPasswordPage />
      </MemoryRouter>,
    );
    expect(screen.getByText(en.auth.passwordRule)).toBeTruthy();
  });
});
