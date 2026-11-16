// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The SMS gateway section. Like the mail password, the stored authorisation header is never sent
 * to the browser: a blank field or the placeholder saves as "leave it alone" (null). The test
 * text says why it could not be asked for, apart from a send that was tried and failed. The
 * gateway and numbers below are invented.
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

const { SmsForm } = await import('./MessagingSettingsPage.tsx');

const t = i18n.getFixedT('en');
const onSaved = vi.fn();

const sms = {
  enabled: true,
  url: 'https://gateway.example.org/send',
  method: 'POST',
  contentType: 'application/json',
  bodyTemplate: '{"to":"{to}","text":"{message}"}',
  hasAuthHeader: true,
  headers: { 'X-Invented': 'kept' },
  from: '+40700000000',
  timeoutSeconds: 15,
};

function show() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <SmsForm settings={{ sms, smsConfigured: true } as unknown as AdminSettings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>,
  );
}

const authHeader = () => screen.getByLabelText(t('admin.messaging.smsAuthHeader'));
const save = () => fireEvent.click(screen.getByRole('button', { name: t('common.save') }));
const sent = () => (put.mock.calls[put.mock.calls.length - 1][1] as { body: { authHeader: string | null; headers: unknown } }).body;

function sendTest() {
  fireEvent.change(screen.getByLabelText(t('admin.messaging.testRecipient')), { target: { value: '+40711111111' } });
  fireEvent.click(screen.getByRole('button', { name: t('admin.messaging.sendTest') }));
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({ data: { sms }, error: undefined });
  post.mockReset().mockResolvedValue({ data: { sent: true, error: null }, error: undefined });
});
afterEach(cleanup);

describe('SmsForm', () => {
  it('keeps the stored header when the field is left empty, and the configured headers with it', async () => {
    show();
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sent().authHeader).toBeNull();
    expect(sent().headers).toEqual({ 'X-Invented': 'kept' });
  });

  it('keeps the stored header when the placeholder itself is typed back', async () => {
    show();
    fireEvent.change(authHeader(), { target: { value: '••••••••' } });
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sent().authHeader).toBeNull();
  });

  it('sends a typed header, and empties the field once it is stored', async () => {
    show();
    fireEvent.change(authHeader(), { target: { value: 'Basic aW52ZW50ZWQ=' } });
    save();
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(sent().authHeader).toBe('Basic aW52ZW50ZWQ=');
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect((authHeader() as HTMLInputElement).value).toBe('');
  });

  it('says a refused value was refused, not that the save failed', async () => {
    put.mockResolvedValue({ data: undefined, error: { code: 'validation.failed' } });
    show();
    save();
    expect(await screen.findByText(t('admin.settingsProblems.invalid'))).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('sends a test text to the number typed', async () => {
    show();
    sendTest();
    expect(await screen.findByText(t('admin.messaging.testSent'))).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith('/api/v1/admin/settings/sms/test', { body: { recipient: '+40711111111' } });
  });

  it('says a test text asked for too soon is that, not an unconfigured channel', async () => {
    post.mockResolvedValue({ data: undefined, error: { code: 'admin.sms_test_too_soon' } });
    show();
    sendTest();
    expect(await screen.findByText(t('admin.messaging.testTooSoon'))).toBeInTheDocument();
    expect(screen.queryByText(t('admin.messaging.testNotConfigured'))).toBeNull();
  });

  it('says so when the test request never reaches the server', async () => {
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    sendTest();
    expect(await screen.findByText(t('admin.messaging.testRequestFailed'))).toBeInTheDocument();
  });
});
