// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { RasterMapInfo } from '../../api/hooks.ts';
import { worldToken } from '../../filters/worldList.ts';
import type { FilterNode } from '../../filters/types.ts';

const { queryCalls, updateSpy, listCalls, can } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  updateSpy: vi.fn(),
  listCalls: [] as Record<string, unknown>[],
  can: { value: true },
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
  hasEmbeddedPlacement: false,
});

let rows: RasterMapInfo[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useCan: () => can.value,
  useRasterMaps: (params: Record<string, unknown>) => (listCalls.push(params), { data: { items: rows, page: 1, pageSize: 20, totalItems: rows.length }, isFetching: false, isError: false }),
  useUploadRasterMap: () => ({ mutateAsync: vi.fn() }),
  useDeleteRasterMap: () => ({ mutateAsync: vi.fn() }),
  useUpdateRasterMap: () => ({ mutateAsync: updateSpy, isPending: false }),
  useRenderRasterMap: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

// The condition editor and saved filters have their own tests; here only what the tab asks of the list.
vi.mock('../../components/filters/WorldFilterBar.tsx', () => ({
  default: ({ world, where }: { world: string; where: unknown }) => (
    <div data-testid="world-filter-bar" data-world={world} data-on={where === null ? 'no' : 'yes'} />
  ),
}));

// The placement editor has its own test and draws a map; here only which sheet the tab opens it for.
vi.mock('../../components/geodata/placement/PlacementEditorModal.tsx', () => ({
  default: ({ raster }: { raster: RasterMapInfo | null }) =>
    raster ? <div data-testid="fake-placement-editor" data-raster={raster.id} /> : null,
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
  listCalls.length = 0;
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

describe('RasterMapsTab — the list filter', () => {
  const kindIs = (value: string): FilterNode => ({
    node: 'condition', field: 'mapKind', op: 'equals', values: [{ type: 'id', value }],
  });

  function mountAt(search: string) {
    rows = [raster(null)];
    render(
      <MemoryRouter initialEntries={[`/geodata?tab=raster${search}`]}>
        <App>
          <RasterMapsTab />
        </App>
      </MemoryRouter>,
    );
  }

  it('asks the list with no filter when the address holds none', () => {
    mountAt('');
    expect(listCalls.at(-1)?.filter).toBeUndefined();
    expect(screen.getByTestId('world-filter-bar').dataset.world).toBe('georeferencedMap');
    expect(screen.getByTestId('world-filter-bar').dataset.on).toBe('no');
  });

  it('asks the list with the conditions a link carries', () => {
    const token = worldToken('georeferencedMap', kindIs('caveMap'))!;
    mountAt(`&filter=${token}`);
    expect(listCalls.at(-1)?.filter).toBe(token);
    expect(screen.getByTestId('world-filter-bar').dataset.on).toBe('yes');
  });

  it('does not take a filter about geodata files as its own', () => {
    const wrongWorld = worldToken('geofile', kindIs('gpx'))!;
    mountAt(`&filter=${wrongWorld}`);
    expect(listCalls.at(-1)?.filter).toBeUndefined();
  });
});

describe('RasterMapsTab — without the right to change maps', () => {
  afterEach(() => {
    cleanup();
    can.value = true;
  });

  function mount() {
    rows = [raster(null)];
    const { container } = render(
      <MemoryRouter>
        <App>
          <RasterMapsTab />
        </App>
      </MemoryRouter>,
    );
    return container;
  }

  it('offers the edit, delete and upload controls to an account that may use them', () => {
    const container = mount();
    expect(container.querySelector('.anticon-edit')).not.toBeNull();
    expect(container.querySelector('.anticon-delete')).not.toBeNull();
    expect(container.querySelector('.ant-upload-drag')).not.toBeNull();
  });

  it('offers none of them to an account that may only read', () => {
    can.value = false;
    const container = mount();
    expect(screen.getByText('Harta 1')).toBeTruthy();
    expect(container.querySelector('.anticon-edit')).toBeNull();
    expect(container.querySelector('.anticon-delete')).toBeNull();
    expect(container.querySelector('.ant-upload-drag')).toBeNull();
  });
});

describe('RasterMapsTab — map sheets drawn from a document', () => {
  afterEach(cleanup);

  const sheet = (status: RasterMapInfo['status']): RasterMapInfo => ({
    ...raster(null),
    id: 'sheet-1',
    name: 'Planșa 1',
    status,
    sourceFileId: 'pdf-1',
    sourcePage: 1,
    sourcePageCount: 2,
    sourceLayers: ['Contur', 'Text'],
    renderLayers: null,
    renderDpi: 300,
    renderWidth: 2400,
    renderHeight: 1680,
  });

  function mount(rowsNow: RasterMapInfo[]) {
    rows = rowsNow;
    return render(
      <MemoryRouter>
        <App>
          <RasterMapsTab />
        </App>
      </MemoryRouter>,
    );
  }

  it('takes a PDF and a scanned picture as well as a GeoTIFF', () => {
    const { container } = mount([]);
    expect(container.querySelector('input[type=file]')?.getAttribute('accept')).toBe(
      '.tif,.tiff,.pdf,.png,.jpg,.jpeg',
    );
  });

  it('warns, in the editor, once a sheet is linked to a cave', () => {
    const { container } = mount([{ ...sheet('ready'), caveFeatureId: 'cave-1' }]);
    fireEvent.click(container.querySelector('.anticon-edit')!.closest('button')!);
    expect(screen.getByTestId('sheet-cave-warning')).toBeTruthy();
  });

  it('does not warn for a sheet linked to no cave, nor for a GeoTIFF linked to one', () => {
    const { container } = mount([sheet('ready'), { ...raster('cave-1'), id: 'tif-1', name: 'Harta 2' }]);
    const edits = container.querySelectorAll('.anticon-edit');
    fireEvent.click(edits[0].closest('button')!);
    expect(screen.queryByTestId('sheet-cave-warning')).toBeNull();
  });

  it('warns the moment a cave is picked for a sheet', async () => {
    const { container } = mount([sheet('ready')]);
    fireEvent.click(container.querySelector('.anticon-edit')!.closest('button')!);
    expect(screen.queryByTestId('sheet-cave-warning')).toBeNull();
    fireEvent.click(screen.getByTestId('raster-map-cave-picker'));
    fireEvent.change(screen.getByTestId('raster-map-cave-picker-input'), { target: { value: 'pe' } });
    fireEvent.click(await screen.findByRole('button', { name: /Peștera 2/ }, { timeout: 3000 }));
    expect(await screen.findByTestId('sheet-cave-warning')).toBeTruthy();
  });

  it('names an unplaced sheet as needing placement and opens the editor for it', () => {
    mount([sheet('unplaced')]);
    expect(screen.getByText('Needs placement')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Show on map' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Place on map…' }));
    expect(screen.getByTestId('fake-placement-editor').dataset.raster).toBe('sheet-1');
  });

  it('offers to adjust a placed sheet, and to draw it again', () => {
    mount([sheet('ready')]);
    expect(screen.getByRole('button', { name: 'Adjust placement…' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Redraw…' }));
    expect(screen.getByTestId('redraw-page')).toBeTruthy();
  });

  it('holds both back while the sheet is being processed', () => {
    mount([sheet('processing')]);
    expect(screen.getByRole('button', { name: 'Place on map…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Redraw…' })).toBeDisabled();
  });

  it('offers neither for a GeoTIFF', () => {
    mount([raster(null)]);
    expect(screen.queryByRole('button', { name: 'Place on map…' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Redraw…' })).toBeNull();
  });
});
