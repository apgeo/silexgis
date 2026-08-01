// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FeatureType } from '../../api/hooks.ts';
import MapContextMenu from './MapContextMenu.tsx';

afterEach(cleanup);

const types = [
  { id: 1, name: 'Sinkhole', acceptedGeometryClasses: ['point'] },
  { id: 2, name: 'Fault', acceptedGeometryClasses: ['lineString'] },
] as FeatureType[];

const target = { pixel: [100, 120] as [number, number], lonLat: [25.5, 45.25] as [number, number] };

function renderMenu(over: Partial<Parameters<typeof MapContextMenu>[0]> = {}) {
  const props = {
    target,
    featureTypes: types,
    canEdit: true,
    onClose: vi.fn(),
    onAddFeature: vi.fn(),
    onPlace: vi.fn(),
    ...over,
  };
  render(
    <App>
      <MapContextMenu {...props} />
    </App>,
  );
  return props;
}

describe('MapContextMenu', () => {
  it('offers add submenus and placement items to editors', () => {
    renderMenu();
    expect(screen.getByText('Add feature')).toBeInTheDocument();
    expect(screen.getByText('New cave here')).toBeInTheDocument();
    expect(screen.getByText('New entrance here')).toBeInTheDocument();
    expect(screen.getByText('Copy coordinates')).toBeInTheDocument();
  });

  it('offers only coordinate copy to non-editors', () => {
    renderMenu({ canEdit: false });
    expect(screen.queryByText('Add feature')).not.toBeInTheDocument();
    expect(screen.queryByText('New cave here')).not.toBeInTheDocument();
    expect(screen.getByText('Copy coordinates')).toBeInTheDocument();
  });

  it('reports cave placement with the click coordinate and closes', () => {
    const props = renderMenu();
    fireEvent.click(screen.getByText('New cave here'));
    expect(props.onPlace).toHaveBeenCalledWith('add-cave', [25.5, 45.25]);
    expect(props.onClose).toHaveBeenCalled();
  });

  it('copies the coordinate as lat, lon', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    const props = renderMenu();

    fireEvent.click(screen.getByText('Copy coordinates'));

    expect(writeText).toHaveBeenCalledWith('45.250000, 25.500000');
    expect(props.onClose).toHaveBeenCalled();
  });
});
