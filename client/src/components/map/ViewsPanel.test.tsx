// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { MapImageExport } from '../../map/viewConfig.ts';

let catalog: { id: number; name: string; layerKind: string; isBase: boolean; inDocuments: boolean }[] = [];
let exported: MapImageExport = { refused: 'failed' };

vi.mock('../../api/hooks.ts', () => ({
  useMapViews: () => ({ data: [] }),
  useMapLayers: () => ({ data: catalog }),
  useCreateMapView: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

function exportImage() {
  render(
    <App>
      <ViewsPanel onCapture={() => ({})} onApply={() => {}} />
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
