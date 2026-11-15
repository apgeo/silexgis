// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { queryKeys, type AdminSettings } from '../../api/hooks.ts';

/**
 * The installation's entrance markers, from the admin screen: it shows what the server stored,
 * previews it as the map draws it, and sends the section back whole.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', () => ({
  api: { PUT: (...args: unknown[]) => put(...args) },
}));

const { default: MapSymbolsSettingsForm } = await import('./MapSymbolsSettingsForm.tsx');

const onSaved = vi.fn();

function show(entranceSymbol: string, approximateEntranceSymbol: string) {
  const settings = { mapSymbols: { entranceSymbol, approximateEntranceSymbol } } as unknown as AdminSettings;
  const queryClient = new QueryClient();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  render(
    <QueryClientProvider client={queryClient}>
      <App>
        <MapSymbolsSettingsForm settings={settings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>,
  );
  return { invalidate };
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({
    data: { mapSymbols: { entranceSymbol: 'shape:cave', approximateEntranceSymbol: 'shape:cave:d46b08' } },
    error: undefined,
  });
});
afterEach(cleanup);

describe('MapSymbolsSettingsForm', () => {
  it('previews the stored symbols as the map draws them', async () => {
    show('osm_cave.svg', 'shape:triangle:aa0000');

    const preview = await screen.findByTestId('admin-map-symbols-preview');
    await waitFor(() => {
      const sources = Array.from(preview.querySelectorAll('img')).map((img) => img.getAttribute('src') ?? '');
      expect(sources.filter((src) => src === '/feature_symbols/osm_cave.svg')).toHaveLength(3);
      expect(sources.filter((src) => decodeURIComponent(src).includes("fill='#aa0000'"))).toHaveLength(3);
    });
  });

  it('offers a colour for a drawn shape and none for a symbol file', async () => {
    show('osm_cave.svg', 'shape:triangle:aa0000');

    await screen.findByTestId('admin-map-symbols-approximate-color');
    expect(screen.queryByTestId('admin-map-symbols-entrance-color')).toBeNull();
  });

  it('goes back to the defaults and saves them, then refreshes the map configuration', async () => {
    const { invalidate } = show('osm_cave.svg', 'shape:triangle:aa0000');
    await screen.findByTestId('admin-map-symbols-preview');

    fireEvent.click(screen.getByRole('button', { name: 'Back to the defaults' }));
    fireEvent.click(screen.getByTestId('admin-map-symbols-save'));

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith('/api/v1/admin/settings/map-symbols', {
        body: { entranceSymbol: 'shape:cave', approximateEntranceSymbol: 'shape:cave:d46b08' },
      }),
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.mapConfig });
  });
});
