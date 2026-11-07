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

vi.mock('../../../api/hooks.ts', () => ({
  usePreviewRasterPlacement: () => ({ mutateAsync: preview, isPending: false }),
  useSaveRasterPlacement: () => ({ mutateAsync: save, isPending: false }),
  useClearRasterPlacement: () => ({ mutateAsync: clear, isPending: false }),
}));

vi.mock('./PlacementMap.tsx', async () => {
  const { useEffect } = await import('react');
  return {
    default: function FakePlacementMap({ onViewReady, onCornersChange, corners, preview: shown }: PlacementMapProps) {
      useEffect(() => {
        // A box in the map's projection around 24.0 E 45.5 N.
        onViewReady([2671000, 5701000, 2673000, 5703000]);
        // eslint-disable-next-line react-hooks/exhaustive-deps
      }, []);
      return (
        <div data-testid="fake-map" data-preview={shown ? 'yes' : 'no'}>
          <button
            data-testid="fake-drag"
            onClick={() => corners && onCornersChange({ ...corners, se: [24.005, 45.4995] })}
          />
        </div>
      );
    },
  };
});

vi.mock('./SheetPicker.tsx', () => ({ default: () => <div data-testid="fake-sheet" /> }));

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
