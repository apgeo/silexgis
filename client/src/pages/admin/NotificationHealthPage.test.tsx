// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

vi.mock('../../api/hooks.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks.ts')>()),
  useCapabilities: () => ({ data: { domains: { settings: 'read, execute' } } }),
}));

const { api } = await import('../../api/client.ts');
const { default: NotificationHealthPage } = await import('./NotificationHealthPage.tsx');

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <App>
        <NotificationHealthPage />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

it('does not report zeros and "nothing waiting" when the health read failed', async () => {
  vi.spyOn(api, 'GET').mockRejectedValue(new TypeError('Failed to fetch'));
  show();

  expect(await screen.findByTestId('notification-health-load-failed')).toBeTruthy();
  expect(screen.getByTestId('notification-deliveries-load-failed')).toBeTruthy();
  expect(screen.queryByText('Nothing waiting')).toBeNull();
  expect(screen.queryByText('0 of 0')).toBeNull();
});

it('shows the figures once the health read answered', async () => {
  vi.spyOn(api, 'GET').mockImplementation(((path: string) =>
    Promise.resolve(
      path.endsWith('/health')
        ? {
            data: { counts: [], oldestPendingAgeSeconds: null, paidMessagesToday: 0, dailyPaidMessageCap: 0 },
            response: new Response(null),
          }
        : { data: { items: [], page: 1, pageSize: 25, totalItems: 0 }, response: new Response(null) },
    )) as never);
  show();

  expect(await screen.findByText('Nothing waiting')).toBeTruthy();
  expect(screen.queryByTestId('notification-health-load-failed')).toBeNull();
});
