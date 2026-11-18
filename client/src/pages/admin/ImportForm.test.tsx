// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The import section of the admin settings.
 *
 * The server refuses a section save that leaves a value out (an omitted number would otherwise be
 * stored as zero), and the form shows only some of the section's values. So what is pinned is that
 * a save sends every member of the section — the ones the form does not show carried as stored.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', async () => ({
  ApiError: (await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts')).ApiError,
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
  },
}));

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

const { ImportForm } = await import('./MessagingSettingsPage.tsx');

const stored = {
  allowCreateWithoutReview: false,
  duplicateRadiusMeters: 50,
  duplicateNameSimilarity: 0.8,
  photoProximityRadiusMeters: 120,
  photoClusterRadiusMeters: 35,
};

beforeEach(() => {
  put.mockReset().mockResolvedValue({ data: { import: stored }, error: undefined });
});
afterEach(cleanup);

describe('ImportForm', () => {
  it('sends every member of the section on save, the ones it does not show included', async () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <App>
          <ImportForm settings={{ import: stored } as unknown as AdminSettings} onSaved={vi.fn()} />
        </App>
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByRole('switch'));
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][0]).toBe('/api/v1/admin/settings/import');
    expect(put.mock.calls[0][1].body).toEqual({ ...stored, allowCreateWithoutReview: true });
  });
});
