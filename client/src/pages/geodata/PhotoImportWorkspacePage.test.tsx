// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PhotoCandidate, PhotoPreview, PhotoSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();

let session: PhotoSession = {
  fileIds: ['file-1', 'file-2', 'file-3'],
  options: {
    defaultKind: 'caveEntrance',
    clusterRadiusMeters: 25,
    proximityRadiusMeters: 80,
    visibility: 'private',
    locationProtected: false,
    tagIds: [],
    elevation: 'discard',
    cameraClockOffsetSeconds: 0,
    trackMatchToleranceSeconds: 120,
  },
  decisions: {},
  updatedAt: null,
};

/** Three pictures of one entrance: one candidate with a gallery, not three rows. */
const entrance: PhotoCandidate = {
  key: 'file-1',
  geom: { type: 'Point', coordinates: [25.51, 45.61] },
  positionSource: 'exif',
  members: [
    {
      fileId: 'file-1',
      originalName: 'Pestera Ursilor.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      capturedAt: '2026-08-08T12:00:00Z',
      positionSource: 'exif',
      altitudeMeters: 812,
      directionDegrees: 137,
      directionIsMagnetic: false,
      dop: 1.4,
      confidence: 'excellent',
      hasOwnPosition: true,
      thumbnailUrl: '/api/v1/files/file-1/thumbnail?size=160&token=t',
    },
    {
      fileId: 'file-2',
      originalName: 'IMG_2044.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      capturedAt: '2026-08-08T12:01:00Z',
      positionSource: 'exif',
      altitudeMeters: null,
      directionDegrees: null,
      directionIsMagnetic: false,
      dop: null,
      confidence: 'unknown',
      hasOwnPosition: true,
      thumbnailUrl: '/api/v1/files/file-2/thumbnail?size=160&token=t',
    },
  ],
  proposedName: 'Pestera Ursilor',
  altitudeMeters: 812,
  directionDegrees: 137,
  directionIsMagnetic: false,
  dop: 1.4,
  confidence: 'excellent',
  trackMatchSecondsFromFix: null,
  trackMatchInterpolated: false,
  nearby: [
    {
      featureId: 'feat-9',
      name: null,
      kind: 'caveEntrance',
      distanceMeters: 18.2,
      caveFeatureId: 'cave-9',
      caveName: 'Izbucul Mare',
    },
  ],
  decision: null,
};

/** A picture nothing could place: its own row, and not selectable. */
const unplaced: PhotoCandidate = {
  ...entrance,
  key: 'file-3',
  geom: null,
  positionSource: 'none',
  members: [
    {
      ...entrance.members[0],
      fileId: 'file-3',
      originalName: 'IMG_3000.jpg',
      positionSource: 'none',
      altitudeMeters: null,
      directionDegrees: null,
      dop: null,
      confidence: 'unknown',
      hasOwnPosition: false,
    },
  ],
  proposedName: null,
  altitudeMeters: null,
  directionDegrees: null,
  dop: null,
  confidence: 'unknown',
  nearby: [],
};

const preview: PhotoPreview = {
  items: [entrance, unplaced],
  page: 1,
  pageSize: 25,
  totalItems: 2,
  allKeys: ['file-1', 'file-3'],
  // The unplaced picture is filtered in but not selectable: nothing puts it anywhere, so
  // "select all" must not take it.
  selectableKeys: ['file-1'],
  placedCount: 1,
  unplacedCount: 1,
  photoCount: 3,
  trackFixCount: 0,
  trackFirstFixAt: null,
  trackLastFixAt: null,
  unreadableFileIds: [],
};

vi.mock('../../api/hooks.ts', () => ({
  usePhotoImportSession: () => ({ data: session, isError: false }),
  useSavePhotoImportSession: () => ({ mutate: saveSessionMutate }),
  usePhotoImportPreview: () => ({ data: preview, isFetching: false }),
  useCommitPhotoImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  usePhotoImportTracks: () => ({ data: [], isLoading: false }),
  useUploadFile: () => ({ mutateAsync: vi.fn() }),
  useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }] }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'natural', name: 'Natural' }] }),
  useFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole' }] }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
}));

// The map builds a real OpenLayers instance against a canvas jsdom does not have; the
// workspace is a table beside a map, and this file is about the table.
vi.mock('../../components/import/PhotoCandidateMap.tsx', () => ({
  default: () => <div data-testid="photo-candidate-map" />,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useSearchParams: () => [new URLSearchParams(), vi.fn()],
  };
});

const { default: PhotoImportWorkspacePage } = await import('./PhotoImportWorkspacePage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <PhotoImportWorkspacePage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('PhotoImportWorkspacePage', () => {
  beforeEach(() => {
    commitMutate.mockReset();
    commitMutate.mockResolvedValue({
      batch: { id: 'batch-1', createdCount: 1, attachedCount: 0, skippedCount: 1 },
      failures: [],
    });
    saveSessionMutate.mockReset();
  });

  it('shows one row per place rather than one per picture', async () => {
    show();

    // The name a person gave one of the pictures is proposed; the camera's own names are not.
    expect(await screen.findByDisplayValue('Pestera Ursilor')).toBeInTheDocument();
    expect(screen.getByText(/Pestera Ursilor.jpg/)).toBeInTheDocument();
    expect(screen.getByText(/\+1$/)).toBeInTheDocument();
  });

  it('says how a place came to be where it is, and how much that is worth', async () => {
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    expect(screen.getByText('From the camera')).toBeInTheDocument();
    expect(screen.getByText('Excellent fix')).toBeInTheDocument();
    // The bearing is the thing that turns "somewhere on this slope" into a hole to walk to.
    expect(screen.getByText('Facing 137°')).toBeInTheDocument();
  });

  it('warns about pictures nothing could place and refuses to select them', async () => {
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    expect(screen.getByText('1 of the pictures have no position')).toBeInTheDocument();

    const boxes = screen.getAllByRole('checkbox');
    // The header box plus one per row; the unplaced row's is disabled.
    expect(boxes.some((box) => (box as HTMLInputElement).disabled)).toBe(true);
  });

  it('creates only what was selected, and nothing until the button is pressed', async () => {
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    expect(commitMutate).not.toHaveBeenCalled();

    // The header checkbox selects everything selectable, which excludes the unplaced row.
    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    fireEvent.click(screen.getByTestId('photo-import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    expect(commitMutate.mock.calls[0][0].selection).toEqual(['file-1']);
  });

  it('turns a nearby object into a decision that creates nothing', async () => {
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    fireEvent.mouseDown(screen.getByText('Create a new one'));
    fireEvent.click(await screen.findByText('Izbucul Mare · 18 m'));

    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    fireEvent.click(screen.getByTestId('photo-import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    expect(commitMutate.mock.calls[0][0].decisions['file-1']).toMatchObject({
      action: 'attach',
      attachToFeatureId: 'feat-9',
    });
  });
});
