// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/client.ts';
import '../../i18n';
import i18n from '../../i18n';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { MapImageExport, ViewConfig } from '../../map/viewConfig.ts';

let catalog: { id: number; name: string; layerKind: string; isBase: boolean; inDocuments: boolean }[] = [];
let exported: MapImageExport = { refused: 'failed' };
const createSpy = vi.fn();
const deleteSpy = vi.fn();
const shareSpy = vi.fn();
let views: { id: string; name: string; isHome: boolean; config: object }[] = [];
/** What the read of the views failed with, when a test makes it fail. */
let viewsFailure: unknown;

vi.mock('../../api/hooks.ts', () => ({
  useMapViews: () =>
    viewsFailure === undefined
      ? { data: views, isError: false, error: null, refetch: vi.fn() }
      : { data: undefined, isError: true, error: viewsFailure, refetch: vi.fn() },
  useMapLayers: () => ({ data: catalog }),
  useCreateMapView: () => ({ mutateAsync: createSpy, isPending: false }),
  useDeleteMapView: () => ({ mutateAsync: deleteSpy, isPending: false }),
  useShareMapView: () => ({ mutateAsync: shareSpy, isPending: false }),
}));
vi.mock('../../map/viewConfig.ts', async () => {
  const actual = await vi.importActual<typeof import('../../map/viewConfig.ts')>('../../map/viewConfig.ts');
  return { ...actual, exportMapImage: () => Promise.resolve(exported) };
});
// The dialog is tested on its own; here it only shows what the panel handed it.
vi.mock('./ViewShareDialog.tsx', () => ({
  default: ({ shared }: { shared: { name: string; url: string; leavesOut: unknown } | null }) =>
    shared ? (
      <div data-testid="shared-link" data-name={shared.name} data-leaves-out={JSON.stringify(shared.leavesOut)}>
        {shared.url}
      </div>
    ) : null,
}));

const { default: ViewsPanel } = await import('./ViewsPanel.tsx');

afterEach(() => {
  cleanup();
  views = [];
  createSpy.mockReset();
  deleteSpy.mockReset();
  shareSpy.mockReset();
});

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

describe('a failed read of the saved views', () => {
  it('says the views could not be read, rather than showing a blank list', () => {
    viewsFailure = new TypeError('Failed to fetch');
    try {
      render(
        <App>
          <ViewsPanel onCapture={() => VIEW} onApply={() => {}} />
        </App>,
      );
      expect(screen.getByTestId('views-load-failed')).toBeTruthy();
    } finally {
      viewsFailure = undefined;
    }
  });
});

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

describe('a saved-view write the server refuses', () => {
  function panel() {
    render(
      <App>
        <ViewsPanel onCapture={() => VIEW} onApply={() => {}} />
      </App>,
    );
  }

  it('says that the person may not save views, rather than only that the save failed', async () => {
    await i18n.changeLanguage('en');
    createSpy.mockReset().mockRejectedValue(new ApiError(403, 'access.create_forbidden'));
    panel();
    fireEvent.change(screen.getByRole('textbox', { name: 'Name of the view to save' }), { target: { value: 'Mine' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save current view' }));

    expect(await screen.findByText(/You may not save map views/)).toBeTruthy();
  });

  it('says a view somebody else deleted is gone, when it is shared or deleted', async () => {
    await i18n.changeLanguage('en');
    views = [{ id: 'v1', name: 'Plateau', isHome: false, config: VIEW }];
    shareSpy.mockReset().mockRejectedValue(new ApiError(404, 'map_view.not_found'));
    deleteSpy.mockReset().mockRejectedValue(new ApiError(404, 'map_view.not_found'));
    panel();

    fireEvent.click(screen.getByRole('button', { name: /Share.*Plateau/ }));
    expect(await screen.findAllByText(/This saved view is no longer there/)).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: /Delete.*Plateau/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(deleteSpy).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(screen.getAllByText(/This saved view is no longer there/)).toHaveLength(2));
  });
});

describe('the saved views themselves', () => {
  function showViews(onApply: (view: unknown) => void = () => {}) {
    render(
      <App>
        <ViewsPanel onCapture={() => VIEW} onApply={onApply} />
      </App>,
    );
  }

  it('applies the view whose name is pressed', () => {
    views = [
      { id: 'v1', name: 'Bihor overview', isHome: false, config: VIEW },
      { id: 'v2', name: 'Padiș close', isHome: false, config: VIEW },
    ];
    const onApply = vi.fn();
    showViews(onApply);

    fireEvent.click(screen.getByRole('button', { name: 'Padiș close' }));

    expect(onApply).toHaveBeenCalledWith(views[1]);
  });

  it('saves the view privately, as an ordinary view, and empties the name box', async () => {
    createSpy.mockResolvedValue({});
    showViews();

    const box = screen.getByRole('textbox');
    fireEvent.change(box, { target: { value: '  Bihor overview ' } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('views.save') }));

    await waitFor(() => expect(createSpy).toHaveBeenCalledTimes(1));
    expect(createSpy).toHaveBeenCalledWith({
      name: 'Bihor overview',
      description: null,
      config: VIEW,
      isHome: false,
      cavingGroupId: null,
      visibility: 'private',
    });
    await waitFor(() => expect(box).toHaveValue(''));
  });

  it('keeps the typed name when the save is refused', async () => {
    createSpy.mockRejectedValue(new TypeError('Failed to fetch'));
    showViews();

    const box = screen.getByRole('textbox');
    fireEvent.change(box, { target: { value: 'Bihor overview' } });
    fireEvent.click(screen.getByRole('button', { name: i18n.t('views.save') }));

    await waitFor(() => expect(createSpy).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(i18n.t('common.saveFailed'))).toBeInTheDocument();
    expect(box).toHaveValue('Bihor overview');
  });

  it('shows the shared link at this installation\'s own address, under the view\'s name', async () => {
    views = [{ id: 'v1', name: 'Bihor overview', isHome: false, config: VIEW }];
    shareSpy.mockResolvedValue({ id: 'v1', shareToken: 'tok-123' });
    showViews();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('a11y.shareView', { name: 'Bihor overview' }) }));

    const link = await screen.findByTestId('shared-link');
    expect(shareSpy).toHaveBeenCalledWith('v1');
    expect(link.textContent).toBe(`${window.location.origin}/shared/view/tok-123`);
    expect(link).toHaveAttribute('data-name', 'Bihor overview');
  });
});
