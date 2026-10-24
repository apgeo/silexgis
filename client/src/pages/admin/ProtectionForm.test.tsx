// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The location-protection switches, from the screen an administrator sets them on.
 *
 * What is pinned: every switch of the section is on the screen, shows the position the server
 * last stored, and is sent back on save — a switch the form forgot to carry would be reset to off
 * by every save of its neighbours.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', () => ({
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

const { ProtectionForm } = await import('./MessagingSettingsPage.tsx');

const onSaved = vi.fn();

const stored = {
  revealProtectedAssociations: false,
  calendarFeedEnabled: false,
  keepLocationInPhotoFiles: false,
  serveUnstrippableOriginalsToLocationReaders: true,
  describeLocationFreeCopies: false,
};

function show() {
  const settings = { protection: stored } as unknown as AdminSettings;
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <ProtectionForm settings={settings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>,
  );
}

function switchLabelled(label: RegExp) {
  return screen.getByRole('switch', { name: label });
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({ data: { protection: stored }, error: undefined });
});
afterEach(cleanup);

describe('ProtectionForm', () => {
  it('shows both photo-file switches in the positions the server stored', () => {
    show();

    expect(
      switchLabelled(/still let people who may see the location download videos/).getAttribute(
        'aria-checked',
      ),
    ).toBe('true');
    expect(
      switchLabelled(/Tell phones the size and fingerprint/).getAttribute('aria-checked'),
    ).toBe('false');
    // Each says what it does, not only what it is called.
    expect(screen.getByText(/Someone who may not see the location still never gets it/)).toBeTruthy();
    expect(screen.getByText(/Those requests get slower/)).toBeTruthy();
  });

  it('sends every switch of the section on save, the new ones included', async () => {
    show();

    fireEvent.click(switchLabelled(/Tell phones the size and fingerprint/));
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][0]).toBe('/api/v1/admin/settings/protection');
    expect(put.mock.calls[0][1].body).toEqual({ ...stored, describeLocationFreeCopies: true });
  });
});
