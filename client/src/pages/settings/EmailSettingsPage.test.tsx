// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

/** The email settings page when the account itself could not be read. */

const t = i18n.getFixedT('en');
const refetch = vi.fn();
let meRead: Record<string, unknown> = {};

const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false });
vi.mock('../../api/hooks.ts', () => ({
  useMe: () => meRead,
  useRequestEmailChange: idle,
  useConfirmEmailChange: idle,
  useResendEmailChange: idle,
  useCancelEmailChange: idle,
  useVerifyEmail: idle,
}));
vi.mock('../../auth/auth.tsx', () => ({ useAuth: () => ({ refreshSession: vi.fn() }) }));

const { default: EmailSettingsPage } = await import('./EmailSettingsPage.tsx');

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/settings/email']}>
        <EmailSettingsPage />
      </MemoryRouter>
    </App>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  refetch.mockReset();
});
afterEach(cleanup);

describe('a failed read of the account', () => {
  it('says so and offers Retry instead of spinning for ever', () => {
    meRead = { data: undefined, isPending: false, isError: true, error: new TypeError('offline'), refetch };
    show();
    expect(screen.getByTestId('email-settings-load-failed')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });
});
