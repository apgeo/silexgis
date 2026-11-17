// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { ApiError } from '../../api/client.ts';
import i18n from '../../i18n';
import type { RasterMapInfo } from '../../api/hooks.ts';

/**
 * Drawing a map sheet again: the request carries the page, the layers and the resolution the
 * dialog shows, and a refusal names what was wrong with them instead of closing as if it had
 * worked. Every sheet here is invented.
 */

const t = i18n.getFixedT('en');
const renderSheet = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useRenderRasterMap: () => ({ mutateAsync: renderSheet, isPending: false }),
}));

const { default: RedrawSheetModal } = await import('./RedrawSheetModal.tsx');

const sheet = (overrides: Partial<RasterMapInfo> = {}): RasterMapInfo =>
  ({
    id: 'sheet-1',
    name: 'Invented sheet',
    sourcePageCount: 3,
    sourcePage: 2,
    sourceLayers: ['Walls', 'Labels'],
    renderLayers: null,
    renderDpi: 300,
    ...overrides,
  }) as unknown as RasterMapInfo;

let onClose: Mock<() => void>;

beforeEach(() => {
  renderSheet.mockReset();
  onClose = vi.fn();
});

afterEach(cleanup);

function show(raster: RasterMapInfo) {
  render(
    <App>
      <RedrawSheetModal raster={raster} onClose={onClose} />
    </App>,
  );
}

async function submit() {
  await act(async () => {
    fireEvent.click(screen.getByTestId('redraw-submit'));
  });
}

describe('redrawing a sheet', () => {
  it('sends the page, the layers and the resolution it opened with, then says it is queued', async () => {
    renderSheet.mockResolvedValue(undefined);
    show(sheet());

    await submit();

    expect(renderSheet).toHaveBeenCalledWith({
      id: 'sheet-1',
      body: { page: 2, layers: null, dpi: 300, allLayers: true },
    });
    expect(await screen.findByText(t('geodata.redraw.queued'))).toBeInTheDocument();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('sends only the layers left ticked once "all layers" is taken off', async () => {
    renderSheet.mockResolvedValue(undefined);
    show(sheet());

    fireEvent.click(screen.getByTestId('redraw-all-layers'));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Labels' }));
    await submit();

    expect(renderSheet).toHaveBeenCalledWith({
      id: 'sheet-1',
      body: { page: 2, layers: ['Walls'], dpi: 300, allLayers: false },
    });
  });

  it.each([
    ['a page the document does not have', new ApiError(400, 'georeferenced_map.page_out_of_range'), 'geodata.placement.errors.pageOutOfRange'],
    ['a resolution out of range', new ApiError(400, 'georeferenced_map.dpi_out_of_range'), 'geodata.placement.errors.dpiOutOfRange'],
    ['a failure with no words of its own', new TypeError('Failed to fetch'), 'geodata.placement.errors.generic'],
  ])('names %s and stays open', async (_label, failure, key) => {
    renderSheet.mockRejectedValue(failure);
    show(sheet());

    await submit();

    expect(await screen.findByText(t(key))).toBeInTheDocument();
    expect(screen.queryByText(t('geodata.redraw.queued'))).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
