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

const { default: ProtectionForm } = await import('./ProtectionForm.tsx');

const onSaved = vi.fn();

const stored = {
  revealProtectedAssociations: false,
  calendarFeedEnabled: false,
  keepLocationInPhotoFiles: false,
  serveUnstrippableOriginalsToLocationReaders: true,
  describeLocationFreeCopies: false,
};

function form(settings: AdminSettings) {
  return (
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <ProtectionForm settings={settings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>
  );
}

function show() {
  return render(form({ protection: stored } as unknown as AdminSettings));
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

  // The section is saved whole, so the save names the version of the section the form was filled
  // from: a save made after another administrator saved the section is refused, not taken over
  // their values.
  it('saves against the version of the section it was filled from', async () => {
    render(form({ protection: stored, versions: { protection: 'p1', mail: 'm1' } } as unknown as AdminSettings));

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][1].headers).toEqual({ 'If-Match': '"p1"' });
  });

  it('reads the settings again and says why when the section was saved over since', async () => {
    const onStale = vi.fn();
    put.mockResolvedValue({ data: undefined, error: { status: 412, code: 'concurrency.version_mismatch' } });
    render(
      <QueryClientProvider client={new QueryClient()}>
        <App>
          <ProtectionForm
            settings={{ protection: stored, versions: { protection: 'p1' } } as unknown as AdminSettings}
            onSaved={onSaved}
            onStale={onStale}
          />
        </App>
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByText(/Someone else changed this since you loaded it/)).toBeTruthy();
    expect(onStale).toHaveBeenCalledTimes(1);
    expect(onSaved).not.toHaveBeenCalled();
  });

  // Every section's save answers the whole stored document, so every open section form is
  // handed a new settings object. Only a change to its own section may put stored values back.
  it('keeps an unsaved switch when another section of the settings is saved', () => {
    const view = render(form({ protection: stored, mail: { host: 'a' } } as unknown as AdminSettings));
    const fingerprint = () => switchLabelled(/Tell phones the size and fingerprint/);
    fireEvent.click(fingerprint());
    expect(fingerprint().getAttribute('aria-checked')).toBe('true');

    view.rerender(form({ protection: { ...stored }, mail: { host: 'b' } } as unknown as AdminSettings));
    expect(fingerprint().getAttribute('aria-checked')).toBe('true');

    // A change to this section itself — its own save, or another administrator's — is shown.
    view.rerender(form({
      protection: { ...stored, describeLocationFreeCopies: false, calendarFeedEnabled: true },
      mail: { host: 'b' },
    } as unknown as AdminSettings));
    expect(fingerprint().getAttribute('aria-checked')).toBe('false');
  });
});
