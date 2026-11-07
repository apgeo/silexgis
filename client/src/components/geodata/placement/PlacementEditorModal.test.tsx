// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import type { RasterMapInfo } from '../../../api/hooks.ts';
import { cornersToPlacement, type Corners } from '../../../geodata/placement/placement.ts';
import type { PlacementMapProps } from './PlacementMap.tsx';
import PlacementEditorModal from './PlacementEditorModal.tsx';

/**
 * The placement editor: which request each control sends. The map is faked down to its contract
 * — the box it first shows, the corners after a drag — and the three routes are spies.
 */
const preview = vi.fn();
const save = vi.fn();
const clear = vi.fn();
const importAnnotation = vi.fn();

vi.mock('../../../api/hooks.ts', () => ({
  usePreviewRasterPlacement: () => ({ mutateAsync: preview, isPending: false }),
  useSaveRasterPlacement: () => ({ mutateAsync: save, isPending: false }),
  useClearRasterPlacement: () => ({ mutateAsync: clear, isPending: false }),
  useImportRasterAnnotation: () => ({ mutateAsync: importAnnotation, isPending: false }),
  fetchRasterAnnotation: vi.fn(),
}));

// No grid definitions here: longitude and latitude need none, and a grid is the server's to define.
vi.mock('../../../map/projections.ts', () => ({
  canPlace: () => false,
  resolveProjection: () => Promise.resolve(false),
}));

vi.mock('./PlacementMap.tsx', async () => {
  const { useEffect } = await import('react');
  return {
    default: function FakePlacementMap({
      onViewReady,
      onCornersChange,
      corners,
      preview: shown,
      onMapClick,
      markers,
    }: PlacementMapProps) {
      useEffect(() => {
        // A box in the map's projection around 24.0 E 45.5 N.
        onViewReady([2671000, 5701000, 2673000, 5703000]);
        // eslint-disable-next-line react-hooks/exhaustive-deps
      }, []);
      return (
        <div
          data-testid="fake-map"
          data-preview={shown ? 'yes' : 'no'}
          data-markers={markers.filter((m) => m.lonLat !== null).length}
        >
          <button data-testid="fake-map-click" onClick={() => onMapClick?.([24.003, 45.501])} />
          <button
            data-testid="fake-drag"
            onClick={() => corners && onCornersChange({ ...corners, se: [24.005, 45.4995] })}
          />
        </div>
      );
    },
  };
});

vi.mock('./SheetPicker.tsx', () => ({
  default: function FakeSheetPicker({
    onPick,
    onMaskChange,
  }: {
    onPick?: (u: number, v: number) => void;
    onMaskChange?: (mask: [number, number][] | null) => void;
  }) {
    return (
      <div data-testid="fake-sheet">
        <button data-testid="fake-sheet-pick" onClick={() => onPick?.(0.25, 0.75)} />
        <button
          data-testid="fake-sheet-outline"
          onClick={() =>
            onMaskChange?.([
              [0.1, 0.1],
              [0.9, 0.1],
              [0.9, 0.9],
              [0.1, 0.9],
            ])
          }
        />
      </div>
    );
  },
}));

const corners: Corners = {
  nw: [24.0, 45.502],
  ne: [24.004, 45.502],
  se: [24.004, 45.5],
  sw: [24.0, 45.5],
};

function sheet(overrides: Partial<RasterMapInfo> = {}): RasterMapInfo {
  return {
    id: 'sheet-1',
    name: 'Planșa de probă',
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
    caveFeatureId: null,
    ownerUserId: 'user-1',
    cavingGroupId: null,
    visibility: 'private',
    cogUrl: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    sourceFileId: 'pdf-1',
    renderWidth: 2400,
    renderHeight: 1680,
    hasEmbeddedPlacement: false,
    placement: cornersToPlacement(corners, 'homography'),
    placementSource: 'manual',
    previewUrl: 'http://files.local/preview.webp',
    ...overrides,
  };
}

const answer = {
  image: 'data:image/png;base64,AA==',
  extent3857: [2671000, 5701000, 2673000, 5703000],
  extent4326: [24.0, 45.5, 24.004, 45.502],
  width: 1024,
  height: 717,
  rmsMetres: 0.03,
  residuals: [
    { index: 0, metres: 0.5 },
    { index: 1, metres: 0.25 },
    { index: 2, metres: 0.5 },
    { index: 3, metres: 0.25 },
  ],
};

const onClose = vi.fn();

function show(raster: RasterMapInfo = sheet()) {
  return render(
    <App>
      <PlacementEditorModal raster={raster} onClose={onClose} />
    </App>,
  );
}

type PreviewCall = { id: string; body: ReturnType<typeof cornersToPlacement>; signal: AbortSignal };
const lastPreview = () => (preview.mock.lastCall as [PreviewCall])[0];

beforeEach(() => {
  preview.mockReset().mockResolvedValue(answer);
  save.mockReset().mockResolvedValue(sheet({ status: 'processing' }));
  clear.mockReset().mockResolvedValue(sheet({ status: 'unplaced' }));
  importAnnotation.mockReset().mockResolvedValue(sheet({ status: 'processing' }));
  onClose.mockReset();
});

afterEach(cleanup);

describe('the placement editor', () => {
  it('asks for a preview of the corners a saved placement left, as fractions in WGS 84', async () => {
    show();
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    const { id, body, signal } = lastPreview();
    expect(id).toBe('sheet-1');
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(body).toEqual(cornersToPlacement(corners, 'homography'));
    expect(await screen.findByText('Fit: 0.03 m')).toBeInTheDocument();
    expect(screen.getByTestId('fake-map').dataset.preview).toBe('yes');
  });

  it('starts an unplaced sheet in the middle of the view and asks for its preview', async () => {
    show(sheet({ status: 'unplaced', placement: null, placementSource: null }));
    await waitFor(() => expect(preview).toHaveBeenCalled());
    const { body } = lastPreview();
    expect(body.transformation).toBe('homography');
    expect(body.crs).toBe('EPSG:4326');
    const [nw, ne, , sw] = body.gcps;
    expect(nw.x).toBeGreaterThan(23.99);
    expect(nw.x).toBeLessThan(24.02);
    // The sheet's own proportions, measured on the map's projection.
    expect((ne.x - nw.x) / (nw.y - sw.y)).toBeGreaterThan(1.5);
  });

  it('sends a corner typed in the numeric panel', async () => {
    show();
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByText('Type the corners'));
    const input = await screen.findByTestId('placement-corner-nw-lon');
    fireEvent.change(input, { target: { value: '23.999' } });
    // Nothing is asked while the number is still being typed.
    expect(preview).toHaveBeenCalledTimes(1);
    fireEvent.blur(input);
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(2));
    const { body } = lastPreview();
    expect(body.gcps[0]).toMatchObject({ u: 0, v: 0, x: 23.999, y: 45.502 });
    expect(body.gcps[2]).toMatchObject({ u: 1, v: 1, x: 24.004, y: 45.5 });
  });

  it('sends the corners a drag left', async () => {
    show();
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByTestId('fake-drag'));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(2));
    expect(lastPreview().body.gcps[2]).toMatchObject({ x: 24.005, y: 45.4995 });
  });

  it('keeps the sides parallel when asked, and shows each corner’s residual', async () => {
    show();
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByText('Keep parallel'));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(2));
    expect(lastPreview().body.transformation).toBe('affine');
    expect(await screen.findByText(/Top left: 0\.50 m/)).toBeInTheDocument();
  });

  it('saves exactly what it last previewed, then closes', async () => {
    show();
    fireEvent.click(screen.getByText('Keep parallel'));
    await waitFor(() => expect(lastPreview()?.body.transformation).toBe('affine'));
    fireEvent.click(screen.getByTestId('placement-save'));
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save).toHaveBeenCalledWith({ id: 'sheet-1', body: lastPreview().body });
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('says what a refused preview means, in words', async () => {
    preview.mockRejectedValue(new ApiError(400, 'georeferenced_map.placement_points_degenerate'));
    show();
    expect(
      await screen.findByText('The points are too close together or in a line. Spread them out.'),
    ).toBeInTheDocument();
  });

  it('says what a refused save means, and stays open', async () => {
    save.mockRejectedValue(new ApiError(409, 'georeferenced_map.busy'));
    show();
    await waitFor(() => expect(preview).toHaveBeenCalled());
    fireEvent.click(screen.getByTestId('placement-save'));
    expect(
      await screen.findByText('The sheet is being processed. Try again when it has finished.'),
    ).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('offers to remove only a placement made by hand', async () => {
    show(sheet({ placementSource: 'embedded', hasEmbeddedPlacement: true }));
    expect(screen.queryByTestId('placement-remove')).toBeNull();
    cleanup();
    show();
    fireEvent.click(screen.getByTestId('placement-remove'));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await waitFor(() => expect(clear).toHaveBeenCalledWith('sheet-1'));
  });

  it('asks for nothing in two-point mode until two pairs are complete', async () => {
    show(sheet({ status: 'unplaced', placement: null, placementSource: null }));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByText('Two points'));
    expect(screen.getByTestId('fake-sheet')).toBeInTheDocument();
    expect(screen.getByTestId('placement-save')).toBeDisabled();
    await new Promise((resolve) => setTimeout(resolve, 250));
    expect(preview).toHaveBeenCalledTimes(1);
  });
});

describe('the placement editor with control points', () => {
  const unplaced = () => sheet({ status: 'unplaced', placement: null, placementSource: null });

  /** Adds one point: a press on the sheet, then its ground half typed into the table. */
  async function addTyped(index: number, lon: number, lat: number) {
    fireEvent.click(screen.getByTestId('fake-sheet-pick'));
    const x = await screen.findByTestId(`placement-point-${index}-lon`);
    fireEvent.change(x, { target: { value: String(lon) } });
    fireEvent.blur(x);
    const y = screen.getByTestId(`placement-point-${index}-lat`);
    fireEvent.change(y, { target: { value: String(lat) } });
    fireEvent.blur(y);
  }

  async function openControls() {
    show(unplaced());
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByText('Control points'));
    fireEvent.click(screen.getByTestId('placement-add-point'));
  }

  it('adds pairs and asks for an affine preview only once three are complete', async () => {
    await openControls();
    await addTyped(0, 24.001, 45.5015);
    await addTyped(1, 24.003, 45.5015);
    expect(screen.getByTestId('placement-too-few')).toHaveTextContent('Affine needs at least 3 complete points; there are 2.');
    await new Promise((resolve) => setTimeout(resolve, 250));
    expect(preview).toHaveBeenCalledTimes(1);
    // The third point's ground half is chosen on the map.
    fireEvent.click(screen.getByTestId('fake-sheet-pick'));
    fireEvent.click(screen.getByTestId('fake-map-click'));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(2));
    const { body } = lastPreview();
    expect(body.transformation).toBe('affine');
    expect(body.crs).toBe('EPSG:4326');
    expect(body.gcps).toHaveLength(3);
    expect(body.gcps[2]).toMatchObject({ u: 0.25, v: 0.75, x: 24.003, y: 45.501 });
    expect(screen.getByTestId('fake-map').dataset.markers).toBe('3');
  });

  it('stops asking when the transformation chosen needs more points than there are', async () => {
    await openControls();
    await addTyped(0, 24.001, 45.5015);
    await addTyped(1, 24.003, 45.5015);
    await addTyped(2, 24.002, 45.5005);
    await waitFor(() => expect(lastPreview().body.gcps).toHaveLength(3));
    const asked = preview.mock.calls.length;
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Transformation' }));
    fireEvent.click(await screen.findByText('Perspective — at least 4 points'));
    expect(await screen.findByTestId('placement-too-few')).toHaveTextContent(
      'Perspective needs at least 4 complete points; there are 3.',
    );
    expect(screen.getByTestId('placement-save')).toBeDisabled();
    await new Promise((resolve) => setTimeout(resolve, 250));
    expect(preview).toHaveBeenCalledTimes(asked);
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Transformation' }));
    fireEvent.click(await screen.findByText('Thin-plate spline — at least 3 points'));
    await waitFor(() => expect(lastPreview().body.transformation).toBe('tps'));
  });

  it('shows each point’s residual and singles out one far above the rest', async () => {
    preview.mockResolvedValue({
      ...answer,
      rmsMetres: 1,
      residuals: [
        { index: 0, metres: 0.2 },
        { index: 1, metres: 0.3 },
        { index: 2, metres: 4.5 },
      ],
    });
    await openControls();
    await addTyped(0, 24.001, 45.5015);
    await addTyped(1, 24.003, 45.5015);
    await addTyped(2, 24.002, 45.5005);
    expect(await screen.findByTestId('placement-point-2-residual')).toHaveTextContent('4.5 m');
    expect(screen.getByTestId('placement-point-2-residual').className).toContain('ant-tag');
    expect(screen.getByTestId('placement-point-0-residual').className).not.toContain('ant-tag');
  });

  it('sends the outline drawn on the sheet', async () => {
    await openControls();
    await addTyped(0, 24.001, 45.5015);
    await addTyped(1, 24.003, 45.5015);
    await addTyped(2, 24.002, 45.5005);
    await waitFor(() => expect(lastPreview().body.gcps).toHaveLength(3));
    fireEvent.click(screen.getByTestId('fake-sheet-outline'));
    await waitFor(() => expect(lastPreview().body.mask).toHaveLength(4));
    fireEvent.click(screen.getByTestId('placement-clear-outline'));
    await waitFor(() => expect(lastPreview().body.mask).toBeNull());
  });

  it('opens the document’s own placement point by point, read-only in its own system', async () => {
    const own = {
      version: 1,
      transformation: 'polynomial2',
      crs: 'WKT:PROJCS["Invented grid"]',
      gcps: [
        { u: 0, v: 0, x: 1000, y: 2000, label: null },
        { u: 1, v: 1, x: 1600, y: 1580, label: null },
      ],
      mask: [
        [0.02, 0.02],
        [0.98, 0.02],
        [0.98, 0.98],
      ],
    };
    show(sheet({ placement: own, placementSource: 'embedded', hasEmbeddedPlacement: true }));
    expect(screen.getByText(/document's own coordinate system and cannot be edited/)).toBeInTheDocument();
    expect(screen.getByTestId('placement-point-0-lon')).toBeDisabled();
    expect(screen.getByTestId('placement-use-frame')).toBeInTheDocument();
    expect(screen.queryByTestId('placement-remove')).toBeNull();
    fireEvent.click(screen.getByTestId('placement-start-fresh'));
    expect(screen.queryByTestId('placement-point-0-lon')).toBeNull();
  });

  it('imports a placement file and closes', async () => {
    show();
    const annotation = { type: 'Annotation', body: { type: 'FeatureCollection', features: [] } };
    const input = document.querySelector('input[type=file][accept=".json,application/json"]') as HTMLInputElement;
    fireEvent.change(input, {
      target: { files: [new File([JSON.stringify(annotation)], 'e2e.georef.json', { type: 'application/json' })] },
    });
    await waitFor(() => expect(importAnnotation).toHaveBeenCalledWith({ id: 'sheet-1', annotation }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('says so when the file is not JSON', async () => {
    show();
    const input = document.querySelector('input[type=file][accept=".json,application/json"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(['not json'], 'x.json')] } });
    expect(await screen.findByText('The file is not a placement file (JSON).')).toBeInTheDocument();
    expect(importAnnotation).not.toHaveBeenCalled();
  });
});
