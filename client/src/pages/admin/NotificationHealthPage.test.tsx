// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { expectEveryColumnHeaderNamed, expectEveryFieldNamed } from '../../testFieldNames.ts';

/** The operator's page of notification health and deliveries. Every row below is invented. */

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, api: { GET: get, POST: post } };
});
vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCapabilities: () => ({ data: { domains: { settings: ['read', 'execute'] } } }),
    hasAccessAction: () => true,
  };
});

const { default: NotificationHealthPage } = await import('./NotificationHealthPage.tsx');

const deadRow = {
  id: 41,
  notificationId: 9,
  recipientUserId: '00000000-0000-4000-8000-000000000041',
  recipientLabel: 'Ion A.',
  category: 'tripPublished',
  templateKey: 'notify.trip-published',
  channel: 'email',
  status: 'dead',
  attempts: 5,
  createdAt: '2026-11-01T08:00:00Z',
  notBefore: '2026-11-01T08:00:00Z',
  sentAt: null,
  error: 'smtp refused',
};

function show() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <App>
          <NotificationHealthPage />
        </App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  get.mockReset().mockImplementation((path: string) =>
    Promise.resolve(
      path.endsWith('/health')
        ? {
            data: {
              counts: [{ status: 'dead', count: 1 }],
              oldestPendingCreatedAt: null,
              oldestPendingAgeSeconds: null,
              paidMessagesToday: 0,
              dailyPaidMessageCap: 0,
            },
            response: { status: 200 },
          }
        : { data: { items: [deadRow], page: 1, pageSize: 25, totalItems: 1 }, response: { status: 200 } },
    ),
  );
  post.mockReset();
});
afterEach(cleanup);

async function sendAgain() {
  fireEvent.click(await screen.findByRole('button', { name: 'Send again' }));
  const buttons = await screen.findAllByRole('button', { name: 'Send again' });
  fireEvent.click(buttons[buttons.length - 1]);
}

describe('sending a failed message again', () => {
  it('says it could not be put back when the request never arrives', async () => {
    post.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    await sendAgain();

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(i18n.t('notificationHealth.retryFailed'))).toBeTruthy();
  });

  it('says it could not be put back when the server refuses', async () => {
    post.mockResolvedValue({ data: undefined, error: { status: 403 }, response: { status: 403 } });
    show();
    await sendAgain();

    expect(await screen.findByText(i18n.t('notificationHealth.retryFailed'))).toBeTruthy();
  });
});

describe('the health figures', () => {
  it('does not report zeros and "nothing waiting" when the health read failed', async () => {
    get.mockReset().mockRejectedValue(new TypeError('Failed to fetch'));
    show();

    expect(await screen.findByTestId('notification-health-load-failed')).toBeTruthy();
    expect(screen.getByTestId('notification-deliveries-load-failed')).toBeTruthy();
    expect(screen.queryByText('Nothing waiting')).toBeNull();
    expect(screen.queryByText('0 of 0')).toBeNull();
  });

  it('shows the figures once the health read answered', async () => {
    get.mockReset().mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/health')
          ? {
              data: { counts: [], oldestPendingAgeSeconds: null, paidMessagesToday: 0, dailyPaidMessageCap: 0 },
              response: { status: 200 },
            }
          : { data: { items: [], page: 1, pageSize: 25, totalItems: 0 }, response: { status: 200 } },
      ),
    );
    show();

    expect(await screen.findByText('Nothing waiting')).toBeTruthy();
    expect(screen.queryByTestId('notification-health-load-failed')).toBeNull();
  });
});

describe('messages that reached only the server log', () => {
  function answer(counts: { channel: string; status: string; count: number }[], items: unknown[]) {
    get.mockReset().mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/health')
          ? {
              data: { counts, oldestPendingAgeSeconds: null, paidMessagesToday: 0, dailyPaidMessageCap: 0 },
              response: { status: 200 },
            }
          : { data: { items, page: 1, pageSize: 25, totalItems: items.length }, response: { status: 200 } },
      ),
    );
  }

  it('says in words that nobody received them and points to the messaging settings', async () => {
    answer(
      [{ channel: 'email', status: 'loggedOnly', count: 2 }],
      [{ ...deadRow, id: 42, status: 'loggedOnly', attempts: 1, error: null }],
    );
    show();

    const warning = await screen.findByTestId('notification-health-logged-only');
    expect(warning.textContent).toContain(i18n.t('notificationHealth.loggedOnly'));
    const link = screen.getByRole('link', { name: i18n.t('notificationHealth.mailSettings') });
    expect(link.getAttribute('href')).toBe('/admin/messaging');
    // The row's own status is the logged-only outcome, never "Sent".
    expect((await screen.findAllByText(i18n.t('notificationHealth.statuses.loggedOnly'))).length).toBeGreaterThan(0);
    expect(screen.queryByText(i18n.t('notificationHealth.statuses.sent'))).toBeNull();
  });

  it('raises no warning when every message went out', async () => {
    answer([{ channel: 'email', status: 'sent', count: 3 }], []);
    show();

    expect(await screen.findByText('Nothing waiting')).toBeTruthy();
    expect(screen.queryByTestId('notification-health-logged-only')).toBeNull();
  });

  it('says nothing needs attention instead of an empty table', async () => {
    answer([], []);
    show();

    expect(await screen.findByText(i18n.t('notificationHealth.nothingNeedsAttention'))).toBeTruthy();
  });
});

describe('the filter row', () => {
  it('names the status filter and the attention switch for a screen reader', async () => {
    get.mockReset().mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/health')
          ? {
              data: { counts: [], oldestPendingAgeSeconds: null, paidMessagesToday: 0, dailyPaidMessageCap: 0 },
              response: { status: 200 },
            }
          : { data: { items: [], page: 1, pageSize: 25, totalItems: 0 }, response: { status: 200 } },
      ),
    );
    const { container } = show();
    await screen.findByText('Nothing waiting');

    expectEveryFieldNamed(container);
    expect(screen.getByRole('switch')).toHaveAccessibleName('Only what needs attention');
  });

  it('gives the expand and action columns a heading a screen reader says', async () => {
    get.mockReset().mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/health')
          ? {
              data: { counts: [], oldestPendingAgeSeconds: null, paidMessagesToday: 0, dailyPaidMessageCap: 0 },
              response: { status: 200 },
            }
          : { data: { items: [], page: 1, pageSize: 25, totalItems: 0 }, response: { status: 200 } },
      ),
    );
    const { container } = show();
    await screen.findByText('Nothing waiting');

    expectEveryColumnHeaderNamed(container);
  });
});
