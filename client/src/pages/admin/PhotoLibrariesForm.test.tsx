// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The one-way brake, from the screen that pulls it.
 *
 * The asymmetry under test is the whole feature and it is invisible in the types: a library the
 * deployment supplied gets a switch, and a product nobody connected gets a sentence and no control
 * at all. The server refuses to invent a library either way, but the failure this guards against is
 * a later edit that renders a switch for a product with no address — which reads to an
 * administrator as a library they can turn on, and is a state nobody can act on.
 *
 * Every library, address and name below is invented.
 */

const get = vi.fn();
const put = vi.fn();

vi.mock('../../api/client.ts', () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
  },
}));

let status: {
  providers: { source: string; name: string; suspended: boolean }[];
  unconfigured: { source: string; name: string; suspended: boolean }[];
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    queryKeys: actual.queryKeys,
    useAdminSettings: () => ({ data: undefined, isLoading: false }),
    useCapabilities: () => ({ data: undefined }),
    useMe: () => ({ data: undefined }),
    usePhotoLibraries: () => ({ data: status }),
  };
});

const { PhotoLibrariesForm } = await import('./MessagingSettingsPage.tsx');

const onSaved = vi.fn();

function settingsWith(immichSuspended: boolean, photoPrismSuspended: boolean) {
  return { photoLibraries: { immichSuspended, photoPrismSuspended } } as unknown as AdminSettings;
}

function show(settings: AdminSettings) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <PhotoLibrariesForm settings={settings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  status = {
    providers: [{ source: 'immich', name: 'Immich', suspended: false }],
    unconfigured: [{ source: 'photoprism', name: 'PhotoPrism', suspended: false }],
  };
  onSaved.mockReset();
  get.mockReset().mockResolvedValue({ data: settingsWith(false, false) });
  put.mockReset().mockResolvedValue({ data: settingsWith(true, false), error: undefined });
});
afterEach(cleanup);

describe('PhotoLibrariesForm', () => {
  it('offers a switch for a library the deployment supplied and none for one it did not', () => {
    show(settingsWith(false, false));

    expect(screen.getByTestId('photo-library-suspend-immich')).toBeTruthy();
    // The half that matters: no control whatsoever for a product with no address. A switch here
    // would be offering to turn on a container nobody started.
    expect(screen.queryByTestId('photo-library-suspend-photoprism')).toBeNull();
    expect(screen.getByText(/PhotoPrism is not connected/)).toBeTruthy();
  });

  it('says the sentence that gets left out: stopping changes nothing about what the library holds', () => {
    show(settingsWith(false, false));

    expect(
      screen.getByText(/changes nothing about what it holds, what it indexes, or who can log into it/),
    ).toBeTruthy();
    expect(screen.getByText(/docker compose stop immich-server/)).toBeTruthy();
  });

  it('shows the switch in the position it would post, not the one another query cached', () => {
    // The status answer and the stored settings are separately cached and refresh differently.
    // A switch reading one and writing the other eventually renders a state it would not post.
    status.providers[0].suspended = false;
    show(settingsWith(true, false));

    expect(screen.getByTestId('photo-library-suspend-immich').getAttribute('aria-checked')).toBe(
      'true',
    );
  });

  it('carries the other library’s brake, re-read, so saving one cannot release the other', async () => {
    // Somebody else stopped the other library after this tab loaded its copy of the settings.
    get.mockResolvedValue({ data: settingsWith(false, true) });
    show(settingsWith(false, false));

    fireEvent.click(screen.getByTestId('photo-library-suspend-immich'));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][1].body).toEqual({
      immichSuspended: true,
      // Not false, which is what this tab's own stale copy said: a save of one library must never
      // be the thing that releases the other's brake.
      photoPrismSuspended: true,
    });
  });

  it('falls back to the settings it was given when the fresh read comes back with nothing', async () => {
    get.mockResolvedValue({ data: undefined });
    show(settingsWith(false, true));

    fireEvent.click(screen.getByTestId('photo-library-suspend-immich'));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][1].body).toEqual({
      immichSuspended: true,
      photoPrismSuspended: true,
    });
  });

  it('reports a refused save instead of leaving the switch looking as though it took', async () => {
    put.mockResolvedValue({ data: undefined, error: { title: 'no' } });
    show(settingsWith(false, false));

    fireEvent.click(screen.getByTestId('photo-library-suspend-immich'));

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(onSaved).not.toHaveBeenCalled();
  });
});
