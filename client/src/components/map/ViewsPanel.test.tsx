// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { MapImageExport, ViewConfig } from '../../map/viewConfig.ts';

let catalog: { id: number; name: string; layerKind: string; isBase: boolean; inDocuments: boolean }[] = [];
let exported: MapImageExport = { refused: 'failed' };
const createSpy = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useMapViews: () => ({ data: [] }),
  useMapLayers: () => ({ data: catalog }),
  useCreateMapView: () => ({ mutateAsync: createSpy, isPending: false }),
  useDeleteMapView: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useShareMapView: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock('../../map/viewConfig.ts', async () => {
  const actual = await vi.importActual<typeof import('../../map/viewConfig.ts')>('../../map/viewConfig.ts');
  return { ...actual, exportMapImage: () => Promise.resolve(exported) };
});
vi.mock('./ViewShareDialog.tsx', () => ({ default: () => null }));

const { default: ViewsPanel } = await import('./ViewsPanel.tsx');

afterEach(() => cleanup());

const VIEW: ViewConfig = {
  configVersion: 1,
  center: [25.3, 45.7],
  zoom: 10,
  entrancesVisible: true,
  surfaceFeaturesVisible: true,
  geofileIds: [],
  rasters: [],
  tagFilter: null,
};

function exportImage() {
  render(
    <App>
      <ViewsPanel onCapture={() => VIEW} onApply={() => {}} />
    </App>,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Export map image' }));
}

describe('exporting the map as an image over a layer that does not allow it', () => {
  it('names the layer, and the installation’s own background allowed in documents', async () => {
    await i18n.changeLanguage('en');
    catalog = [
      { id: 1, name: 'Topographic', layerKind: 'xyz', isBase: true, inDocuments: false },
      { id: 2, name: 'Orthophoto', layerKind: 'xyz', isBase: true, inDocuments: true },
    ];
    exported = { refused: 'unreadable', layers: ['Topographic'] };

    exportImage();

    const said = await screen.findByText(/cannot be put into an image while it shows Topographic/);
    expect(said.textContent).toContain('such as Orthophoto');
    expect(said.textContent).not.toContain('OpenStreetMap');
  });

  it('says so when the installation has no background allowed in documents', async () => {
    catalog = [{ id: 1, name: 'Topographic', layerKind: 'xyz', isBase: true, inDocuments: false }];
    exported = { refused: 'unreadable', layers: ['Topographic'] };

    exportImage();

    expect(await screen.findByText(/None of this installation’s backgrounds may be put into an image/)).toBeTruthy();
  });
});

describe('saving a view whose filter holds a place of its own', () => {
  function saveAs(name: string, captured: ViewConfig) {
    render(
      <App>
        <ViewsPanel onCapture={() => captured} onApply={() => {}} />
      </App>,
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Name of the view to save' }), { target: { value: name } });
    fireEvent.click(screen.getByRole('button', { name: 'Save current view' }));
  }

  it('says why and asks nothing, since a saved view never stores a typed point', async () => {
    await i18n.changeLanguage('en');
    createSpy.mockReset().mockResolvedValue({});
    const typedPoint = withWhere(emptyDocument(['feature']), 'feature', {
      node: 'condition',
      field: 'position',
      op: 'within',
      values: [{ type: 'number', value: 500 }, { type: 'point', longitude: 23.5, latitude: 45.25 }],
    });

    saveAs('Around the spring', { ...VIEW, filterDocument: typedPoint });

    expect(await screen.findByText(/a saved view never stores one/)).toBeTruthy();
    expect(createSpy).not.toHaveBeenCalled();
  });

  it('saves a view whose filter names no place of its own', async () => {
    await i18n.changeLanguage('en');
    createSpy.mockReset().mockResolvedValue({});
    const plain = withWhere(emptyDocument(['feature']), 'feature', {
      node: 'condition',
      field: 'surveyedLength',
      op: 'greaterThan',
      values: [{ type: 'number', value: 300 }],
    });

    saveAs('Long caves', { ...VIEW, filterDocument: plain });

    await vi.waitFor(() => expect(createSpy).toHaveBeenCalledTimes(1));
    expect(createSpy.mock.calls[0]?.[0]).toMatchObject({ name: 'Long caves', config: { filterDocument: plain } });
  });
});
