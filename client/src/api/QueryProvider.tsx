// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import { MutationCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { useTranslation } from 'react-i18next';
import { isConcurrencyConflict, retryQuery } from './client.ts';

/**
 * Hosts the TanStack Query client inside the antd App context, so a lost-update conflict on
 * any mutation surfaces one themed prompt (reload + reapply) instead of every call site
 * having to recognise the concurrency code.
 */
export function QueryProvider({ children }: { children: ReactNode }) {
  const { message } = AntApp.useApp();
  const { i18n } = useTranslation();
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: retryQuery } },
        mutationCache: new MutationCache({
          onError: (error) => {
            if (isConcurrencyConflict(error)) {
              void message.warning(i18n.t('common.conflict'));
            }
          },
        }),
      }),
  );

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
