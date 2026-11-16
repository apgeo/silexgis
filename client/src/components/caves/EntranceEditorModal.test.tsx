// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type OlMap from 'ol/Map';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { Entrance } from '../../api/hooks.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';

// Every map the dialog builds, in order: a second one is the defect.
const built = vi.hoisted(() => [] as OlMap[]);
vi.mock('ol/Map', async (original) => {
  const Actual = (await original<typeof import('ol/Map')>()).default;
  class Counted extends Actual {
    constructor(...args: ConstructorParameters<typeof Actual>) {
      super(...args);
      built.push(this);
    }
  }
  return { default: Counted };
});

const createEntrance = vi.fn();
const updateEntrance = vi.fn();
const fetchVersion = vi.fn();
vi.mock('../../api/hooks.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks.ts')>()),
  useEntranceTypes: () => ({ data: [{ id: 1, name: 'Shaft' }] }),
  useCreateEntrance: () => ({ mutateAsync: createEntrance, isPending: false }),
  useUpdateEntrance: () => ({ mutateAsync: updateEntrance, isPending: false }),
  useMapLayers: () => ({ isPending: false, data: [] }),
  fetchFeatureVersion: (id: string) => fetchVersion(id),
}));

const { default: EntranceEditorModal } = await import('./EntranceEditorModal.tsx');

function dialog(open: boolean, onClose: () => void = () => {}, entrance: Entrance | null = null) {
  return (
    <MemoryRouter>
      <App>
        <EntranceEditorModal caveId="cave-1" entrance={entrance} defaultCenter={[25, 45]} open={open} onClose={onClose} />
      </App>
    </MemoryRouter>
  );
}

/** The maps that are on the page right now. */
const attached = () => built.filter((map) => map.getTargetElement()?.isConnected);

beforeEach(() => {
  // The background choice reads the application's one shared map; it exists in every session.
  getWorkspaceMap();
  built.length = 0;
  createEntrance.mockReset();
  updateEntrance.mockReset();
  fetchVersion.mockReset();
  fetchVersion.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe('EntranceEditorModal', () => {
  it('keeps its map on screen while the page around it re-renders', async () => {
    const { rerender } = render(dialog(true));
    await waitFor(() => expect(attached()).toHaveLength(1));
    const map = attached()[0];

    // The page hands a fresh callback on each render, as a parent re-rendering for any reason does.
    for (let i = 0; i < 3; i += 1) {
      rerender(dialog(true, () => {}));
    }
    expect(attached()).toEqual([map]);
    expect(built).toHaveLength(1);
  });

  it('lets its map go when the dialog closes', async () => {
    const { rerender } = render(dialog(true));
    await waitFor(() => expect(attached()).toHaveLength(1));
    const map = attached()[0];

    rerender(dialog(false));
    await waitFor(() => expect(map.getTargetElement()).toBeNull());
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('keeps the survey date the entrance already carries when it is edited', async () => {
    updateEntrance.mockResolvedValue({});
    const onClose = vi.fn();
    render(dialog(true, onClose, surveyed));
    await waitFor(() => expect(screen.getByDisplayValue('Upper')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(updateEntrance).toHaveBeenCalledTimes(1));
    const { id, body } = updateEntrance.mock.calls[0][0] as { id: string; body: { surveyedAt: unknown; name: unknown } };
    expect(id).toBe('entrance-1');
    expect(body.name).toBe('Upper');
    expect(body.surveyedAt).toBe('2025-08-14');
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('leaves a form with an empty required field open, with no unhandled failure', async () => {
    const unhandled: unknown[] = [];
    const onUnhandled = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', onUnhandled);
    try {
      render(dialog(true));
      await waitFor(() => expect(attached()).toHaveLength(1));
      fireEvent.change(screen.getByLabelText('Longitude'), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'OK' }));
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(createEntrance).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
      // A new entrance has no version to hold.
      expect(fetchVersion).not.toHaveBeenCalled();
    } finally {
      process.off('unhandledRejection', onUnhandled);
    }
  });

  it('says why the server refused the save instead of only that it failed', async () => {
    updateEntrance.mockRejectedValue(new ApiError(403, 'entrance.location_forbidden'));
    render(dialog(true, () => {}, { ...surveyed, isMain: false }));
    await waitFor(() => expect(screen.getByDisplayValue('Upper')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText(/exact location is withheld from you/)).toBeTruthy();
  });

  it('saves against the version it was opened at, not one read while it was open', async () => {
    updateEntrance.mockResolvedValue({});
    fetchVersion.mockResolvedValue('"7"');
    render(dialog(true, () => {}, surveyed));
    await waitFor(() => expect(screen.getByDisplayValue('Upper')).toBeTruthy());
    await waitFor(() => expect(fetchVersion).toHaveBeenCalledWith('entrance-1'));

    // Somebody else saves meanwhile; any later read would answer with their version.
    fetchVersion.mockResolvedValue('"9"');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(updateEntrance).toHaveBeenCalledTimes(1));
    expect(updateEntrance.mock.calls[0][0]).toMatchObject({ id: 'entrance-1', ifMatch: '"7"' });
    expect(fetchVersion).toHaveBeenCalledTimes(1);
  });

  it('leaves a save somebody else overtook to the one conflict prompt, and stays open', async () => {
    updateEntrance.mockRejectedValue(new ApiError(412, 'concurrency.stale'));
    fetchVersion.mockResolvedValue('"7"');
    const onClose = vi.fn();
    render(dialog(true, onClose, surveyed));
    await waitFor(() => expect(screen.getByDisplayValue('Upper')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(updateEntrance).toHaveBeenCalledTimes(1));
    // The refusal is one the save can meet: it named the version the form was opened at.
    expect(updateEntrance.mock.calls[0][0]).toMatchObject({ ifMatch: '"7"' });
    await new Promise((resolve) => setTimeout(resolve, 50));
    // The shared mutation handler shows the reload-and-reapply prompt; a second, vaguer one here
    // would contradict it.
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
  });
});

const surveyed: Entrance = {
  id: 'entrance-1',
  caveId: 'cave-1',
  name: 'Upper',
  entranceTypeId: 1,
  isMain: true,
  geom: { type: 'Point', coordinates: [25.1, 45.2] },
  altitude: 820,
  description: null,
  positionQuality: 'gps',
  surveyedAt: '2025-08-14',
  approximateLocation: false,
} as Entrance;
