// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

/**
 * The installation settings page: it reads the stored settings once, hands each section to its
 * own form, and keeps what a section saves — or reads everything again when a section was saved
 * over since it was read. The sign-in policy section is the real one, so a save goes through the
 * same client call it makes in use; every other section is a stand-in, each tested on its own.
 */

const get = vi.fn();
const put = vi.fn();

vi.mock('../../api/client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client.ts')>()),
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
  },
}));

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    ...actual,
    useCapabilities: () => ({ data: { domains: { settings: 'read, write' } } }),
    useMe: () => ({ data: { email: 'admin@invented.example' } }),
  };
});

// Every section but the sign-in policy, each reduced to a mark saying it was handed the settings.
vi.mock('./MailForm.tsx', () => ({ default: () => <div data-testid="section-mail" /> }));
for (const name of [
  'SmsForm',
  'ProtectionForm',
  'ImportForm',
  'NotificationsForm',
  'AnnouncementsForm',
  'PhotoLibrariesForm',
  'SurveyUploadsSettingsForm',
  'MapSymbolsSettingsForm',
  'TestDataSettingsForm',
  'WorkspaceDefaultForm',
  'SyncSettingsForm',
  'MapBackgroundsForm',
  'SelfHostedBasemapPanel',
]) {
  vi.doMock(`./${name}.tsx`, () => ({ default: () => null }));
}

const { default: MessagingSettingsPage } = await import('./MessagingSettingsPage.tsx');

const t = i18n.getFixedT('en');

const security = {
  requireConfirmedEmail: false,
  sendConfirmationOnRegistration: true,
  authenticatorTwoFactorEnabled: true,
  emailTwoFactorEnabled: false,
  smsTwoFactorEnabled: false,
  twoFactorCodeLifetimeMinutes: 10,
  twoFactorResendIntervalSeconds: 60,
};

function stored(overrides: Partial<typeof security> = {}, version = 'v1') {
  return {
    security: { ...security, ...overrides },
    mailConfigured: true,
    smsConfigured: false,
    versions: { security: version },
  };
}

function answer(data: unknown, status = 200) {
  return { data, error: undefined, response: new Response(null, { status }) };
}

function refusal(problem: Record<string, unknown>, status: number) {
  return { data: undefined, error: problem, response: new Response(null, { status }) };
}

let client: QueryClient;

function show() {
  render(
    <QueryClientProvider client={client}>
      <App>
        <MessagingSettingsPage />
      </App>
    </QueryClientProvider>,
  );
}

async function openPolicy() {
  fireEvent.click(await screen.findByRole('tab', { name: new RegExp(t('admin.messaging.policyTab')) }));
  return screen.findByRole('spinbutton', { name: t('admin.messaging.codeLifetime') });
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  get.mockReset();
  put.mockReset();
  // No retries: a failed read is shown at once rather than after the installation's back-off.
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});
afterEach(cleanup);

describe('MessagingSettingsPage', () => {
  it('reads the stored settings and hands them to the sections', async () => {
    get.mockResolvedValue(answer(stored({ twoFactorCodeLifetimeMinutes: 12 })));
    show();

    expect(await screen.findByTestId('section-mail')).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith('/api/v1/admin/settings');
    expect(await openPolicy()).toHaveValue('12');
    expect(screen.queryByTestId('messaging-settings-load-failed')).toBeNull();
  });

  it('names a failed read instead of an empty page, and reads again on Retry', async () => {
    get.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValue(answer(stored()));
    show();

    const failed = await screen.findByTestId('messaging-settings-load-failed');
    expect(failed).toHaveTextContent(t('admin.settingsLoadFailed'));
    expect(screen.queryByTestId('section-mail')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(await screen.findByTestId('section-mail')).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
    expect(screen.queryByTestId('messaging-settings-load-failed')).toBeNull();
  });

  it('says a refused read as a refusal, with no Retry that would only be refused again', async () => {
    get.mockResolvedValue(refusal({ code: 'access.forbidden', status: 403 }, 403));
    show();

    const refused = await screen.findByTestId('messaging-settings-load-failed');
    expect(refused).toHaveAttribute('data-refused', 'true');
    expect(screen.queryByRole('button', { name: t('common.retry') })).toBeNull();
  });

  it('keeps what a section saved, held to the version it was read at', async () => {
    get.mockResolvedValue(answer(stored()));
    put.mockResolvedValue(answer(stored({ twoFactorCodeLifetimeMinutes: 15 }, 'v2')));
    show();

    fireEvent.change(await openPolicy(), { target: { value: '15' } });
    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));

    expect(await screen.findByText(t('common.saved'))).toBeInTheDocument();
    expect(put).toHaveBeenCalledWith(
      '/api/v1/admin/settings/security',
      expect.objectContaining({ headers: { 'If-Match': '"v1"' } }),
    );
    // The page now holds the saved answer: the next save of the section is held to its version.
    expect(client.getQueryData<{ versions: { security: string } }>(['admin', 'settings'])?.versions.security).toBe('v2');
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('says a save refused because the section changed since it was read, and reads the settings again', async () => {
    get
      .mockResolvedValueOnce(answer(stored()))
      .mockResolvedValue(answer(stored({ twoFactorCodeLifetimeMinutes: 20 }, 'v3')));
    put.mockResolvedValue(refusal({ code: 'concurrency.version_mismatch', status: 412 }, 412));
    show();

    fireEvent.change(await openPolicy(), { target: { value: '15' } });
    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));

    expect(await screen.findByText(t('common.conflict'))).toBeInTheDocument();
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
    // The form refills from what is stored now, not from the value that was refused.
    await waitFor(() =>
      expect(screen.getByRole('spinbutton', { name: t('admin.messaging.codeLifetime') })).toHaveValue('20'),
    );
    expect(screen.queryByText(t('common.saved'))).toBeNull();
  });
});
