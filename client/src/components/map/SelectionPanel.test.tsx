// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { useWorkspaceStore, type WorkspaceSelection } from '../../stores/workspaceStore.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera, type ViewCameraTarget } from '../../workspace/viewCamera.ts';
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

const featureEnvelope = {
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

const deleteFeature = vi.fn().mockResolvedValue(undefined);
const updateFeature = vi.fn().mockResolvedValue(undefined);

/** Reassigned per test: the panel draws a headline picture only when there is one to draw. */
let caveSummary: { headlinePicture: { thumbnailUrl: string; caption: string | null } | null } = {
  headlinePicture: null,
};

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => ({ data: cave, isPending: false }),
  useCaveSummary: () => ({ data: caveSummary }),
  useCaveTypes: () => ({ data: [] }),
  useClusterEntrances: () => ({ data: clusterEntrances, isPending: false }),
  useEntrances: () => ({ data: entrances }),
  useFeature: () => ({ data: featureEnvelope, isPending: false }),
  useFeatureTypes: () => ({
    data: [{ id: 1, code: 'fracture', name: 'Fracture', propertiesSchema: null }],
  }),
  useCan: () => true,
  useDeleteFeature: () => ({ mutateAsync: deleteFeature, isPending: false }),
  useUpdateFeature: () => ({ mutateAsync: updateFeature, isPending: false }),
  // The panel's arrangement now merges an installation default over the person's own; with
  // nothing published, both sides are empty and the built-in order applies.
  useUiDefaults: () => ({ data: undefined }),
  useFeatures: () => ({ data: { items: [] } }),
}));

// These pull in stacks of their own and none of them is what these tests are about. The panel
// composes its sections from a registry now, so the sections it can draw are mocked at their own
// modules rather than by mocking every hook they happen to call.
vi.mock('../history/HistoryPanel.tsx', () => ({ default: () => null }));
vi.mock('../features/FeatureEditModal.tsx', () => ({ default: () => null }));
vi.mock('../tags/TagChips.tsx', () => ({ default: () => <div data-testid="tag-chips" /> }));
vi.mock('../attachments/AttachmentSection.tsx', () => ({
  default: () => <div data-testid="attachment-section" />,
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
  linksMounted.mockReset();
  caveSummary = { headlinePicture: null };
});

afterEach(() => {
  detachCamera();
  cleanup();
  useWorkspaceStore.getState().setSelection(null);
});

describe('the shared detail panel', () => {
  it('moves the camera of the view on screen when a cluster asks to be zoomed into', () => {
    renderPanel({ kind: 'cluster', lon: 25.1, lat: 45.2, count: 7, zoom: 8 });

    fireEvent.click(screen.getByRole('button', { name: /Zoom here/ }));

    // Two levels in from the zoom the cell was summed at, which is what opens it.
    expect(camera.flyTo).toHaveBeenCalledWith(25.1, 45.2, 10);
  });

  it('moves it to a cluster member the viewer picks out of the list', () => {
    renderPanel({ kind: 'cluster', lon: 25.1, lat: 45.2, count: 7, zoom: 8 });

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

  it('moves it to a cave entrance', async () => {
    renderPanel({ kind: 'entrance', entranceId: 'entrance-a', caveId: 'cave-1' });

    fireEvent.click(await screen.findByRole('button', { name: /Zoom to/ }));

    expect(camera.flyTo).toHaveBeenCalledWith(25.11, 45.21, 16);
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
