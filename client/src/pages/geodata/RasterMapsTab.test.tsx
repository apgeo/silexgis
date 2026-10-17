// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { RasterMapInfo } from '../../api/hooks.ts';

const { queryCalls, updateSpy } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  updateSpy: vi.fn(),
}));

const hit = (id: string, title: string) =>
  ({ world: 'feature', id, title, subtitle: 'Cave', symbol: null, placeable: false });

const raster = (caveFeatureId: string | null): RasterMapInfo => ({
  id: 'map-1',
  name: 'Harta 1',
  description: null,
  mapKind: 'caveMap',
  fileId: 'file-1',
  status: 'ready',
  processingError: null,
  bbox: null,
  minZoom: null,
  maxZoom: null,
  attribution: null,
  defaultOpacity: 0.8,
  caveFeatureId,
  ownerUserId: 'user-1',
  cavingGroupId: null,
  visibility: 'private',
  cogUrl: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
});

let rows: RasterMapInfo[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useCan: () => true,
  useRasterMaps: () => ({ data: { items: rows, page: 1, pageSize: 20, totalItems: rows.length }, isFetching: false }),
  useUploadRasterMap: () => ({ mutateAsync: vi.fn() }),
  useDeleteRasterMap: () => ({ mutateAsync: vi.fn() }),
  useUpdateRasterMap: () => ({ mutateAsync: updateSpy, isPending: false }),
  useFilterVocabulary: () => ({ data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] } }),
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{ world: 'feature', hits: [hit('cave-1', 'Peștera 1'), hit('cave-2', 'Peștera 2')], total: null }],
            page: 1, pageSize: 8, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  // The stored cave is named the way the route names it; one this account cannot read is not.
  useFilterResolve: (_world: string, ids: readonly string[]) => ({
    data: ids.filter((id) => id === 'cave-1').map((id) => hit(id, 'Peștera 1')),
  }),
}));

const { default: RasterMapsTab } = await import('./RasterMapsTab.tsx');

function openEditor(caveFeatureId: string | null) {
  rows = [raster(caveFeatureId)];
  const { container } = render(
    <MemoryRouter>
      <App>
        <RasterMapsTab />
      </App>
    </MemoryRouter>,
  );
  const edit = container.querySelector('.anticon-edit')?.closest('button');
  fireEvent.click(edit!);
}

function sentBody() {
  const [request] = updateSpy.mock.lastCall as [{ body: { caveFeatureId: string | null } }];
  return request.body;
}

afterEach(cleanup);
beforeEach(() => {
  queryCalls.length = 0;
  updateSpy.mockReset();
  updateSpy.mockResolvedValue({});
});

describe('RasterMapsTab — the linked cave', () => {
  it('names the linked cave without searching for it', () => {
    openEditor('cave-1');
    expect(screen.getByTestId('raster-map-cave-picker').textContent).toContain('Peștera 1');
  });

  it('shows a linked cave this account may not read as not shown, never as its identifier', () => {
    openEditor('cave-hidden');
    const trigger = screen.getByTestId('raster-map-cave-picker');
    expect(trigger.textContent).not.toContain('cave-hidden');
    fireEvent.click(trigger);
    expect(screen.getByTestId('raster-map-cave-picker-restricted')).toBeTruthy();
  });

  it('searches caves only and saves the one picked', async () => {
    openEditor(null);
    fireEvent.click(screen.getByTestId('raster-map-cave-picker'));
    fireEvent.change(screen.getByTestId('raster-map-cave-picker-input'), { target: { value: 'pe' } });
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    fireEvent.click(await screen.findByRole('button', { name: /Peștera 2/ }, { timeout: 3000 }));

    const asked = [...queryCalls].reverse().find((c) => c.enabled)?.body;
    expect(JSON.stringify(asked)).toContain('"kind"');
    expect(JSON.stringify(asked)).toContain('"cave"');

    fireEvent.click(screen.getByRole('button', { name: /OK/i }));
    await waitFor(() => expect(updateSpy).toHaveBeenCalled());
    expect(sentBody().caveFeatureId).toBe('cave-2');
  });

  it('can unlink the cave', async () => {
    openEditor('cave-1');
    fireEvent.click(screen.getByTestId('raster-map-cave-picker'));
    fireEvent.click(screen.getByTestId('raster-map-cave-picker-clear'));

    fireEvent.click(screen.getByRole('button', { name: /OK/i }));
    await waitFor(() => expect(updateSpy).toHaveBeenCalled());
    expect(sentBody().caveFeatureId).toBeNull();
  });
});
