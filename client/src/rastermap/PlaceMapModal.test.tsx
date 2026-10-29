// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import type { SheetMarker } from './coordinates.ts';
import PlaceMapModal from './PlaceMapModal.tsx';

/**
 * The map screen of a phone's map: what it draws, and which write each control sends. The drawing
 * is faked down to its click contract and the writes are spies.
 */
const createPoint = vi.fn();
const movePoint = vi.fn();
const deleteLink = vi.fn();
let links: ResLink[] = [];

vi.mock('../api/hooks.ts', () => ({
  useDocument: () => ({ data: { id: 'doc-1', currentFileId: 'file-1' } }),
  useFile: () => ({
    data: {
      id: 'file-1',
      contentUrl: 'http://files.local/original.tif',
      thumbnailUrl: 'http://files.local/file-1/thumbnail?size=480&token=t',
      mayDownloadOriginal: true,
    },
  }),
  useDeleteResLink: () => ({ mutateAsync: deleteLink, isPending: false }),
}));

vi.mock('../api/hooks/placePoints.ts', () => ({
  usePlacePointLinks: () => ({ data: links }),
  useCreatePlacePoint: () => ({ mutateAsync: createPoint, isPending: false }),
  useMovePlacePoint: () => ({ mutateAsync: movePoint, isPending: false }),
}));

vi.mock('../components/reslinks/ResLinkTargetPicker.tsx', () => ({
  default: ({ onChange }: { onChange: (id: string | null, title: string | null) => void }) => (
    <button data-testid="fake-pick-place" onClick={() => onChange('place-new', 'Galeria Nouă')} />
  ),
}));

vi.mock('./RasterMapView.tsx', () => ({
  default: function FakeRasterMapView({
    imageUrl,
    markers,
    onMapClick,
    onMarkerClick,
  }: {
    imageUrl: string;
    markers: readonly SheetMarker[];
    onMapClick?: (at: { x: number; y: number }) => void;
    onMarkerClick?: (marker: SheetMarker, at: { x: number; y: number } | null) => void;
  }) {
    return (
      <div data-testid="fake-view" data-image={imageUrl} data-placing={String(onMapClick !== undefined)}>
        <button data-testid="fake-click-sheet" onClick={() => onMapClick?.({ x: 0.5, y: 0.25 })} />
        {markers.map((marker) => (
          <button
            key={marker.key}
            data-testid={`fake-marker-${marker.label}`}
            onClick={() => onMarkerClick?.(marker, { x: 0.3, y: 0.6 })}
          />
        ))}
      </div>
    );
  },
}));

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: 'm',
    targetType: 'document',
    targetId: 'doc-1',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

function pin(id: string, name: string, options: Partial<{ fileId: string; mayEdit: boolean; anchor: unknown }> = {}) {
  return {
    id,
    shortCode: 'ABCDEFGH',
    relationType: { id: 1, code: 'map-place-point', name: 'x', directed: true, inverseName: 'x' },
    description: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    mayEdit: options.mayEdit ?? true,
    members: [
      member({
        id: `${id}-picture`,
        isMain: true,
        anchorKind: 'imageRegion',
        anchor: (options.anchor ?? { shape: 'point', x: 0.2, y: 0.4, px: 1, py: 2 }) as ResLinkMember['anchor'],
        anchorFileId: options.fileId ?? 'file-1',
        anchorState: options.fileId === undefined ? 'exact' : 'degraded',
      }),
      member({
        id: `${id}-place`,
        targetType: 'feature',
        targetId: `place-${id}`,
        display: { title: name, subtitle: null, route: null, thumbnailUrl: null },
      }),
    ],
  } as unknown as ResLink;
}

function show(canEdit = true) {
  return render(
    <App>
      <PlaceMapModal open onClose={() => undefined} documentId="doc-1" title="Plan" canEdit={canEdit} />
    </App>,
  );
}

beforeEach(() => {
  createPoint.mockReset().mockResolvedValue({ id: 'made' });
  movePoint.mockReset().mockResolvedValue({ id: 'moved' });
  deleteLink.mockReset().mockResolvedValue(undefined);
  links = [];
});

afterEach(() => {
  cleanup();
});

describe('a phone map opened as a map', () => {
  it('draws the server rendition, never the stored bytes, even for a reader who may download them', () => {
    show();
    const image = screen.getByTestId('fake-view').getAttribute('data-image')!;
    expect(image).toContain('/thumbnail?');
    expect(image).toContain('size=2400');
    expect(image).toContain('token=t');
  });

  it('draws each place as a marker named for it, and counts what it cannot draw', () => {
    links = [
      pin('a', 'Sala Mare'),
      pin('old', 'Puțul', { fileId: 'file-0' }),
      pin('off', 'Afară', { anchor: { shape: 'point', px: 9000, py: 1 } }),
    ];
    show();
    expect(screen.getByTestId('fake-marker-Sala Mare')).toBeInTheDocument();
    expect(screen.queryByTestId('fake-marker-Puțul')).not.toBeInTheDocument();
    expect(screen.getByTestId('placemap-earlier')).toHaveTextContent('1 point was measured on an earlier picture.');
    expect(screen.getByTestId('placemap-offpicture')).toHaveTextContent('1 point is not on this picture.');
  });

  it('moves a pressed place to the next point clicked, on the current picture', async () => {
    links = [pin('a', 'Sala Mare')];
    show();
    fireEvent.click(screen.getByTestId('fake-marker-Sala Mare'));
    fireEvent.click(await screen.findByTestId('placemap-move'));
    expect(screen.getByTestId('fake-view')).toHaveAttribute('data-placing', 'true');
    fireEvent.click(screen.getByTestId('fake-click-sheet'));
    await waitFor(() =>
      expect(movePoint).toHaveBeenCalledWith({ id: 'a', body: { fileId: 'file-1', x: 0.5, y: 0.25 } }),
    );
  });

  it('removes a pressed place once the removal is confirmed', async () => {
    links = [pin('a', 'Sala Mare')];
    show();
    fireEvent.click(screen.getByTestId('fake-marker-Sala Mare'));
    fireEvent.click(await screen.findByTestId('placemap-delete'));
    // The confirmation's own button, beside the one that asked.
    const confirm = await waitFor(() => {
      const buttons = screen.getAllByRole('button', { name: 'Delete' });
      expect(buttons).toHaveLength(2);
      return buttons.find((button) => button.getAttribute('data-testid') !== 'placemap-delete')!;
    });
    fireEvent.click(confirm);
    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith('a'));
  });

  it('adds a point: a click on the map, then the place it is', async () => {
    show();
    fireEvent.click(screen.getByTestId('placemap-add'));
    fireEvent.click(screen.getByTestId('fake-click-sheet'));
    expect(await screen.findByTestId('placemap-save')).toBeDisabled();
    fireEvent.click(screen.getByTestId('fake-pick-place'));
    fireEvent.click(screen.getByTestId('placemap-save'));
    await waitFor(() =>
      expect(createPoint).toHaveBeenCalledWith({
        documentId: 'doc-1',
        placeId: 'place-new',
        fileId: 'file-1',
        x: 0.5,
        y: 0.25,
      }),
    );
  });

  it('offers no change to a reader who may not change the cave, and says why on a pressed place', async () => {
    links = [pin('a', 'Sala Mare', { mayEdit: false })];
    show(false);
    expect(screen.queryByTestId('placemap-add')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('fake-marker-Sala Mare'));
    expect(await screen.findByText('You may not change the points of this map.')).toBeInTheDocument();
    expect(screen.queryByTestId('placemap-move')).not.toBeInTheDocument();
  });
});
