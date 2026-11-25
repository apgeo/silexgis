// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { MessageTemplate } from '../../api/hooks.ts';

/**
 * Rewriting a message's wording for one language, and taking the rewrite back.
 *
 * Saving writes that one key and language; restoring the default deletes the rewrite rather than
 * storing a copy of the shipped text. Both refresh the list, so the "rewritten" mark follows what
 * the server now holds. The wording below is invented.
 */

const put = vi.fn();
const refetchTemplates = vi.fn();
// The list's read; replaced by a failed one where a test needs it.
let templatesRead: Record<string, unknown>;
const del = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      GET: vi.fn(),
      PUT: (...args: unknown[]) => put(...args),
      DELETE: (...args: unknown[]) => del(...args),
    },
  };
});

const template: MessageTemplate = {
  key: 'account.invite',
  channel: 'email',
  description: 'Sent when an administrator invites somebody.',
  placeholders: ['link'],
  locales: [
    {
      locale: 'en',
      subject: 'Join the invented club',
      body: 'Follow {link} to join.',
      defaultSubject: 'You are invited',
      defaultBody: 'Open {link}.',
      customised: true,
    },
    {
      locale: 'ro',
      subject: null,
      body: 'Deschide {link}.',
      defaultSubject: 'Invitație',
      defaultBody: 'Deschide {link}.',
      customised: false,
    },
  ],
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    queryKeys: actual.queryKeys,
    useCapabilities: () => ({ data: { domains: { messageTemplates: 'read, write' } } }),
    useMessageTemplates: () => templatesRead,
  };
});

const { default: MessageTemplatesPage } = await import('./MessageTemplatesPage.tsx');

const t = i18n.getFixedT('en');
let client: QueryClient;

beforeEach(async () => {
  await i18n.changeLanguage('en');
  refetchTemplates.mockReset();
  templatesRead = { data: [template], isLoading: false, isError: false, error: null, refetch: refetchTemplates };
  put.mockReset().mockResolvedValue({ data: {}, error: undefined, response: new Response(null, { status: 200 }) });
  del.mockReset().mockResolvedValue({ data: undefined, error: undefined, response: new Response(null, { status: 204 }) });
  client = new QueryClient();
  vi.spyOn(client, 'invalidateQueries');
});
afterEach(cleanup);

function show() {
  render(
    <QueryClientProvider client={client}>
      <App>
        <MessageTemplatesPage />
      </App>
    </QueryClientProvider>,
  );
  // The messages are folded; open the one under test.
  fireEvent.click(screen.getByText('account.invite', { exact: false }));
}

describe('MessageTemplatesPage', () => {
  it('saves a rewrite of one message in one language', async () => {
    show();
    const body = await screen.findByLabelText(t('admin.templates.body'));
    fireEvent.change(body, { target: { value: 'Use {link} within a week.' } });
    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(put).toHaveBeenCalledWith('/api/v1/admin/message-templates/{key}/{locale}', {
      params: { path: { key: 'account.invite', locale: 'en' } },
      body: { subject: 'Join the invented club', body: 'Use {link} within a week.' },
    });
    expect(await screen.findByText(t('common.saved'))).toBeTruthy();
    expect(client.invalidateQueries).toHaveBeenCalled();
  });

  it('shows the server’s reason when it refuses a placeholder the message never gets', async () => {
    put.mockResolvedValue({
      data: undefined,
      error: { code: 'message_template.unknown_placeholder', detail: 'Unknown placeholder {porridge}.' },
      response: new Response(null, { status: 400 }),
    });
    show();
    const body = await screen.findByLabelText(t('admin.templates.body'));
    fireEvent.change(body, { target: { value: '{porridge}' } });
    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));

    expect(await screen.findByText('Unknown placeholder {porridge}.')).toBeTruthy();
    expect(client.invalidateQueries).not.toHaveBeenCalled();
  });

  it('restores the default by deleting the rewrite for that language, after asking', async () => {
    show();
    fireEvent.click(await screen.findByRole('button', { name: t('admin.templates.reset') }));
    expect(del).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(del).toHaveBeenCalledTimes(1));
    expect(del).toHaveBeenCalledWith('/api/v1/admin/message-templates/{key}/{locale}', {
      params: { path: { key: 'account.invite', locale: 'en' } },
    });
    expect(await screen.findByText(t('admin.templates.resetDone'))).toBeTruthy();
    expect(client.invalidateQueries).toHaveBeenCalled();
  });

  it('offers no restore for a language that still uses the shipped wording', async () => {
    show();
    fireEvent.click(await screen.findByRole('tab', { name: 'RO' }));
    expect(await screen.findByText(t('admin.templates.usingDefault'))).toBeTruthy();
    // The English tab, now hidden, keeps its button; the Romanian one shown has nothing to restore.
    expect(screen.queryByRole('button', { name: t('admin.templates.reset') })).toBeNull();
  });

  it('names a failed read of the messages and reads again on Retry, instead of an empty page', () => {
    templatesRead = {
      data: undefined,
      isLoading: false,
      isError: true,
      error: new TypeError('Failed to fetch'),
      refetch: refetchTemplates,
    };
    render(
      <QueryClientProvider client={client}>
        <App>
          <MessageTemplatesPage />
        </App>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('message-templates-load-failed')).toHaveTextContent(t('admin.listLoadFailed'));
    expect(screen.queryByText('account.invite', { exact: false })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchTemplates).toHaveBeenCalledTimes(1);
  });
});
