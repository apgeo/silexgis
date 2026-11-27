// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

/**
 * The audit trail: what it lists, a read that did not land (named, with a way to ask again) and a
 * read the server refused (named as that, with no Retry that would only be refused again). It
 * writes nothing. Every entry below is invented.
 */

const t = i18n.getFixedT('en');
const get = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, api: { GET: (...args: unknown[]) => get(...args) } };
});

const { default: AuditPage } = await import('./AuditPage.tsx');

const entries = {
  page: 1,
  pageSize: 50,
  totalItems: 1,
  items: [
    {
      id: 'e1',
      at: '2026-11-01T10:00:00Z',
      userName: 'ana.invented',
      action: 'created',
      entityType: 'InventedThing',
      entityId: 'thing-1',
      changes: null,
    },
  ],
};

const answered = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });
const refused = (status: number, code: string) => ({
  data: undefined,
  error: { code },
  response: new Response(null, { status }),
});

function show() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <App>
        <AuditPage />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  get.mockReset();
});

afterEach(cleanup);

describe('the audit page', () => {
  it('names the kind of record in words, and an automatic change as the system\'s', async () => {
    get.mockResolvedValue(
      answered({
        ...entries,
        items: [{ ...entries.items[0], userName: null, userId: null, entityType: 'Feature:CaveEntrance' }],
      }),
    );
    show();

    expect(await screen.findByText('Entrance')).toBeInTheDocument();
    expect(screen.getByText('System')).toBeInTheDocument();
  });

  it('lists who changed what', async () => {
    get.mockResolvedValue(answered(entries));
    show();

    expect(await screen.findByText('ana.invented')).toBeInTheDocument();
    // A kind nobody has worded is still spelt as words rather than as the type's own name.
    expect(screen.getByText('Invented thing')).toBeInTheDocument();
    expect(screen.queryByTestId('audit-load-failed')).toBeNull();
  });

  it('names a read that did not land, offers to ask again, and lists the entries once it does', async () => {
    get.mockResolvedValueOnce(refused(503, 'unavailable'));
    get.mockResolvedValue(answered(entries));
    show();

    const failed = await screen.findByTestId('audit-load-failed');
    expect(failed).toHaveTextContent(t('audit.loadFailed'));
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));

    expect(await screen.findByText('ana.invented')).toBeInTheDocument();
    expect(screen.queryByTestId('audit-load-failed')).toBeNull();
  });

  it('names a refused read as a refusal and offers no retry', async () => {
    get.mockResolvedValue(refused(403, 'auth.forbidden'));
    show();

    const failed = await screen.findByTestId('audit-load-failed');
    expect(failed).toHaveAttribute('data-refused', 'true');
    expect(failed).toHaveTextContent(t('common.readRefused'));
    expect(screen.queryByRole('button', { name: t('common.retry') })).toBeNull();
  });
});
