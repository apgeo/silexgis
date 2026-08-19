// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { MapLayerInfo } from '../../api/hooks.ts';
import type { Scene3DSurfaceState } from '../../scene3d/scene3dEngine.ts';
import { EMPTY_SURVEY_MESH_3D_STATE } from '../../scene3d/surveyMesh3d.ts';
import Scene3DLayerPanel, { type Scene3DLayerPanelProps } from './Scene3DLayerPanel.tsx';

const layers = [
  {
    id: 1,
    name: 'OpenStreetMap',
    layerKind: 'xyz',
    urlTemplate: 'https://tile.example/{z}/{x}/{y}.png',
    attribution: null,
    isBase: true,
    isDefault: true,
    sortOrder: 0,
    options: null,
  },
] as unknown as MapLayerInfo[];

const drawingTheCutaway: Scene3DSurfaceState = {
  requested: 'cutaway',
  effective: 'cutaway',
  cutawayAvailable: true,
  hasFootprint: true,
};

function renderPanel(overrides: Partial<Scene3DLayerPanelProps> = {}) {
  const props: Scene3DLayerPanelProps = {
    layers,
    activeBaseId: 1,
    onBaseChange: vi.fn(),
    baseOpacity: {},
    onBaseOpacityChange: vi.fn(),
    overlayVisible: {},
    onOverlayVisibleChange: vi.fn(),
    overlayOpacity: {},
    onOverlayOpacityChange: vi.fn(),
    meshVisible: true,
    onMeshVisibleChange: vi.fn(),
    meshState: EMPTY_SURVEY_MESH_3D_STATE,
    surfaceMode: 'overlay',
    onSurfaceModeChange: vi.fn(),
    surfaceState: {
      requested: 'overlay',
      effective: 'overlay',
      cutawayAvailable: true,
      hasFootprint: true,
    },
    ...overrides,
  };
  render(<Scene3DLayerPanel {...props} />);
  return props;
}

afterEach(cleanup);

describe('Scene3DLayerPanel', () => {
  it('names the scene layers the way the flat map names the same things', () => {
    renderPanel();

    // Same words in both views, from the same keys: two names for one layer would read as two
    // different layers.
    expect(screen.getByRole('checkbox', { name: 'Cave centerlines' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Cave entrances' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Surface features' })).toBeInTheDocument();
  });

  it('shows every layer until something says otherwise', () => {
    renderPanel({ overlayVisible: { centerlines: false } });

    expect(screen.getByRole('checkbox', { name: 'Cave centerlines' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Cave entrances' })).toBeChecked();
  });

  it('reports a layer being turned off by the name the scene knows it by', () => {
    const props = renderPanel();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Cave centerlines' }));

    expect(props.onOverlayVisibleChange).toHaveBeenCalledWith('centerlines', false);
  });

  it('shows the fade of a hidden layer as unusable rather than as something that still acts', () => {
    renderPanel({ overlayVisible: { entrances: false } });

    const handle = screen.getByRole('slider', { name: 'Opacity of Cave entrances' });
    expect(handle).toHaveAttribute('aria-disabled', 'true');
  });

  it('offers the basemap its own fade, which is about reading it and not about seeing through it', () => {
    renderPanel({ baseOpacity: { 1: 0.4 } });

    expect(screen.getByText('Base layers')).toBeInTheDocument();
    expect(
      screen.getByRole('slider', { name: 'Opacity of OpenStreetMap' }),
    ).toHaveAttribute('aria-valuenow', '40');
    expect(screen.getByRole('radio', { name: 'OpenStreetMap' })).toBeChecked();
  });

  it('explains that the overlay carries depth in colour rather than in what hides what', () => {
    renderPanel();

    expect(screen.getByText(/colour of the line/)).toBeInTheDocument();
  });

  it('gives the survey colours a key, without which the ramp says nothing', () => {
    renderPanel();

    const legend = screen.getByTestId('scene3d-depth-legend');
    // One entry per band, labelled with the depth it starts at.
    expect(legend.textContent).toBe('0 m50 m150 m300 m600 m');
    expect(screen.getByText('Depth below the top of the cave')).toBeInTheDocument();
  });

  it('drops the depth key with the layer it is about', () => {
    renderPanel({ overlayVisible: { centerlines: false } });

    expect(screen.queryByTestId('scene3d-depth-legend')).not.toBeInTheDocument();
  });

  it('tells a viewer whose angle took the cutaway away how to get it back', () => {
    renderPanel({
      surfaceMode: 'cutaway',
      surfaceState: { ...drawingTheCutaway, effective: 'overlay', pausedBy: 'angle' },
    });

    expect(screen.getByText(/tilt the view down towards the cave/)).toBeInTheDocument();
  });

  it('tells a viewer under the ground the one thing that works from there', () => {
    // Tilting is useless below the surface: there is no ground left between the camera and the
    // cave to remove, so no angle brings the opening back and only climbing out does.
    renderPanel({
      surfaceMode: 'cutaway',
      surfaceState: { ...drawingTheCutaway, effective: 'overlay', pausedBy: 'belowSurface' },
    });

    expect(screen.getByText(/Rise back above it/)).toBeInTheDocument();
    expect(screen.queryByText(/tilt the view down towards the cave/)).not.toBeInTheDocument();
  });

  it('still drives the scene when there is no basemap catalog to name', () => {
    // The catalog is one section of this panel; the layers, the fades and the surface mode are
    // about the scene. An installation whose catalog is unreadable must not lose them with it.
    renderPanel({ layers: [] });

    expect(screen.queryByText('Base layers')).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Cave centerlines' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Cut it away' })).toBeInTheDocument();
  });

  it('refuses the cutaway on a browser that cannot draw one, and says which', () => {
    renderPanel({
      surfaceState: { ...drawingTheCutaway, requested: 'overlay', cutawayAvailable: false },
    });

    // Offered but disabled: a viewer who cannot have it is better told why than left hunting for
    // a control that is not there.
    expect(screen.getByRole('radio', { name: 'Cut it away' })).toBeDisabled();
  });

  it('says when there is no survey to cut around', () => {
    renderPanel({
      surfaceMode: 'cutaway',
      surfaceState: { ...drawingTheCutaway, hasFootprint: false },
    });

    expect(screen.getByText(/No survey is loaded to cut around/)).toBeInTheDocument();
  });

  it('offers the walls of the selected cave as their own switch, with no fade beside them', () => {
    renderPanel();

    expect(
      screen.getByRole('checkbox', { name: 'Walls of the selected cave' }),
    ).toBeInTheDocument();
    // A fade over a mesh that cannot be faded would be a control wired to nothing, so there is
    // one slider per layer that has an opacity and none for this one.
    expect(
      screen.queryByRole('slider', { name: 'Opacity of Walls of the selected cave' }),
    ).not.toBeInTheDocument();
  });

  it('reports the walls being turned off, which is what releases their graphics memory', () => {
    const props = renderPanel();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Walls of the selected cave' }));

    expect(props.onMeshVisibleChange).toHaveBeenCalledWith(false);
  });

  it('says how big a mesh is while it is still arriving, not only once it is there', () => {
    // The ordinary export is a few hundred kilobytes and the case this was built for is fifty
    // megabytes; a viewer waiting on the second one is owed the difference before it lands.
    renderPanel({
      meshState: {
        status: 'loading',
        name: 'Coiba Mare',
        triangleCount: 1234567,
        precisionLost: false,
      },
    });

    expect(screen.getByTestId('scene3d-mesh-status').textContent).toMatch(/1,234,567 triangles/);
  });

  it('tells a viewer to pick a cave rather than leaving the switch explaining nothing', () => {
    renderPanel({ meshState: EMPTY_SURVEY_MESH_3D_STATE });

    expect(screen.getByTestId('scene3d-mesh-status').textContent).toMatch(/Pick a cave/);
  });

  it('says a conversion has not finished instead of claiming the cave has no walls', () => {
    renderPanel({ meshState: { status: 'converting', name: 'Coiba Mare', precisionLost: false } });

    const said = screen.getByTestId('scene3d-mesh-status').textContent ?? '';
    expect(said).toMatch(/still being converted/);
    // The two states are genuinely different answers and must not read as the same one.
    expect(said).not.toMatch(/No wall model/);
  });

  it("gives the server's own reason when a conversion failed, rather than a blank refusal", () => {
    renderPanel({
      meshState: {
        status: 'failed',
        name: 'Coiba Mare',
        precisionLost: false,
        message: 'This is not a binary STL.',
      },
    });

    expect(screen.getByTestId('scene3d-mesh-status').textContent).toMatch(
      /This is not a binary STL\./,
    );
  });

  it('warns that a drawn mesh came from a file that had already lost detail', () => {
    renderPanel({
      meshState: { status: 'drawn', name: 'Coiba Mare', triangleCount: 12, precisionLost: true },
    });

    expect(screen.getByTestId('scene3d-mesh-precision').textContent).toMatch(/local origin/);
  });
});
