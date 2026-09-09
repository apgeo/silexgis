// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

import type { RegistryRegionBreakdown } from '../../api/hooks.ts';

/**
 * The breakdown page, checked on the thing it exists to get right.
 *
 * The registry counts the total over the caves the reader may read and the rows over the caves
 * they may also place, so the column does not reach the total. A screen that says nothing about
 * that has published a table that looks broken, and a screen that closes the gap — with a
 * gathered row, or a percentage, or a quietly adjusted total — has published a different answer.
 * Both failures are asserted against here, in both directions: the sentence appears when the two
 * figures differ and stays away when they agree, so it cannot be a decoration that is always on.
 */

const { regionsSpy } = vi.hoisted(() => ({ regionsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useRegistryRegions: (params: unknown) => regionsSpy(params),
  useCaveTypes: () => ({ data: [{ id: 1, name: 'Cave' }] }),
  useRockTypes: () => ({ data: [{ id: 2, name: 'Limestone' }] }),
}));

const { downloadSpy } = vi.hoisted(() => ({
  downloadSpy: vi.fn((_url: string) => Promise.resolve()),
}));

vi.mock('../../api/download.ts', () => ({
  downloadFile: (url: string) => downloadSpy(url),
  registryRegionsExportUrl: (params: Record<string, unknown>) =>
    `/api/v1/stats/registry/regions/export?rockTypeId=${String(params.rockTypeId)}`,
}));

const { default: RegistryRegionsPage } = await import('./RegistryRegionsPage.tsx');

function breakdown(overrides: Partial<RegistryRegionBreakdown> = {}): RegistryRegionBreakdown {
  return {
    caveCount: 40,
    regions: [
      { region: 'Apuseni', caveCount: 18 },
      { region: 'Banat', caveCount: 7 },
      { region: null, caveCount: 5 },
    ],
    basis: 'Counted over the caves you may place.',
    ...overrides,
  };
}

function renderPage(search = '') {
  return render(
    <ConfigProvider>
      <App>
        <MemoryRouter initialEntries={[`/statistics/regions${search}`]}>
          <RegistryRegionsPage />
        </MemoryRouter>
      </App>
    </ConfigProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('the registry regions page', () => {
  it('says why the rows do not add up to the total instead of letting it read as a fault', async () => {
    regionsSpy.mockReturnValue({ data: breakdown(), isError: false, error: null });

    renderPage();

    // 18 + 7 + 5 = 30 rows against a total of 40: ten caves this reader may read and may not place.
    const counts = await screen.findByTestId('registry-regions-counts');
    expect(counts.textContent).toContain('30');
    expect(counts.textContent).toContain('40');
    const shortfall = screen.getByTestId('registry-regions-shortfall');
    expect(shortfall.textContent).toContain('10');
    expect(shortfall.textContent).toMatch(/position you may not see/i);
    // Never closed over: no gathered row, no percentage, nothing that reconciles the two figures.
    expect(screen.queryByText('%')).toBeNull();
    // The server's own sentence about what it counted over, in its own words rather than ours.
    expect(screen.getByTestId('registry-regions-basis').textContent).toBe(
      'Counted over the caves you may place.',
    );
  });

  it('keeps the unrecorded region as a row of its own and labels it', async () => {
    regionsSpy.mockReturnValue({ data: breakdown(), isError: false, error: null });

    renderPage();

    expect(await screen.findByText('Apuseni')).toBeTruthy();
    // A cave placed under no region is part of what the breakdown says, so it is a row and not a
    // silence — and it is not drawn as an empty cell either.
    expect(screen.getByText('No region recorded')).toBeTruthy();
  });

  /**
   * Two rows that both mean "no region" are two rows, and the list has to be able to tell them
   * apart. The registry groups on the column as it stands, so a cave saved with an empty region
   * and one saved with none are grouped separately — keyed by the region alone they collide,
   * React says so on the console, and the browser sweep treats that as a defect on any
   * installation whose data happens to hold one.
   */
  it('gives the unrecorded region and an empty one keys the list can tell apart', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    regionsSpy.mockReturnValue({
      data: breakdown({
        caveCount: 30,
        regions: [
          { region: 'Apuseni', caveCount: 18 },
          { region: '', caveCount: 7 },
          { region: null, caveCount: 5 },
        ],
      }),
      isError: false,
      error: null,
    });

    renderPage();

    await screen.findByTestId('registry-regions-table');
    expect(screen.getAllByText('No region recorded')).toHaveLength(2);
    const said = consoleError.mock.calls.map((call) => call.map(String).join(' ')).join('\n');
    expect(said).not.toMatch(/same key/i);
    consoleError.mockRestore();
  });

  it('says nothing about a shortfall when there is none', async () => {
    regionsSpy.mockReturnValue({
      data: breakdown({
        caveCount: 25,
        regions: [
          { region: 'Apuseni', caveCount: 18 },
          { region: 'Banat', caveCount: 7 },
        ],
      }),
      isError: false,
      error: null,
    });

    renderPage();

    await screen.findByTestId('registry-regions-counts');
    expect(screen.queryByTestId('registry-regions-shortfall')).toBeNull();
  });

  it('asks for the file with the same narrowing the screen is showing', async () => {
    regionsSpy.mockReturnValue({ data: breakdown(), isError: false, error: null });

    renderPage('?rockTypeId=2');
    (await screen.findByTestId('registry-regions-export')).click();

    // The screen's own query object, unchanged, is what the export builder is handed: there is no
    // second place here where the file's question could come out different from the screen's.
    const asked = regionsSpy.mock.calls.at(-1)?.[0] as Record<string, unknown>;
    expect(asked.rockTypeId).toBe(2);
    expect(downloadSpy).toHaveBeenCalledWith(
      '/api/v1/stats/registry/regions/export?rockTypeId=2',
    );
  });

  it('offers no file while there is nothing on the screen to take one of', async () => {
    regionsSpy.mockReturnValue({ data: undefined, isError: false, error: null });

    renderPage();

    expect(await screen.findByTestId('registry-regions-export')).toHaveProperty('disabled', true);
  });
});
