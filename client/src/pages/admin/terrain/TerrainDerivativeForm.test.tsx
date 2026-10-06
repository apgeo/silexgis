// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import type { TerrainBuild, TerrainDerivativeCreate } from '../../../api/hooks.ts';

const requestMutate = vi.fn();

function build(overrides: Partial<TerrainBuild>): TerrainBuild {
  return {
    id: 'b1',
    extent: { type: 'Polygon', coordinates: [[[25, 45.5], [25.5, 45.5], [25.5, 46], [25, 46], [25, 45.5]]] },
    requestedMaxDepth: 13,
    status: 'succeeded',
    phase: 'publish',
    progress: 100,
    message: null,
    errorCode: null,
    sizeBytes: 48 * 1024 * 1024,
    pyramidVersion: '1.2.3',
    heightDatum: 'orthometric',
    geoidHeightM: 0,
    surveyHeightOffsetM: 0,
    isActive: false,
    createdAt: '2026-08-17T06:00:00Z',
    startedAt: '2026-08-17T06:00:00Z',
    finishedAt: '2026-08-17T06:00:12Z',
    updatedAt: '2026-08-17T06:00:12Z',
    ...overrides,
  } as TerrainBuild;
}

let builds: TerrainBuild[] = [];

vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>(
    '../../../api/hooks.ts',
  );
  return {
    terrainBuildUnsettled: actual.terrainBuildUnsettled,
    terrainListPollInterval: actual.terrainListPollInterval,
    useTerrainBuilds: () => ({
      data: { items: builds, page: 1, pageSize: 100, totalItems: builds.length },
      isLoading: false,
    }),
    useRequestTerrainDerivative: () => ({ mutateAsync: requestMutate, isPending: false }),
  };
});

const { default: TerrainDerivativeForm } = await import('./TerrainDerivativeForm.tsx');

function show(canExecute = true) {
  return render(
    <App>
      <TerrainDerivativeForm canExecute={canExecute} />
    </App>,
  );
}

/** Picks a kind of picture the way a pointer would: open the list, click the entry. */
async function chooseKind(label: string) {
  const picker = within(screen.getByTestId('terrain-derivative-kind')).getByRole('combobox');
  await act(async () => {
    fireEvent.mouseDown(picker);
  });
  await act(async () => {
    fireEvent.click(await screen.findByTitle(label));
  });
}

function sent(): TerrainDerivativeCreate {
  expect(requestMutate).toHaveBeenCalledTimes(1);
  return requestMutate.mock.calls[0][0] as TerrainDerivativeCreate;
}

beforeEach(() => {
  requestMutate.mockReset().mockResolvedValue({});
  builds = [build({ id: 'older' }), build({ id: 'drawn', isActive: true })];
});

afterEach(cleanup);

describe('TerrainDerivativeForm', () => {
  it('opens on the build the scene is drawing and asks for a hillshade in the server’s own defaults', async () => {
    show();

    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    await waitFor(() => expect(requestMutate).toHaveBeenCalledTimes(1));
    expect(sent()).toEqual({
      terrainBuildId: 'drawn',
      derivative: 'hillshade',
      name: 'Shaded relief',
      lighting: 'single',
      azimuthDegrees: 315,
      altitudeDegrees: 45,
      zFactor: 1,
      surfaceFit: 'horn',
      slopeUnit: null,
      ruggednessFit: null,
      computeEdges: true,
      contourIntervalMetres: null,
      colourRamp: null,
    });
    expect(await screen.findByText(/has been queued/)).toBeInTheDocument();
  });

  it('offers only builds that ran to the end, and nothing at all when none did', async () => {
    builds = [
      build({ id: 'running', status: 'running', phase: 'bake', pyramidVersion: null }),
      build({ id: 'stopped', status: 'failed', pyramidVersion: null }),
    ];
    show();

    expect(screen.getByTestId('terrain-derivative-no-builds')).toBeInTheDocument();
    expect(screen.getByTestId('terrain-derivative-submit')).toBeDisabled();
  });

  // A slope is sent with only what a slope reads, because what it does not read the server strips
  // before deciding whether it already has it. And it is offered under its plain name, with
  // nothing on the form warning that its numbers are not measurements: they are.
  it('offers a slope without a caveat, and sends only what a slope reads', async () => {
    show();

    await chooseKind('Steepness');

    expect(screen.queryByTestId('terrain-derivative-latitude-caveat')).not.toBeInTheDocument();
    expect(screen.queryByText(/approximate/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Light from (°)')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    await waitFor(() => expect(requestMutate).toHaveBeenCalledTimes(1));
    expect(sent()).toMatchObject({
      derivative: 'slope',
      name: 'Steepness',
      lighting: null,
      azimuthDegrees: null,
      altitudeDegrees: null,
      zFactor: null,
      surfaceFit: 'horn',
      slopeUnit: 'degrees',
      ruggednessFit: null,
      colourRamp: null,
    });
  });

  it('shows each kind the settings it reads and no others', async () => {
    show();

    await chooseKind('Facing');
    expect(screen.queryByTestId('terrain-derivative-latitude-caveat')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Slope arithmetic')).toBeInTheDocument();
    expect(screen.queryByLabelText('Steepness as')).not.toBeInTheDocument();

    await chooseKind('Ruggedness');
    expect(screen.queryByLabelText('Slope arithmetic')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Ruggedness definition')).toBeInTheDocument();
  });

  it('keeps a name somebody typed when the kind changes, and follows the kind otherwise', async () => {
    show();

    await chooseKind('Roughness');
    expect(screen.getByLabelText('Name')).toHaveValue('Roughness');

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Grind massif' } });
    await chooseKind('Topographic position');
    expect(screen.getByLabelText('Name')).toHaveValue('Grind massif');
  });

  // The server's own bound, including which end is open: a light on the horizon lights nothing.
  it('refuses a light that does not stand above the horizon before sending anything', async () => {
    show();

    fireEvent.change(screen.getByLabelText('Light height (°)'), { target: { value: '0' } });
    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    expect(await screen.findByText(/stands above the horizon/)).toBeInTheDocument();
    expect(requestMutate).not.toHaveBeenCalled();
  });

  it('sends a colour relief as the bytes of its stops, and refuses one with too few', async () => {
    show();

    await chooseKind('Coloured relief');
    expect(screen.getAllByTestId('terrain-derivative-stop')).toHaveLength(3);

    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));
    await waitFor(() => expect(requestMutate).toHaveBeenCalledTimes(1));
    expect(sent().colourRamp).toEqual([
      { elevation: 200, red: 47, green: 133, blue: 90, alpha: 255 },
      { elevation: 1000, red: 233, green: 196, blue: 106, alpha: 255 },
      { elevation: 2500, red: 255, green: 255, blue: 255, alpha: 255 },
    ]);

    requestMutate.mockClear();
    const removals = screen.getAllByRole('button', { name: 'Remove this stop' });
    fireEvent.click(removals[2]);
    fireEvent.click(removals[1]);
    expect(screen.getAllByTestId('terrain-derivative-stop')).toHaveLength(1);

    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));
    expect(await screen.findByText(/at least two heights/)).toBeInTheDocument();
    expect(requestMutate).not.toHaveBeenCalled();
  });

  // Contour lines read one setting and none of the others. In particular there is no outermost
  // ring of cells to compute or leave blank, so that choice is neither shown nor sent: sent, it
  // would be recorded against a picture it never applied to.
  it('asks for contour lines at a spacing, and sends nothing a line does not read', async () => {
    show();

    await chooseKind('Contour lines');
    expect(screen.getByLabelText('Name')).toHaveValue('Contour lines');
    expect(screen.getByLabelText('A line every (m)')).toHaveValue('20');
    expect(screen.queryByTestId('terrain-derivative-edges')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Slope arithmetic')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('A line every (m)'), { target: { value: '25' } });
    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    await waitFor(() => expect(requestMutate).toHaveBeenCalledTimes(1));
    expect(sent()).toEqual({
      terrainBuildId: 'drawn',
      derivative: 'contours',
      name: 'Contour lines',
      lighting: null,
      azimuthDegrees: null,
      altitudeDegrees: null,
      zFactor: null,
      surfaceFit: null,
      slopeUnit: null,
      ruggednessFit: null,
      computeEdges: null,
      contourIntervalMetres: 25,
      colourRamp: null,
    });
  });

  // The server's own bounds. Lines no height apart are no lines, and lines further apart than any
  // ground is high are a finished picture with nothing on it.
  it('refuses a spacing no lines could be drawn at before sending anything', async () => {
    show();
    await chooseKind('Contour lines');

    fireEvent.change(screen.getByLabelText('A line every (m)'), { target: { value: '0' } });
    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    expect(await screen.findByText(/spaced from 1 to 1000 metres/)).toBeInTheDocument();
    expect(requestMutate).not.toHaveBeenCalled();
  });

  it('is shown to somebody who may only read, but cannot be sent by them', () => {
    show(false);

    expect(screen.getByTestId('terrain-derivative-needs-execute')).toHaveTextContent(
      'right to execute',
    );
    expect(screen.getByTestId('terrain-derivative-submit')).toBeDisabled();
    expect(screen.getByLabelText('Name')).toBeDisabled();
  });

  it('turns a refusal into the sentence it deserves', async () => {
    requestMutate.mockRejectedValue(new ApiError(404, 'terrain_derivative.build_not_found'));
    show();

    fireEvent.click(screen.getByTestId('terrain-derivative-submit'));

    expect(await screen.findByText(/nothing to draw the ground from/)).toBeInTheDocument();
  });
});
