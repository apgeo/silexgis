// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { WorkspaceSelection } from '../../stores/workspaceStore.ts';

const cave = {
  id: 'cave-1',
  name: 'Peștera Demo Mare',
  caveTypeId: null,
  region: null,
  surveyedLength: null,
  depth: null,
  entranceCount: 2,
  visibility: 'public',
  approximateLocation: false,
};

const entrances = [
  { id: 'entrance-1', name: 'Intrarea de sus', approximate: false, geom: { coordinates: [25.6, 45.65] } },
  { id: 'entrance-2', name: 'Intrarea de jos', approximate: false, geom: { coordinates: [25.61, 45.66] } },
];

let selection: WorkspaceSelection | null = null;

vi.mock('../../stores/workspaceStore.ts', () => ({
  useWorkspaceStore: (select: (state: unknown) => unknown) =>
    select({ selection, setSelection: vi.fn() }),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => ({ data: cave, isPending: false }),
  useEntrances: () => ({ data: entrances }),
  useCaveTypes: () => ({ data: [] }),
  useClusterEntrances: () => ({ data: [] }),
  useFeature: () => ({ data: undefined, isPending: true }),
  useFeatureTypes: () => ({ data: [] }),
  useDeleteFeature: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateFeature: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCan: () => false,
}));

// The map modules reach for a live OpenLayers map; the panel's own behaviour is what is
// under test, so they are stubbed at their boundary.
vi.mock('../../map/mapContext.ts', () => ({ flyTo: vi.fn(), fitGeoJsonGeometry: vi.fn() }));
vi.mock('../../map/featureLayer.ts', () => ({ reloadSurfaceFeatures: vi.fn() }));
vi.mock('../../map/mapFilters.ts', () => ({ getMapTagFilter: () => null }));

/** The links panel is mounted, not rendered here: what it is mounted *for* is the point. */
const mounted = vi.fn();
vi.mock('../reslinks/LinksSection.tsx', () => ({
  default: (props: { entityType: string; entityId: string; entityTitle?: string | null }) => {
    mounted(props);
    return <div data-testid="links-section" />;
  },
}));

const { default: SelectionPanel } = await import('./SelectionPanel.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <SelectionPanel />
      </App>
    </MemoryRouter>,
  );
}

describe('SelectionPanel', () => {
  beforeEach(() => {
    mounted.mockReset();
  });

  afterEach(cleanup);

  it('links the entrance that was clicked, not the cave it belongs to', () => {
    selection = { kind: 'entrance', entranceId: 'entrance-2', caveId: 'cave-1' };
    show();

    // An entrance is a feature in its own right: a link recorded from here has to name it,
    // or every entrance of a cave would silently record links against the same entity.
    expect(mounted).toHaveBeenCalledWith(
      expect.objectContaining({
        entityType: 'feature',
        entityId: 'entrance-2',
        entityTitle: 'Intrarea de jos',
      }),
    );
  });

  it('links the cave itself when the cave, and not one of its entrances, is selected', () => {
    selection = { kind: 'cave', caveId: 'cave-1' };
    show();

    expect(mounted).toHaveBeenCalledWith(
      expect.objectContaining({ entityId: 'cave-1', entityTitle: 'Peștera Demo Mare' }),
    );
    expect(screen.getByTestId('links-section')).toBeInTheDocument();
  });
});
