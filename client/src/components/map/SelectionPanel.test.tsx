// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { useWorkspaceStore, type WorkspaceSelection } from '../../stores/workspaceStore.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera, type ViewCameraTarget } from '../../workspace/viewCamera.ts';
import { ApiError } from '../../api/client.ts';
import { noteVersionOfReading } from '../../api/readingVersion.ts';
import SelectionPanel from './SelectionPanel.tsx';

// What this panel does to the rest of the application, rather than what it renders.
//
// The same instance is mounted beside the flat map and beside the 3D scene, so every side effect
// it has must reach whichever view is on screen. Reaching for the flat map's own modules — its
// camera, its overlay's refetch — is the defect these tests exist to prevent: that map is a
// module-level object that exists whether or not it is mounted, so the command is accepted in
// silence, the button the viewer pressed appears to do nothing, and the write they made goes on
// being drawn in the view they are actually looking at.
//
// The other half is which entity the panel records a link against, which is decided from the
// selection and not from whatever the panel happens to have loaded.

const clusterEntrances = {
  features: [
    {
      geometry: { type: 'Point', coordinates: [25.104, 45.203] },
      properties: { id: 'entrance-a', caveId: 'cave-1', name: 'Gura Mare', approximate: false },
    },
  ],
};

const cave = {
  id: 'cave-1',
  name: 'Peștera de Test',
  caveTypeId: null,
  region: null,
  surveyedLength: null,
  depth: null,
  entranceCount: 2,
  visibility: 'public',
  approximateLocation: false,
};

const entrances = [
  { id: 'entrance-a', name: 'Gura Mare', approximate: false, geom: { type: 'Point', coordinates: [25.11, 45.21] } },
  { id: 'entrance-b', name: 'Intrarea de jos', approximate: false, geom: { type: 'Point', coordinates: [25.12, 45.22] } },
];

const featureGeometry = { type: 'LineString', coordinates: [[25.1, 45.2], [25.2, 45.3]] };

const aFeatureEnvelope = {
  kind: 'generic',
  feature: {
    id: 'feature-1',
    name: 'Fracture',
    featureTypeCode: 'fracture',
    geometry: featureGeometry,
    description: null,
    properties: {},
    parents: [],
    locationProtected: false,
    cavingGroupId: null,
    visibility: 'public',
    omittedLocation: false,
    approximateLocation: false,
  },
};

/** The feature as the panel's read of it answers at this moment; reassigned to stand for a re-read. */
let featureEnvelope: typeof aFeatureEnvelope = aFeatureEnvelope;

/** The feature as a read answered at `version` carried it. */
function aFeatureAt(version: string) {
  const envelope = { ...aFeatureEnvelope, feature: { ...aFeatureEnvelope.feature } };
  noteVersionOfReading(envelope, new Response(null, { headers: { ETag: version } }));
  return envelope;
}

const deleteFeature = vi.fn().mockResolvedValue(undefined);
const updateFeature = vi.fn().mockResolvedValue(undefined);

/** Reassigned per test: the panel draws a headline picture only when there is one to draw. */
let caveSummary: { headlinePicture: { thumbnailUrl: string; caption: string | null } | null } = {
  headlinePicture: null,
};

/** What a card's own read answered, when a test stands it in for a failure; undefined reads fine. */
let failedRead: { kind: 'cave' | 'cluster' | 'feature' | 'trip'; error: unknown } | undefined;
const refetchFailed = vi.fn();
function readOf<T>(kind: 'cave' | 'cluster' | 'feature' | 'trip', data: T) {
  return failedRead?.kind === kind
    ? { data: undefined, isPending: false, isError: true, error: failedRead.error, refetch: refetchFailed }
    : { data, isPending: false, isError: false, error: null, refetch: refetchFailed };
}

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => readOf('cave', cave),
  useTripLog: () => readOf('trip', undefined),
  useTripTypes: () => ({ data: [] }),
  useCaveSummary: () => ({ data: caveSummary }),
  useCaveTypes: () => ({ data: [] }),
  useClusterEntrances: () => readOf('cluster', clusterEntrances),
  useEntrances: () => ({ data: entrances }),
  useFeature: () => readOf('feature', featureEnvelope),
  useFeatureTypes: () => ({
    data: [{ id: 1, code: 'fracture', name: 'Fracture', propertiesSchema: null }],
  }),
  useGenericFeatureTypes: () => ({
    data: [{ id: 1, code: 'fracture', name: 'Fracture', propertiesSchema: null }],
  }),
  useCan: (_domain: string, action: string) => holdsInstallationWide(action),
  useDeleteFeature: () => ({ mutateAsync: deleteFeature, isPending: false }),
  useUpdateFeature: () => ({ mutateAsync: updateFeature, isPending: false }),
  // The panel's arrangement now merges an installation default over the person's own; with
  // nothing published, both sides are empty and the built-in order applies.
  useUiDefaults: () => ({ data: undefined }),
  useFeatures: () => ({ data: { items: [] } }),
}));

// What the caller may do: installation-wide, and on the one selected object. Reassigned per test;
// the default holds both, which is what the tests about other things assume.
let installationWide: boolean | string[] = true;
const holdsInstallationWide = (action: string) =>
  installationWide === true || (Array.isArray(installationWide) && installationWide.includes(action));
let objectActions: string | undefined = 'read, write, delete, share';
/** What the feature's own answer failed with, when it failed. */
let objectError: unknown;
vi.mock('../../api/hooks/access.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/access.ts')>('../../api/hooks/access.ts');
  return {
    ...actual,
    useCan: (_domain: string, action: string) => holdsInstallationWide(action),
    useEffectiveAccess: () => ({
      data: objectActions === undefined ? undefined : { actions: objectActions },
      error: objectError,
    }),
  };
});

// These pull in stacks of their own and none of them is what these tests are about. The panel
// composes its sections from a registry now, so the sections it can draw are mocked at their own
// modules rather than by mocking every hook they happen to call.
vi.mock('../history/HistoryPanel.tsx', () => ({ default: () => null }));
// The edit dialog is drawn only as the one act these tests need of it: handing its values back.
vi.mock('../features/FeatureEditModal.tsx', () => ({
  default: (props: { open: boolean; onSubmit: (values: Record<string, unknown>) => void }) =>
    props.open ? (
      <button
        type="button"
        data-testid="feature-edit-submit"
        onClick={() =>
          props.onSubmit({
            name: 'Fracture',
            featureTypeId: 1,
            description: null,
            properties: {},
            locationProtected: false,
            visibility: 'public',
          })
        }
      />
    ) : null,
}));

// The newest version the transport has filed for a resource at this moment. It can be ahead of the
// copy the panel holds: a save, or a read made elsewhere, files one before the panel's re-read lands.
const versionOnFile = vi.fn<(path: string) => string | undefined>(() => undefined);
vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  lastReadETag: (path: string) => versionOnFile(path),
}));
vi.mock('../tags/TagChips.tsx', () => ({
  default: (props: { canEdit: boolean }) => <div data-testid="tag-chips" data-can-edit={String(props.canEdit)} />,
}));
vi.mock('../attachments/AttachmentSection.tsx', () => ({
  default: (props: { canEdit: boolean }) => (
    <div data-testid="attachment-section" data-can-edit={String(props.canEdit)} />
  ),
}));

/** The links panel is mounted, not rendered here: what it is mounted *for* is the point. */
const linksMounted = vi.fn();
vi.mock('../reslinks/LinksSection.tsx', () => ({
  default: (props: { entityType: string; entityId: string; entityTitle?: string | null }) => {
    linksMounted(props);
    return <div data-testid="links-section" />;
  },
}));

function renderPanel(selection: WorkspaceSelection) {
  useWorkspaceStore.getState().setSelection(selection);
  return render(
    <MemoryRouter>
      <App>
        <SelectionPanel />
      </App>
    </MemoryRouter>,
  );
}

function recorder() {
  return { flyTo: vi.fn(), fitGeometry: vi.fn() } satisfies ViewCameraTarget;
}

let camera: ReturnType<typeof recorder>;
let detachCamera: () => void;

beforeEach(() => {
  camera = recorder();
  detachCamera = setActiveViewCamera(camera);
  deleteFeature.mockClear();
  updateFeature.mockReset().mockResolvedValue(undefined);
  versionOnFile.mockReset().mockReturnValue(undefined);
  featureEnvelope = aFeatureEnvelope;
  linksMounted.mockReset();
  caveSummary = { headlinePicture: null };
  installationWide = true;
  objectActions = 'read, write, delete, share';
  objectError = undefined;
  failedRead = undefined;
  refetchFailed.mockReset();
});

afterEach(() => {
  detachCamera();
  cleanup();
  useWorkspaceStore.getState().setSelection(null);
});

describe('the shared detail panel', () => {
  it('moves the camera of the view on screen when a cluster asks to be zoomed into', () => {
    renderPanel({ kind: 'cluster', lon: 25.1, lat: 45.2, count: 7, zoom: 8, cell: [24.9, 45.3, 25.6, 45.7] });

    fireEvent.click(screen.getByRole('button', { name: /Zoom here/ }));

    // Two levels in from the zoom the cell was summed at, which is what opens it.
    expect(camera.flyTo).toHaveBeenCalledWith(25.1, 45.2, 10);
  });

  it('moves it to a cluster member the viewer picks out of the list', () => {
    renderPanel({ kind: 'cluster', lon: 25.1, lat: 45.2, count: 7, zoom: 8, cell: [24.9, 45.3, 25.6, 45.7] });

    fireEvent.click(screen.getByRole('button', { name: /Gura Mare/ }));

    expect(useWorkspaceStore.getState().selection).toEqual({
      kind: 'entrance',
      entranceId: 'entrance-a',
      caveId: 'cave-1',
    });
    expect(camera.flyTo).toHaveBeenCalledWith(25.104, 45.203, 15);
  });

  it('shows the cave its headline picture', async () => {
    caveSummary = {
      headlinePicture: { thumbnailUrl: '/api/v1/files/f/thumbnail?size=480&token=x', caption: 'The entrance' },
    };
    renderPanel({ kind: 'cave', caveId: 'cave-1' });

    const picture = (await screen.findByAltText('The entrance')) as HTMLImageElement;
    // A rendering, which is the whole of what the summary hands over: the URL carries a token
    // that opens derivatives and not the stored bytes.
    expect(picture.getAttribute('src')).toContain('/thumbnail?');
  });

  it('shows no picture for a cave whose headline this reader may not see', async () => {
    // Withheld and absent read the same on the wire, deliberately — so the panel must draw
    // nothing rather than a broken frame where a picture would be.
    renderPanel({ kind: 'cave', caveId: 'cave-1' });

    await screen.findByText('Peștera de Test');
    expect(screen.queryByAltText('The entrance')).toBeNull();
  });

  it('writes a cave\'s length and depth in the page\'s language, as the scale beside them is', async () => {
    const { default: i18n } = await import('../../i18n');
    Object.assign(cave, { surveyedLength: 12345.5, depth: 87.2 });
    await i18n.changeLanguage('ro');
    try {
      renderPanel({ kind: 'cave', caveId: 'cave-1' });

      expect(await screen.findByText('12.345,5')).toBeInTheDocument();
      expect(screen.getByText('87,2')).toBeInTheDocument();
    } finally {
      await i18n.changeLanguage('en');
      Object.assign(cave, { surveyedLength: null, depth: null });
    }
  });

  it('writes a snapped entrance position no finer than the grid it was snapped to', async () => {
    entrances[0] = { ...entrances[0], approximateLocation: true } as (typeof entrances)[number];
    try {
      renderPanel({ kind: 'entrance', entranceId: 'entrance-a', caveId: 'cave-1' });

      // To the hundredth of a degree, not the five places a surveyed position gets.
      expect(await screen.findByText('45.21°N 25.11°E')).toBeInTheDocument();
    } finally {
      entrances[0] = { ...entrances[0], approximateLocation: false } as (typeof entrances)[number];
    }
  });

  it('moves it to a cave entrance', async () => {
    renderPanel({ kind: 'entrance', entranceId: 'entrance-a', caveId: 'cave-1' });

    fireEvent.click(await screen.findByRole('button', { name: /Zoom to/ }));

    expect(camera.flyTo).toHaveBeenCalledWith(25.11, 45.21, 16);
  });

  it.each([
    ['refused for want of the right', new ApiError(403, 'forbidden'), 'You are not allowed to make that change.'],
    ['cut off by a lost connection', new TypeError('Failed to fetch'), 'The deletion failed. Please try again.'],
  ])('names a delete %s and keeps the feature selected and drawn', async (_label, failure, words) => {
    deleteFeature.mockRejectedValueOnce(failure);
    const heard = vi.fn();
    const stopListening = onSurfaceFeaturesChanged(heard);
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    fireEvent.click(screen.getByRole('button', { name: /Delete/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(words)).toBeTruthy();
    expect(heard).not.toHaveBeenCalled();
    expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'feature', featureId: 'feature-1' });
    expect(screen.queryByText('Deleted.')).toBeNull();
    stopListening();
  });

  it('frames a feature geometry in it rather than in a map that may not be showing', () => {
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    fireEvent.click(screen.getByRole('button', { name: /Zoom to/ }));

    expect(camera.fitGeometry).toHaveBeenCalledWith(featureGeometry);
  });

  it('announces a delete so every view drawing that feature stops drawing it', async () => {
    const heard = vi.fn();
    const stopListening = onSurfaceFeaturesChanged(heard);
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    fireEvent.click(screen.getByRole('button', { name: /Delete/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteFeature).toHaveBeenCalledWith('feature-1'));
    // Not "the map's overlay refetched": a view the writer never heard of has to hear about it,
    // or a deleted feature stays on screen and stays clickable.
    await waitFor(() => expect(heard).toHaveBeenCalledTimes(1));
    expect(useWorkspaceStore.getState().selection).toBeNull();
    stopListening();
  });

  it('links the entrance that was clicked, not the cave it belongs to', () => {
    renderPanel({ kind: 'entrance', entranceId: 'entrance-b', caveId: 'cave-1' });

    // An entrance is a feature in its own right: a link recorded from here has to name it,
    // or every entrance of a cave would silently record links against the same entity.
    expect(linksMounted).toHaveBeenCalledWith(
      expect.objectContaining({
        entityType: 'feature',
        entityId: 'entrance-b',
        entityTitle: 'Intrarea de jos',
      }),
    );
  });

  it('links the cave itself when the cave, and not one of its entrances, is selected', () => {
    renderPanel({ kind: 'cave', caveId: 'cave-1' });

    expect(linksMounted).toHaveBeenCalledWith(
      expect.objectContaining({ entityId: 'cave-1', entityTitle: 'Peștera de Test' }),
    );
    expect(screen.getByTestId('links-section')).toBeInTheDocument();
  });
});

/**
 * A right can be granted or withheld on one object, so what the panel offers for a selected
 * feature follows the feature's own answer, not the installation-wide one.
 */
describe('the panel with nothing selected', () => {
  it('points at the map beside the map, and at the scene beside the 3D scene', () => {
    useWorkspaceStore.getState().setSelection(null);
    render(
      <MemoryRouter>
        <App>
          <SelectionPanel />
        </App>
      </MemoryRouter>,
    );
    expect(screen.getByText('Click a feature on the map to see details.')).toBeInTheDocument();
    cleanup();

    render(
      <MemoryRouter>
        <App>
          <SelectionPanel inScene />
        </App>
      </MemoryRouter>,
    );
    expect(screen.getByText('Click a feature in the scene to see details.')).toBeInTheDocument();
    expect(screen.queryByText('Click a feature on the map to see details.')).toBeNull();
  });
});

describe('the edit affordances of a selected feature', () => {
  const editAffordances = () => ({
    edit: screen.queryByRole('button', { name: 'Edit' }) !== null,
    delete: screen.queryByRole('button', { name: 'Delete' }) !== null,
    tags: screen.getByTestId('tag-chips').getAttribute('data-can-edit'),
    attachments: screen.queryByTestId('attachment-section')?.getAttribute('data-can-edit') ?? 'closed',
  });

  it('offers them for a feature the member was given, with no installation-wide right', async () => {
    installationWide = false;
    objectActions = 'read, write, delete';
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: true, delete: true, tags: 'true' });
  });

  it('withholds them for a feature held back, whatever the installation-wide right', async () => {
    installationWide = true;
    objectActions = 'read';
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: false, delete: false, tags: 'false' });
  });

  it('offers editing without deleting where only writing was granted', async () => {
    objectActions = 'read, write';
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: true, delete: false });
  });

  it('stands in with the installation-wide right until the feature has answered, unusable meanwhile', async () => {
    installationWide = true;
    objectActions = undefined;
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: true, delete: true, tags: 'false' });
    expect(screen.getByRole('button', { name: 'Edit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
  });

  it('stands in for Delete with the installation-wide right to delete, not the right to write', async () => {
    installationWide = ['write'];
    objectActions = undefined;
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: true, delete: false });
  });

  it('takes a refused answer about the feature as no rights on it, not as no answer', async () => {
    installationWide = true;
    objectActions = undefined;
    objectError = new ApiError(403, 'forbidden');
    renderPanel({ kind: 'feature', featureId: 'feature-1' });

    await screen.findByText('Fracture', { selector: 'h5' });
    expect(editAffordances()).toMatchObject({ edit: false, delete: false, tags: 'false' });
  });
});

/**
 * The edit dialog is filled when it opens, while the panel behind it goes on reading the feature —
 * a return to the browser tab is a read — and every read files a newer version. A save carrying
 * that one would present somebody else's rename as already seen and write the older values over it.
 */
describe('editing a feature from a dialog opened before it was read again', () => {
  const path = '/api/v1/features/feature-1';

  it('saves against the version the dialog opened with', async () => {
    versionOnFile.mockImplementation((asked) => (asked === path ? '"1"' : undefined));
    featureEnvelope = aFeatureAt('"1"');
    const view = renderPanel({ kind: 'feature', featureId: 'feature-1' });
    await screen.findByText('Fracture', { selector: 'h5' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit' })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    versionOnFile.mockImplementation((asked) => (asked === path ? '"2"' : undefined));
    featureEnvelope = aFeatureAt('"2"');
    view.rerender(
      <MemoryRouter>
        <App>
          <SelectionPanel />
        </App>
      </MemoryRouter>,
    );
    fireEvent.click(await screen.findByTestId('feature-edit-submit'));

    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(1));
    expect(updateFeature.mock.calls[0][0]).toMatchObject({ id: 'feature-1', ifMatch: '"1"' });
  });

  // The panel can hold an older copy than the newest version filed: re-clicking a feature on the
  // map reads it outside the panel's cache, and a save files its version before its re-read lands.
  // The dialog is filled from the copy the panel holds, so the save names that copy's version.
  it('saves under the version of the copy it was filled from, though a newer one is on file', async () => {
    featureEnvelope = aFeatureAt('"1"');
    versionOnFile.mockImplementation((asked) => (asked === path ? '"2"' : undefined));
    renderPanel({ kind: 'feature', featureId: 'feature-1' });
    await screen.findByText('Fracture', { selector: 'h5' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit' })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.click(await screen.findByTestId('feature-edit-submit'));

    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(1));
    expect(updateFeature.mock.calls[0][0]).toMatchObject({ id: 'feature-1', ifMatch: '"1"' });
  });

  it('leaves a stale-version refusal to the one conflict warning', async () => {
    updateFeature.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    renderPanel({ kind: 'feature', featureId: 'feature-1' });
    await screen.findByText('Fracture', { selector: 'h5' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Edit' })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.click(await screen.findByTestId('feature-edit-submit'));

    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(1));
    // A second press refused for another reason draws its own sentence. Messages are drawn in the
    // order they were asked for, so once that one is on screen anything the stale refusal had asked
    // for would be too — no wait on the clock is needed to see that it asked for nothing.
    updateFeature.mockRejectedValue(new ApiError(403, 'forbidden'));
    fireEvent.click(screen.getByTestId('feature-edit-submit'));
    expect(await screen.findByText('You are not allowed to make that change.')).toBeTruthy();
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
    expect(screen.queryByText(/Someone else changed this since you loaded it/)).toBeNull();
    // Still open, holding what was typed.
    expect(screen.getByTestId('feature-edit-submit')).toBeTruthy();
  });
});

describe('a card whose own read failed', () => {
  const cluster = { kind: 'cluster', lon: 25.1, lat: 45.2, count: 7, zoom: 8, cell: [24.9, 45.3, 25.6, 45.7] } as const;

  it('says the cluster could not be read, rather than calling it a cluster of nobody', () => {
    failedRead = { kind: 'cluster', error: new TypeError('Failed to fetch') };
    renderPanel(cluster);

    expect(screen.queryByText('0 entrances in this area')).toBeNull();
    expect(screen.getByText('The entrances in this area could not be read.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetchFailed).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['cave', { kind: 'cave', caveId: 'cave-1' }, 'cave-selection-load-failed'],
    ['trip', { kind: 'trip', tripId: 'trip-1' }, 'trip-selection-load-failed'],
    ['feature', { kind: 'feature', featureId: 'feature-1' }, 'feature-selection-load-failed'],
  ] as const)('stops spinning when the %s read fails, and offers to read it again', (kind, selection, testId) => {
    failedRead = { kind, error: new ApiError(503) };
    renderPanel(selection as WorkspaceSelection);

    expect(screen.getByTestId(testId)).toBeTruthy();
    expect(screen.getByText('What was picked on the map could not be read.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });

  it('words a refusal as gone or not yours, with no Retry to be refused again', () => {
    failedRead = { kind: 'cave', error: new ApiError(404, 'not_found') };
    renderPanel({ kind: 'cave', caveId: 'cave-1' });

    expect(screen.getByText('This is no longer there, or is not yours to read.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
  });
});
