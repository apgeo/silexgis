// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The outgoing-mail section. The stored password is never sent to the browser: an empty field, or
 * the placeholder standing in for the stored one, must save as "leave it alone" (null), and only a
 * typed password replaces it. A value the server refuses is said as such, not as a dropped
 * connection. The server and addresses below are invented.
 */

const put = vi.fn();
const post = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: { GET: vi.fn(), POST: (...args: unknown[]) => post(...args), PUT: (...args: unknown[]) => put(...args) },
  };
});

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    queryKeys: actual.queryKeys,
    useAdminSettings: () => ({ data: undefined, isLoading: false }),
    useCapabilities: () => ({ data: undefined }),
    useMe: () => ({ data: undefined }),
    usePhotoLibraries: () => ({ data: undefined }),
  };
});

const { default: MailForm } = await import('./MailForm.tsx');

const onSaved = vi.fn();

const mail = {
  enabled: true,
  host: 'smtp.example.org',
  port: 587,
  security: 'auto',
  username: 'mailer',
  hasPassword: true,
  fromAddress: 'silexgis@example.org',
  fromName: 'Example',
  replyTo: '',
  timeoutSeconds: 30,
  acceptInvalidCertificate: false,
};

function show() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <MailForm
          settings={{ mail, mailConfigured: true } as unknown as AdminSettings}
          onSaved={onSaved}
          defaultTestRecipient=""
        />
      </App>
    </QueryClientProvider>,
  );
}

function password() {
  return screen.getByLabelText(i18n.t('admin.messaging.password'));
}

function save() {
  fireEvent.click(screen.getByRole('button', { name: i18n.t('common.save') }));
}

/** The password the last save sent. */
function sentPassword() {
  const call = put.mock.calls[put.mock.calls.length - 1];
  return (call[1] as { body: { password: string | null } }).body.password;
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({ data: { mail }, error: undefined });
  post.mockReset();
});
afterEach(cleanup);

describe('MailForm', () => {
  it('keeps the stored password when the field is left empty', async () => {
    show();
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sentPassword()).toBeNull();
  });

  it('keeps the stored password when the placeholder itself is typed back', async () => {
    show();
    fireEvent.change(password(), { target: { value: '••••••••' } });
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sentPassword()).toBeNull();
  });

  it('sends a typed password, and empties the field once it is stored', async () => {
    show();
    fireEvent.change(password(), { target: { value: 'invented-secret' } });
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sentPassword()).toBe('invented-secret');
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect((password() as HTMLInputElement).value).toBe('');
  });

  it('says a refused value was refused, not that the save failed', async () => {
    put.mockResolvedValue({ data: undefined, error: { code: 'validation.failed', errors: { port: ['bad'] } } });
    show();
    save();
    expect(await screen.findByText(i18n.t('admin.settingsProblems.invalid'))).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('says when moving the mail server dropped the stored password, and only then', async () => {
    // A save the server answers with no notice says nothing more than "saved"...
    show();
    save();
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/so the saved password was removed/)).toBeNull();
    cleanup();

    // ...while one that moved the server, sent with no password, is told to type it again.
    put.mockResolvedValue({ data: { mail, notices: ['settings.mail_password_cleared'] }, error: undefined });
    show();
    save();
    expect(await screen.findByText(
      'The mail server changed, so the saved password was removed. Type it again.',
    )).toBeInTheDocument();
  });

  it('says the save failed when the request never reached the server', async () => {
    put.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    save();
    expect(await screen.findByText(i18n.t('common.saveFailed'))).toBeInTheDocument();
  });

  it('sends a test message, and says what the server could not deliver in its own words', async () => {
    post.mockResolvedValue({ data: { sent: false, error: 'mailbox unavailable' }, error: undefined });
    show();
    fireEvent.change(screen.getByLabelText(i18n.t('admin.messaging.testRecipient')), { target: { value: 'ana@example.org' } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('admin.messaging.sendTest') }));

    expect(await screen.findByText(i18n.t('admin.messaging.testFailed', { error: 'mailbox unavailable' }))).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith('/api/v1/admin/settings/mail/test', { body: { recipient: 'ana@example.org' } });
  });

  it('says so when the test request never reaches the server', async () => {
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    fireEvent.change(screen.getByLabelText(i18n.t('admin.messaging.testRecipient')), { target: { value: 'ana@example.org' } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('admin.messaging.sendTest') }));

    expect(await screen.findByText(i18n.t('admin.messaging.testRequestFailed'))).toBeInTheDocument();
  });
});
