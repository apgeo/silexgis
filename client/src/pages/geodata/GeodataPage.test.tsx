// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { DownloadError } from '../../api/download.ts';
import type { GeofileInfo } from '../../api/hooks.ts';

const listCalls: { page?: number; pageSize?: number; search?: string }[] = [];
const updateMutate = vi.fn();
const deleteMutate = vi.fn();
const downloadFile = vi.fn();
let rows: ((page: number) => GeofileInfo[]) | null = null;
let forEdit: { data?: { value: GeofileInfo; version?: string }; isLoading: boolean; isError: boolean } = {
  isLoading: true,
  isError: false,
};
const forEditCalls: (string | undefined)[] = [];

const row = (n: number): GeofileInfo =>
  ({
    id: `geo-${n}`,
    name: `Fișier ${n}`,
    description: null,
    format: 'gpx',
    importStatus: 'imported',
    visibility: 'private',
    cavingGroupId: null,
    style: null,
    bbox: null,
    featureCount: 1,
    sourceOptions: null,
  }) as unknown as GeofileInfo;

vi.mock('../../api/hooks.ts', () => ({
  useCan: () => true,
  useGeofiles: (params: { page?: number; pageSize?: number; search?: string }) => {
    listCalls.push(params);
    const page = params.page ?? 1;
    return {
      data: {
        items: rows ? rows(page) : [row(page * 10 + 1), row(page * 10 + 2)],
        page,
        pageSize: params.pageSize ?? 20,
        totalItems: 100,
      },
      isFetching: false,
      isError: false,
    };
  },
  useUploadGeofile: () => ({ mutateAsync: vi.fn() }),
  useUpdateGeofile: () => ({ mutateAsync: updateMutate, isPending: false }),
  useDeleteGeofile: () => ({ mutateAsync: deleteMutate }),
  useGeofileForEdit: (id: string | undefined) => {
    forEditCalls.push(id);
    return id ? forEdit : { isLoading: false, isError: false };
  },
}));

vi.mock('../../api/download.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/download.ts')>('../../api/download.ts');
  return { ...actual, downloadFile: (url: string) => downloadFile(url) };
});

// The directory behind the picker has tests of its own; here it only has to hold a value.
vi.mock('../../components/cavingGroups/CavingGroupPicker.tsx', () => ({
  default: ({ value, onChange }: { value?: string | null; onChange?: (v: string | undefined) => void }) => (
    <input
      data-testid="caving-group-picker"
      value={value ?? ''}
      onChange={(e) => onChange?.(e.target.value || undefined)}
    />
  ),
}));

// The neighbours of the list are covered by their own tests; this file is about the list.
vi.mock('../../components/filters/WorldFilterBar.tsx', () => ({ default: () => null }));
vi.mock('./ImportBatchesTab.tsx', () => ({ default: () => null }));
vi.mock('./RasterMapsTab.tsx', () => ({ default: () => null }));
vi.mock('./GeofileEncodingDialog.tsx', () => ({ default: () => null }));
vi.mock('../../components/geodata/UploadDropzone.tsx', () => ({ default: () => null }));
vi.mock('../../map/mapContext.ts', () => ({ fitGeoJsonGeometry: vi.fn() }));

const { default: GeodataPage } = await import('./GeodataPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <GeodataPage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('GeodataPage', () => {
  beforeEach(() => {
    listCalls.length = 0;
    forEditCalls.length = 0;
    updateMutate.mockReset();
    deleteMutate.mockReset();
    downloadFile.mockReset();
    rows = null;
    forEdit = { isLoading: true, isError: false };
  });

  it('asks for the first page of a new search rather than the page the old list was on', async () => {
    show();
    fireEvent.click(screen.getByTitle('3'));
    await waitFor(() => expect(listCalls.at(-1)).toMatchObject({ page: 3 }));

    fireEvent.change(screen.getByPlaceholderText(/search/i), { target: { value: 'ursilor' } });

    // A narrower list on page 3 would read as "nothing matches" while two files do.
    await waitFor(() => expect(listCalls.at(-1)).toMatchObject({ search: 'ursilor' }));
    expect(listCalls.at(-1)).toMatchObject({ page: 1, search: 'ursilor' });
  });

  it('says why a delete was refused', async () => {
    deleteMutate.mockRejectedValue(new ApiError(403));
    show();

    fireEvent.click(screen.getAllByRole('button', { name: i18n.t('iconButtons.delete') })[0]);
    fireEvent.click(await screen.findByRole('button', { name: /ok/i }));

    expect(await screen.findByText(i18n.t('geodata.refusals.forbidden'))).toBeInTheDocument();
  });

  it('says why an export was refused', async () => {
    downloadFile.mockRejectedValue(new DownloadError(404));
    show();

    fireEvent.mouseEnter(screen.getAllByRole('button', { name: i18n.t('iconButtons.download') })[0]);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'GPX' }));

    await waitFor(() => expect(downloadFile).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(i18n.t('geodata.refusals.gone'))).toBeInTheDocument();
  });

  it('edits from a fresh read and sends the style, group and version that read returned', async () => {
    // The list row is older than the file: another edit has since given it a style and a group.
    forEdit = {
      isLoading: false,
      isError: false,
      data: {
        value: {
          ...row(11),
          name: 'Renamed elsewhere',
          style: { stroke: '#123456' },
          cavingGroupId: 'group-1',
          visibility: 'cavingGroup',
        } as unknown as GeofileInfo,
        version: '"7"',
      },
    };
    updateMutate.mockResolvedValue({});
    show();

    fireEvent.click(screen.getAllByRole('button', { name: i18n.t('geodata.edit') })[0]);
    expect(await screen.findByDisplayValue('Renamed elsewhere')).toBeInTheDocument();
    expect(forEditCalls).toContain('geo-11');

    fireEvent.click(screen.getByRole('button', { name: /ok/i }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalledTimes(1));
    expect(updateMutate.mock.calls[0][0]).toMatchObject({
      id: 'geo-11',
      version: '"7"',
      body: {
        name: 'Renamed elsewhere',
        style: { stroke: '#123456' },
        cavingGroupId: 'group-1',
        visibility: 'cavingGroup',
      },
    });
  });

  it('asks for a caving group before saving caving-group visibility without one', async () => {
    forEdit = {
      isLoading: false,
      isError: false,
      data: { value: { ...row(11), visibility: 'cavingGroup', cavingGroupId: null } as unknown as GeofileInfo },
    };
    show();

    fireEvent.click(screen.getAllByRole('button', { name: i18n.t('geodata.edit') })[0]);
    await screen.findByTestId('caving-group-picker');
    fireEvent.click(screen.getByRole('button', { name: /ok/i }));

    expect(await screen.findByText(i18n.t('geodata.cavingGroupRequired'))).toBeInTheDocument();
    expect(updateMutate).not.toHaveBeenCalled();

    fireEvent.change(screen.getByTestId('caving-group-picker'), { target: { value: 'group-2' } });
    fireEvent.click(screen.getByRole('button', { name: /ok/i }));
    await waitFor(() => expect(updateMutate).toHaveBeenCalledTimes(1));
    expect(updateMutate.mock.calls[0][0].body).toMatchObject({ cavingGroupId: 'group-2', visibility: 'cavingGroup' });
  });

  it('says why a save was refused', async () => {
    forEdit = { isLoading: false, isError: false, data: { value: row(11) } };
    updateMutate.mockRejectedValue(new ApiError(403, 'access.caving_group_binding_forbidden'));
    show();

    fireEvent.click(screen.getAllByRole('button', { name: i18n.t('geodata.edit') })[0]);
    await screen.findByTestId('caving-group-picker');
    fireEvent.click(screen.getByRole('button', { name: /ok/i }));

    expect(await screen.findByText(i18n.t('geodata.refusals.groupNotAllowed'))).toBeInTheDocument();
  });

  it('offers the character encoding only on a delimited file that has finished being read', async () => {
    rows = () => [
      { ...row(1), id: 'csv-done', format: 'csv', importStatus: 'imported' } as unknown as GeofileInfo,
      { ...row(2), id: 'csv-busy', format: 'csv', importStatus: 'importing' } as unknown as GeofileInfo,
      { ...row(3), id: 'csv-queued', format: 'csv', importStatus: 'uploaded' } as unknown as GeofileInfo,
      { ...row(4), id: 'gpx-done', format: 'gpx', importStatus: 'imported' } as unknown as GeofileInfo,
    ];
    show();

    expect(await screen.findByTestId('geodata-encoding-open-csv-done')).toBeEnabled();
    expect(screen.getByTestId('geodata-encoding-open-csv-busy')).toBeDisabled();
    expect(screen.getByTestId('geodata-encoding-open-csv-queued')).toBeDisabled();
    // Every other format states its own encoding.
    expect(screen.queryByTestId('geodata-encoding-open-gpx-done')).toBeNull();
  });
});
