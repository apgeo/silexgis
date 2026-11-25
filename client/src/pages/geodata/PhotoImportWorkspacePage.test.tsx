// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { PhotoCandidate, PhotoPreview, PhotoPreviewRequest, PhotoSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();
// What the save mutation reports about its last call; a test sets it to a failed save.
const saveSessionState: { isError: boolean; isPending: boolean; variables?: unknown } = {
  isError: false,
  isPending: false,
};
const uploadMutate = vi.fn();
const previewRefetch = vi.fn();
const previewCalls: { body: PhotoPreviewRequest; enabled: boolean }[] = [];
let previewFailure: ApiError | null = null;

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

let preview: PhotoPreview;
const fullPreview: PhotoPreview = {
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
preview = fullPreview;

vi.mock('../../api/hooks.ts', () => ({
  usePhotoImportSession: () => ({ data: session, isError: false }),
  useSavePhotoImportSession: () => ({ mutate: saveSessionMutate, ...saveSessionState }),
  usePhotoImportPreview: (body: PhotoPreviewRequest, enabled: boolean) => {
    previewCalls.push({ body, enabled });
    return previewFailure
      ? { data: preview, isFetching: false, isError: true, error: previewFailure, refetch: previewRefetch }
      : { data: preview, isFetching: false, isError: false, refetch: previewRefetch };
  },
  useCommitPhotoImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  usePhotoImportTracks: () => ({ data: [], isLoading: false }),
  useUploadFile: () => ({ mutateAsync: uploadMutate }),
  useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }] }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'natural', name: 'Natural' }] }),
  useFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole' }] }),
  useGenericFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole' }] }),
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
  saveSessionState.isError = false;
  saveSessionState.isPending = false;
  saveSessionState.variables = undefined;
    uploadMutate.mockReset();
    previewRefetch.mockReset();
    previewCalls.length = 0;
    previewFailure = null;
    preview = fullPreview;
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

  // The review is saved as it is worked on; a save that failed leaves the stored review behind
  // the screen, so the page says so, offers to send it again and holds the confirmation meanwhile.
  it('says the last save of the review failed, holds the confirmation and sends it again on Retry', async () => {
    const lastSave = { fileIds: ['file-1'], options: {}, decisions: {} };
    saveSessionState.isError = true;
    saveSessionState.variables = lastSave;
    show();
    await screen.findByDisplayValue('Pestera Ursilor');
    fireEvent.click(screen.getAllByRole('checkbox')[0]);

    expect(await screen.findByText(/latest changes to this review could not be saved/)).toBeInTheDocument();
    expect(screen.getByTestId('photo-import-commit')).toBeDisabled();
    fireEvent.click(screen.getByTestId('photo-import-commit'));
    expect(commitMutate).not.toHaveBeenCalled();

    saveSessionMutate.mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(saveSessionMutate).toHaveBeenCalledWith(lastSave);
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

  it('says a refused grouping failed, offers a retry and shows no places as current', async () => {
    previewFailure = new ApiError(400, 'photo_import.no_files');
    show();

    const alert = await screen.findByTestId('photo-import-preview-error');
    expect(alert.textContent).toContain(i18n.t('geodata.refusals.noFiles'));
    expect(screen.queryByDisplayValue('Pestera Ursilor')).toBeNull();
    expect(screen.getByTestId('photo-import-commit')).toBeDisabled();

    fireEvent.click(within(alert).getByRole('button'));
    expect(previewRefetch).toHaveBeenCalledTimes(1);
  });

  it('says why a confirmation was refused', async () => {
    commitMutate.mockReset();
    commitMutate.mockRejectedValue(new ApiError(403));
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    fireEvent.click(screen.getByTestId('photo-import-commit'));

    expect(await screen.findByText(i18n.t('geodata.refusals.forbidden'))).toBeInTheDocument();
  });

  it('names the picture that could not be uploaded, and why', async () => {
    uploadMutate.mockRejectedValue(new ApiError(413));
    show();
    await screen.findByDisplayValue('Pestera Ursilor');

    const input = screen.getByTestId('photo-import-dropzone').querySelector('input[type=file]')!;
    fireEvent.change(input, { target: { files: [new File(['x'], 'IMG_9999.jpg', { type: 'image/jpeg' })] } });

    expect(
      await screen.findByText(
        i18n.t('photoImport.uploadFailedNamed', {
          name: 'IMG_9999.jpg',
          reason: i18n.t('geodata.upload.errors.tooLarge'),
        }),
      ),
    ).toBeInTheDocument();
  });

  it('drops from the selection a place a new grouping no longer lets be confirmed', async () => {
    const view = show();
    await screen.findByDisplayValue('Pestera Ursilor');
    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    expect(screen.getByTestId('photo-import-commit').textContent).toContain('1');

    preview = { ...fullPreview, selectableKeys: [] };
    view.rerender(
      <MemoryRouter>
        <App>
          <PhotoImportWorkspacePage />
        </App>
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByTestId('photo-import-commit')).toBeDisabled());
  });

  it('asks for a grouping once the reviewer pauses, not on every step of an option', async () => {
    show();
    await screen.findByDisplayValue('Pestera Ursilor');
    fireEvent.click(screen.getByText(i18n.t('photoImport.optionsClock')));
    // Past the first settled request, so what follows is only the typing.
    await waitFor(() => expect(previewCalls.some((call) => call.enabled)).toBe(true));

    const offset = screen
      .getByText(i18n.t('photoImport.clockOffset'))
      .closest('.ant-form-item')!
      .querySelector('input')!;
    previewCalls.length = 0;
    for (const typed of ['6', '60', '600', '3600']) {
      fireEvent.change(offset, { target: { value: typed } });
    }
    // The witness that the pause was waited out: the request for the whole value is made. Every
    // step before it was drawn synchronously, so one asked for on the way would be here first.
    const asked = () =>
      new Set(previewCalls.filter((call) => call.enabled).map((call) => call.body.options.cameraClockOffsetSeconds));
    await waitFor(() => expect(asked().has(3600)).toBe(true));
    for (const partial of [6, 60, 600]) {
      expect(asked().has(partial)).toBe(false);
    }
  });
});
